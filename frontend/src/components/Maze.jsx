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

const MAX_TILE = 96; // biggest tile size, in device pixels
const COMFORT_TILE = 20; // tile size (css px) used for mazes that don't fit the screen
const FIT_ENOUGH = 14; // if the whole maze fits with tiles at least this big (css px), show all of it

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
        this.camX = 0; // top-left of the view, in tiles
        this.camY = 0;

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

    /* ---- setup ---- */

    setModel(model) {
        this.model = model;
        this.needsInit = true;
        if (model) {
            this.cols = 2 * model.width + 1;
            this.rows = 2 * model.height + 1;
            this.walls = buildWalls(model);
            const x = 2 * model.position.x + 1;
            const y = 2 * model.position.y + 1;
            this.anim = { fromX: x, fromY: y, toX: x, toY: y, start: -1e9, dur: 0 };
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
        this.maybeInit();
        this.request();
    }

    recomputeFit() {
        const tiles = Math.max(this.cols, this.rows, 1);
        this.fitTd = Math.max(1, Math.floor(Math.min(this.W, this.H) / tiles));
    }

    /** First view of a new maze: all of it if it fits comfortably, otherwise zoomed in on the player. */
    maybeInit() {
        if (!this.needsInit || !this.model || !this.W) return;
        this.needsInit = false;
        const comfort = Math.round(COMFORT_TILE * this.dpr);
        this.zoomTd = this.fitTd >= Math.round(FIT_ENOUGH * this.dpr) ? null : Math.max(comfort, this.fitTd + 1);
        this.follow = true;
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
        this.emit();
        this.request();
    }

    showAll() {
        this.zoomTd = null;
        this.follow = false;
        this.emit();
        this.request();
    }

    followPlayer() {
        this.follow = true;
        if (this.zoomTd === null) {
            const comfort = Math.round(COMFORT_TILE * this.dpr);
            if (comfort > this.fitTd) this.zoomTd = comfort;
        }
        this.emit();
        this.request();
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
        const td = this.td;

        // player: slide from the old tile to the new one
        const tx = 2 * m.position.x + 1;
        const ty = 2 * m.position.y + 1;
        if (this.anim.toX !== tx || this.anim.toY !== ty) {
            const cur = this.playerAt(now);
            this.anim = { fromX: cur.x, fromY: cur.y, toX: tx, toY: ty, start: now, dur: this.transitionMs };
        }
        const p = this.playerAt(now);
        const moving = this.anim.dur > 0 && now - this.anim.start < this.anim.dur;

        // camera: follow the player, and keep the view inside the maze (or centred if the maze is smaller)
        const viewW = W / td;
        const viewH = H / td;
        if (this.follow) {
            this.camX = p.x + 0.5 - viewW / 2;
            this.camY = p.y + 0.5 - viewH / 2;
        }
        const limit = (c, size, view) => (size <= view ? (size - view) / 2 : clamp(c, 0, size - view));
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

        // breadcrumbs on visited cells
        ctx.fillStyle = "rgba(53, 178, 58, 0.45)";
        const q = Math.max(1, Math.round(td / 4));
        const s = Math.max(1, td - 2 * q);
        const cx0 = Math.max(0, Math.floor((x0 - 1) / 2));
        const cx1 = Math.min(m.width - 1, Math.floor(x1 / 2));
        const cy0 = Math.max(0, Math.floor((y0 - 1) / 2));
        const cy1 = Math.min(m.height - 1, Math.floor(y1 / 2));
        for (let cy = cy0; cy <= cy1; cy++) {
            for (let cx = cx0; cx <= cx1; cx++) {
                if (m.isVisited(cx, cy)) ctx.fillRect((2 * cx + 1) * td + ox + q, (2 * cy + 1) * td + oy + q, s, s);
            }
        }

        // exit and player
        this.sprite(this.img.exit, "#ffb900", (2 * m.exit.x + 1) * td + ox, (2 * m.exit.y + 1) * td + oy, td);
        const body = m.steps % 2 === 1 ? this.img.walk : this.img.stand;
        this.sprite(body, "#35B23A", Math.round(p.x * td) + ox, Math.round(p.y * td) + oy, td);

        if (moving) this.request();
    }
}

const toolButton =
    "min-w-7 border-2 border-white/40 bg-black/70 px-1.5 py-0.5 text-[10px] font-semibold text-white hover:bg-white/20 aria-pressed:border-[#ffca20] aria-pressed:text-[#ffca20]";

/**
 * Square view of the maze. Give it a square size through `className` (e.g. "aspect-square w-full").
 *
 * @param model         a MazeModel (run.model); null while no maze is loaded
 * @param transitionMs  how long the player sprite takes to slide to the next cell
 * @param className     sizing classes for the outer element
 */
export function Maze({ model, transitionMs = 100, className = "" }) {
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
                </div>
            )}
        </section>
    );
}