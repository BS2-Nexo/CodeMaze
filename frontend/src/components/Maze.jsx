import { useEffect, useRef, useState } from "react";

// The server's maze has thin walls on the sides of cells. For a top-down tile view every cell
// becomes one tile and every wall a tile of its own, so a maze of w x h cells is drawn on a
// (2w+1) x (2h+1) tile grid:
//
//     # # # # #        cell (x, y) is the tile (2x+1, 2y+1)
//     # . . . #        wall between two cells: the tile in between
//     # # # . #        corner post: solid when any wall touches it
//     # . . . #
//     # # # # #
//
// The maze is drawn on a canvas, so even 100x100 cells (201x201 tiles) stay fast. Only the part
// that is visible is drawn. Small mazes are shown completely; big ones are shown zoomed in with
// the camera following the player. Mouse wheel / buttons zoom, dragging pans.
//
// Calm playback: the model may receive events much faster than a person can follow. The view
// therefore does not draw every event. It shows a "committed" snapshot (player, steps, breadcrumbs)
// that is refreshed in batches, and the camera glides towards the player instead of jumping.

const MAX_TILE = 96; // biggest tile size, in device pixels
const COMFORT_TILE = 20; // tile size (css px) used for mazes that don't fit the screen
const FIT_ENOUGH = 14; // if the whole maze fits with tiles at least this big (css px), show all of it

// --- calm playback -------------------------------------------------------------------------
const BATCH_EVENTS = 8; // when events pile up, this many are collected and drawn in one go (the player teleports)
const MIN_COMMIT_MS = 180; // ...but a new batch is never shown more often than this (about 5 per second)
const FLUSH_MS = 60; // if no new event arrived for this long (slow runs), show what we have right away
const CAM_TAU_MS = 100; // camera smoothing: roughly how long it takes to cover 2/3 of the way to the player
const DEAD_ZONE = 0.25; // the player may roam this far from the centre (fraction of the view size, per side)
//                         before the camera starts to move; it then glides to re-centre on the player

const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));

function loadImage(src, onLoad) {
    const img = new Image();
    img.onload = onLoad;
    img.src = src;
    return img;
}

/** Is the tile (tx, ty) a wall? */
function isWall(model, tx, ty, W, H) {
    const evenX = tx % 2 === 0;
    const evenY = ty % 2 === 0;

    if (!evenX && !evenY) return false; // a cell

    if (!evenX && evenY) {
        // wall above or below a cell
        const cx = (tx - 1) / 2;
        return ty === 0 ? model.hasWall(cx, 0, "top") : model.hasWall(cx, ty / 2 - 1, "bottom");
    }

    if (evenX && !evenY) {
        // wall left or right of a cell
        const cy = (ty - 1) / 2;
        return tx === 0 ? model.hasWall(0, cy, "left") : model.hasWall(tx / 2 - 1, cy, "right");
    }

    // corner post: only solid if a wall touches it, so open areas have no loose pillars and corners have no gaps
    return (
        (ty > 0 && isWall(model, tx, ty - 1, W, H)) ||
        (ty < H - 1 && isWall(model, tx, ty + 1, W, H)) ||
        (tx > 0 && isWall(model, tx - 1, ty, W, H)) ||
        (tx < W - 1 && isWall(model, tx + 1, ty, W, H))
    );
}

/** One byte per tile: 1 = wall. Walls never change during a run, so this is built once per maze. */
function buildWalls(model) {
    const W = 2 * model.width + 1;
    const H = 2 * model.height + 1;
    const walls = new Uint8Array(W * H);
    for (let ty = 0; ty < H; ty++) {
        for (let tx = 0; tx < W; tx++) {
            if (isWall(model, tx, ty, W, H)) walls[ty * W + tx] = 1;
        }
    }
    return walls;
}

/** Draws a MazeModel on a canvas. No React in here. */
class MazeView {
    constructor(canvas, onChange) {
        this.canvas = canvas;
        this.ctx = canvas.getContext("2d");
        this.onChange = onChange; // called with { follow, fit } when the view mode changes
        this.transitionMs = 100;

        this.model = null;
        this.walls = null;
        this.cols = 0;
        this.rows = 0;
        this.needsInit = false;

        this.W = 0; // canvas size in device pixels
        this.H = 0;
        this.dpr = 1;

        this.fitTd = 1; // tile size (device px) at which the whole maze fits
        this.zoomTd = null; // tile size when zoomed in; null = whole maze
        this.follow = true;
        this.pref = null; // the user's last choice: "all" | "follow" | null. Kept when a new maze is loaded.
        this.camX = 0; // top-left of the view, in tiles
        this.camY = 0;
        this.snapCam = true; // jump (instead of glide) to the target on the next frame
        this.targetX = null; // where the camera is gliding to (tiles); null = pick a new one
        this.targetY = null;
        this.lastDraw = 0;

        // what is currently shown: a snapshot of the model, refreshed in batches
        this.shown = null;
        this.seenVersion = -1;
        this.lastEventAt = 0;
        this.poll = 0;

        this.anim = { fromX: 0, fromY: 0, toX: 0, toY: 0, start: -1e9, dur: 0 };
        this.raf = 0;
        this.drag = null;

        const redraw = () => this.request();
        this.img = {
            wall: loadImage("/assets/Tile.png", redraw),
            exit: loadImage("/assets/FinishFlag.png", redraw),
            stand: loadImage("/assets/Player_stand.png", redraw),
            walk: loadImage("/assets/Player_walk.png", redraw),
        };

        this.onPointerDown = (e) => {
            if (!this.model) return;
            canvas.setPointerCapture(e.pointerId);
            this.drag = { x: e.clientX, y: e.clientY };
        };
        this.onPointerMove = (e) => {
            if (!this.drag) return;
            const dx = (e.clientX - this.drag.x) * this.dpr;
            const dy = (e.clientY - this.drag.y) * this.dpr;
            this.drag = { x: e.clientX, y: e.clientY };
            if (this.zoomTd === null) return; // whole maze is visible: nothing to pan
            if (this.follow) {
                this.follow = false;
                this.emit();
            }
            this.camX -= dx / this.td;
            this.camY -= dy / this.td;
            this.request();
        };
        this.onPointerUp = () => {
            this.drag = null;
        };
        this.onWheel = (e) => {
            e.preventDefault();
            const rect = canvas.getBoundingClientRect();
            this.zoomBy(e.deltaY < 0 ? 1.15 : 1 / 1.15, (e.clientX - rect.left) * this.dpr, (e.clientY - rect.top) * this.dpr);
        };
        canvas.addEventListener("pointerdown", this.onPointerDown);
        canvas.addEventListener("pointermove", this.onPointerMove);
        canvas.addEventListener("pointerup", this.onPointerUp);
        canvas.addEventListener("pointercancel", this.onPointerUp);
        canvas.addEventListener("wheel", this.onWheel, { passive: false });
    }

    destroy() {
        cancelAnimationFrame(this.raf);
        clearTimeout(this.poll);
        const c = this.canvas;
        c.removeEventListener("pointerdown", this.onPointerDown);
        c.removeEventListener("pointermove", this.onPointerMove);
        c.removeEventListener("pointerup", this.onPointerUp);
        c.removeEventListener("pointercancel", this.onPointerUp);
        c.removeEventListener("wheel", this.onWheel);
    }

    /** Tile size in device pixels. */
    get td() {
        return this.zoomTd ?? this.fitTd;
    }

    emit() {
        this.onChange?.({ follow: this.follow, fit: this.zoomTd === null });
    }

    request() {
        if (!this.raf) this.raf = requestAnimationFrame(() => this.draw());
    }

    /** Check again shortly: events are waiting for their batch. */
    schedulePoll() {
        if (this.poll) return;
        this.poll = setTimeout(() => {
            this.poll = 0;
            this.request();
        }, 30);
    }

    /* ---- setup ---- */

    setModel(model) {
        this.model = model;
        this.needsInit = true;
        this.shown = null; // the first frame of the new maze takes a fresh snapshot
        this.seenVersion = -1;
        this.snapCam = true;
        this.targetX = this.targetY = null;
        if (model) {
            this.cols = 2 * model.width + 1;
            this.rows = 2 * model.height + 1;
            this.walls = buildWalls(model);
        } else {
            this.walls = null;
        }
        this.recomputeFit();
        this.maybeInit();
        this.request();
    }

    resize(cssW, cssH, dpr) {
        this.dpr = dpr;
        this.W = Math.max(1, Math.round(cssW * dpr));
        this.H = Math.max(1, Math.round(cssH * dpr));
        this.canvas.width = this.W;
        this.canvas.height = this.H;
        this.recomputeFit();
        if (this.zoomTd !== null && this.zoomTd <= this.fitTd) this.zoomTd = null;
        this.snapCam = true;
        this.targetX = this.targetY = null;
        this.maybeInit();
        this.request();
    }

    recomputeFit() {
        const tiles = Math.max(this.cols, this.rows, 1);
        this.fitTd = Math.max(1, Math.floor(Math.min(this.W, this.H) / tiles));
    }

    /**
     * First view of a new maze. A choice the user made with the buttons ("Alles" / "Folgen") is kept;
     * otherwise: all of it if it fits comfortably, else zoomed in on the player.
     */
    maybeInit() {
        if (!this.needsInit || !this.model || !this.W) return;
        this.needsInit = false;
        const comfort = Math.round(COMFORT_TILE * this.dpr);
        if (this.pref === "all") {
            this.zoomTd = null;
            this.follow = false;
        } else if (this.pref === "follow") {
            this.zoomTd = comfort > this.fitTd ? comfort : null;
            this.follow = true;
        } else {
            this.zoomTd = this.fitTd >= Math.round(FIT_ENOUGH * this.dpr) ? null : Math.max(comfort, this.fitTd + 1);
            this.follow = true;
        }
        this.emit();
    }

    /* ---- view controls ---- */

    zoomBy(factor, ax = this.W / 2, ay = this.H / 2) {
        const old = this.td;
        let next = Math.round(old * factor);
        next = factor > 1 ? Math.max(next, old + 1) : Math.min(next, old - 1);
        next = clamp(next, this.fitTd, MAX_TILE);

        if (!this.follow) {
            // keep the point under the cursor where it is
            const wx = this.camX + ax / old;
            const wy = this.camY + ay / old;
            this.camX = wx - ax / next;
            this.camY = wy - ay / next;
        }
        this.zoomTd = next <= this.fitTd ? null : next;
        this.targetX = this.targetY = null;
        this.emit();
        this.request();
    }

    showAll() {
        this.pref = "all";
        this.zoomTd = null;
        this.follow = false;
        this.emit();
        this.request();
    }

    followPlayer() {
        this.pref = "follow";
        this.follow = true;
        this.targetX = this.targetY = null;
        if (this.zoomTd === null) {
            const comfort = Math.round(COMFORT_TILE * this.dpr);
            if (comfort > this.fitTd) this.zoomTd = comfort;
        }
        this.emit();
        this.request();
    }

    /* ---- batching ---- */

    /** Takes a snapshot of the model's current state and makes it the shown one. */
    commit(now) {
        const m = this.model;
        const prev = this.shown;
        const x = 2 * m.position.x + 1;
        const y = 2 * m.position.y + 1;

        const visited = new Uint8Array(m.width * m.height);
        for (let cy = 0; cy < m.height; cy++) {
            for (let cx = 0; cx < m.width; cx++) {
                if (m.isVisited(cx, cy)) visited[cy * m.width + cx] = 1;
            }
        }

        if (!prev) {
            this.anim = { fromX: x, fromY: y, toX: x, toY: y, start: -1e9, dur: 0 };
        } else {
            const events = m.version - prev.version;
            const dist = Math.abs(x - prev.x) + Math.abs(y - prev.y);
            if (events <= 1 && dist <= 2) {
                // a single, ordinary step: slide
                const cur = this.playerAt(now);
                this.anim = { fromX: cur.x, fromY: cur.y, toX: x, toY: y, start: now, dur: this.transitionMs };
            } else {
                // a whole batch: the player simply appears at the new place
                this.anim = { fromX: x, fromY: y, toX: x, toY: y, start: -1e9, dur: 0 };
            }
        }

        this.shown = {
            version: m.version,
            time: now,
            x,
            y,
            steps: m.steps,
            visited,
            exitReached: !!m.exitReached,
            status: m.status,
        };
    }

    /** Decides whether the model has moved on enough to show a new snapshot. */
    sync(now) {
        const m = this.model;
        if (!m) return;

        if (m.version !== this.seenVersion) {
            this.seenVersion = m.version;
            this.lastEventAt = now;
        }

        const s = this.shown;
        if (!s) {
            this.commit(now);
            return;
        }

        const pending = m.version - s.version;
        if (pending <= 0) return;

        // the end of the run (exit / finished) is always shown at once
        const ending = (m.exitReached && !s.exitReached) || (m.status === "finished" && s.status !== "finished");
        const batchFull = pending >= BATCH_EVENTS && now - s.time >= MIN_COMMIT_MS;
        const quiet = pending < BATCH_EVENTS && now - this.lastEventAt >= FLUSH_MS; // slow run or a pause

        if (ending || batchFull || quiet) this.commit(now);
    }

    /* ---- drawing ---- */

    playerAt(now) {
        const a = this.anim;
        const t = a.dur > 0 ? clamp((now - a.start) / a.dur, 0, 1) : 1;
        return { x: a.fromX + (a.toX - a.fromX) * t, y: a.fromY + (a.toY - a.fromY) * t };
    }

    sprite(img, fallback, x, y, size) {
        const ctx = this.ctx;
        if (img.complete && img.naturalWidth > 0) {
            ctx.drawImage(img, x, y, size, size);
        } else {
            ctx.fillStyle = fallback;
            ctx.fillRect(x, y, size, size);
        }
    }

    draw() {
        this.raf = 0;
        const { ctx, W, H, model: m } = this;
        if (!W || !H) return;

        ctx.imageSmoothingEnabled = false;
        ctx.fillStyle = "#000";
        ctx.fillRect(0, 0, W, H);
        if (!m) return;

        const now = performance.now();
        const dt = this.lastDraw ? Math.min(50, now - this.lastDraw) : 16;
        this.lastDraw = now;

        this.sync(now);
        const s = this.shown;
        if (!s) return;

        const td = this.td;
        const p = this.playerAt(now);
        const moving = this.anim.dur > 0 && now - this.anim.start < this.anim.dur;

        // camera: the player may be off-centre. Only when he leaves the dead zone around the centre
        // does the camera pick a new target (centred on him) and glide there, never jumping.
        const viewW = W / td;
        const viewH = H / td;
        const limit = (c, size, view) => (size <= view ? (size - view) / 2 : clamp(c, 0, size - view));
        let camMoving = false;
        if (this.follow) {
            const px = p.x + 0.5;
            const py = p.y + 0.5;
            const outX = Math.abs(px - (this.camX + viewW / 2)) > viewW * DEAD_ZONE;
            const outY = Math.abs(py - (this.camY + viewH / 2)) > viewH * DEAD_ZONE;
            if (this.targetX === null || outX) this.targetX = px - viewW / 2;
            if (this.targetY === null || outY) this.targetY = py - viewH / 2;
            const targetX = limit(this.targetX, this.cols, viewW);
            const targetY = limit(this.targetY, this.rows, viewH);
            if (this.snapCam) {
                this.camX = targetX;
                this.camY = targetY;
                this.snapCam = false;
            } else {
                const k = 1 - Math.exp(-dt / CAM_TAU_MS);
                this.camX += (targetX - this.camX) * k;
                this.camY += (targetY - this.camY) * k;
                camMoving = Math.abs(targetX - this.camX) * td > 0.5 || Math.abs(targetY - this.camY) * td > 0.5;
            }
        }
        this.camX = limit(this.camX, this.cols, viewW);
        this.camY = limit(this.camY, this.rows, viewH);

        const ox = -Math.round(this.camX * td);
        const oy = -Math.round(this.camY * td);
        const x0 = Math.max(0, Math.floor(this.camX));
        const x1 = Math.min(this.cols - 1, Math.ceil(this.camX + viewW));
        const y0 = Math.max(0, Math.floor(this.camY));
        const y1 = Math.min(this.rows - 1, Math.ceil(this.camY + viewH));

        // walls
        for (let y = y0; y <= y1; y++) {
            for (let x = x0; x <= x1; x++) {
                if (this.walls[y * this.cols + x]) this.sprite(this.img.wall, "#555", x * td + ox, y * td + oy, td);
            }
        }

        // breadcrumbs on visited cells (as of the shown snapshot)
        ctx.fillStyle = "rgba(53, 178, 58, 0.45)";
        const q = Math.max(1, Math.round(td / 4));
        const sz = Math.max(1, td - 2 * q);
        const cx0 = Math.max(0, Math.floor((x0 - 1) / 2));
        const cx1 = Math.min(m.width - 1, Math.floor(x1 / 2));
        const cy0 = Math.max(0, Math.floor((y0 - 1) / 2));
        const cy1 = Math.min(m.height - 1, Math.floor(y1 / 2));
        for (let cy = cy0; cy <= cy1; cy++) {
            for (let cx = cx0; cx <= cx1; cx++) {
                if (s.visited[cy * m.width + cx]) {
                    ctx.fillRect((2 * cx + 1) * td + ox + q, (2 * cy + 1) * td + oy + q, sz, sz);
                }
            }
        }

        // exit and player
        this.sprite(this.img.exit, "#ffb900", (2 * m.exit.x + 1) * td + ox, (2 * m.exit.y + 1) * td + oy, td);
        const body = s.steps % 2 === 1 ? this.img.walk : this.img.stand;
        this.sprite(body, "#35B23A", Math.round(p.x * td) + ox, Math.round(p.y * td) + oy, td);

        if (moving || camMoving) this.request();
        if (m.version !== s.version) this.schedulePoll(); // events are waiting for their batch
    }
}

const toolButton =
    "min-w-7 border-2 border-white/40 bg-black/70 px-1.5 py-0.5 text-[10px] font-semibold text-white hover:bg-white/20 aria-pressed:border-[#ffca20] aria-pressed:text-[#ffca20]";

/**
 * Square view of the maze. Give it a square size through `className` (e.g. "aspect-square w-full").
 *
 * @param model         a MazeModel (run.model); null while no maze is loaded
 * @param transitionMs  how long the player sprite takes to slide to the next cell
 * @param fast          true while the "Schnell" mode (no pacing of the moves) is on
 * @param onToggleFast  called when the "Schnell" button is pressed; the button is hidden without it
 * @param className     sizing classes for the outer element
 */
export function Maze({ model, transitionMs = 100, fast = false, onToggleFast, className = "" }) {
    const boxRef = useRef(null);
    const canvasRef = useRef(null);
    const viewRef = useRef(null);
    const [mode, setMode] = useState({ follow: true, fit: true });

    useEffect(() => {
        const view = new MazeView(canvasRef.current, setMode);
        viewRef.current = view;
        const observer = new ResizeObserver(([entry]) => {
            const { width, height } = entry.contentRect;
            view.resize(width, height, window.devicePixelRatio || 1);
        });
        observer.observe(boxRef.current);
        return () => {
            observer.disconnect();
            view.destroy();
            viewRef.current = null;
        };
    }, []);

    useEffect(() => {
        viewRef.current?.setModel(model);
    }, [model]);

    // the model is changed in place: redraw whenever it has seen a new event
    const version = model?.version;
    useEffect(() => {
        viewRef.current?.request();
    }, [version]);

    useEffect(() => {
        if (viewRef.current) viewRef.current.transitionMs = transitionMs;
    }, [transitionMs]);

    const p = model?.position;
    const view = () => viewRef.current;

    return (
        <section ref={boxRef} className={`relative min-h-0 min-w-0 overflow-hidden bg-black ${className}`}>
            <canvas
                ref={canvasRef}
                role="img"
                aria-label={
                    p ? `Labyrinth game board. Player at column ${p.x + 1}, row ${p.y + 1}.` : "Labyrinth game board"
                }
                className="absolute inset-0 h-full w-full cursor-grab touch-none [image-rendering:pixelated] active:cursor-grabbing"
            />

            {!model && (
                <span className="absolute inset-0 flex items-center justify-center text-xs text-white/60">
                    Lade Labyrinth …
                </span>
            )}

            {model && (
                <div className="absolute right-1 top-1 flex gap-1">
                    <button type="button" className={toolButton} aria-label="Vergrößern" onClick={() => view()?.zoomBy(1.25)}>
                        +
                    </button>
                    <button type="button" className={toolButton} aria-label="Verkleinern" onClick={() => view()?.zoomBy(1 / 1.25)}>
                        −
                    </button>
                    <button type="button" className={toolButton} aria-pressed={mode.fit && !mode.follow} onClick={() => view()?.showAll()}>
                        Alles
                    </button>
                    <button type="button" className={toolButton} aria-pressed={mode.follow && !mode.fit} onClick={() => view()?.followPlayer()}>
                        Folgen
                    </button>
                    {onToggleFast && (
                        <button type="button" className={toolButton} aria-pressed={fast} onClick={onToggleFast}>
                            Schnell
                        </button>
                    )}
                </div>
            )}
        </section>
    );
}