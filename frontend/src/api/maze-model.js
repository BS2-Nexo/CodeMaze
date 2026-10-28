/**
 * Maze state for a renderer: an ES module with no dependencies and no DOM access.
 *
 *   import { CodeMazeApi } from "./codemaze-api.js";
 *   import { MazeRun } from "./maze-model.js";
 *
 *   const { webSocketPath, maze } = await api.createRun({ ... });
 *   const run = new MazeRun(maze, { delayMs: 120 });
 *   run.subscribe((event, model) => requestAnimationFrame(() => draw(model)));
 *   draw(run.model);
 *   const conn = api.openRun(webSocketPath, { onEvent: (e) => run.enqueue(e) });
 *
 * MazeModel  = the maze plus the run state (position, visited cells, path, outcome).
 *              apply(event) changes it, one server event at a time.
 * MazeRun    = a MazeModel plus a queue: events that arrive faster than they should be
 *              shown are held back and applied one by one with a delay in between.
 *
 * Coordinates are zero-based, y grows downward (row 0 is the top row), like the API.
 */

export const SIDES = ["top", "right", "bottom", "left"];

const BIT = { top: 1, right: 2, bottom: 4, left: 8 };
const DIRECTION_TO_SIDE = { up: "top", right: "right", down: "bottom", left: "left" };

const toPos = (p) => ({ x: p.x, y: p.y });

/* ------------------------------------------------------------------ */
/* MazeModel                                                           */
/* ------------------------------------------------------------------ */

export class MazeModel {
  #walls;
  #visited;
  #position;
  #path;
  #version = 0;

  /** Run state; read these from the renderer, don't assign them. */
  status = "ready";          // "ready" | "running" | "finished"
  steps = 0;
  instructions = null;       // known once the run has finished
  outcome = null;            // "exitReached", "crashed", ... (see the API docs), once finished
  message = null;
  exitReached = false;
  /** The last move that was refused: { position, direction, reason }, or null. Cleared by the next move. */
  blocked = null;

  /** @param raw the maze JSON as the server sends it (see "Maze format" in the API docs) */
  constructor(raw) {
    const { width, height, entrance, exit, cells } = raw ?? {};
    if (!Number.isInteger(width) || !Number.isInteger(height) || width < 1 || height < 1 || !Array.isArray(cells)) {
      throw new Error("Unrecognised maze JSON: " + JSON.stringify(raw)?.slice(0, 200));
    }
    this.width = width;
    this.height = height;
    this.entrance = toPos(entrance);
    this.exit = toPos(exit);

    this.#walls = new Uint8Array(width * height);
    this.#visited = new Uint8Array(width * height);

    for (const c of cells) {
      if (!this.inBounds(c.x, c.y)) continue;
      let bits = 0;
      for (const w of c.walls ?? []) bits |= BIT[w] ?? 0;
      this.#walls[c.y * width + c.x] = bits;
    }

    // A wall between two cells counts from both sides, even if the server lists it for only one.
    const w = this.#walls;
    for (let y = 0; y < height; y++) {
      for (let x = 0; x < width; x++) {
        const i = y * width + x;
        if (w[i] & BIT.right && x + 1 < width) w[i + 1] |= BIT.left;
        if (w[i] & BIT.left && x > 0) w[i - 1] |= BIT.right;
        if (w[i] & BIT.bottom && y + 1 < height) w[i + width] |= BIT.top;
        if (w[i] & BIT.top && y > 0) w[i - width] |= BIT.bottom;
      }
    }

    this.#position = toPos(this.entrance);
    this.#path = [toPos(this.entrance)];
    this.#visited[this.#index(this.entrance.x, this.entrance.y)] = 1;
  }

  /* ---- reading (for the renderer) ---- */

  inBounds(x, y) {
    return Number.isInteger(x) && Number.isInteger(y) && x >= 0 && y >= 0 && x < this.width && y < this.height;
  }

  /** Is there a wall on this side of the cell? Anything outside the maze counts as wall. */
  hasWall(x, y, side) {
    if (!this.inBounds(x, y)) return true;
    return (this.#walls[this.#index(x, y)] & BIT[side]) !== 0;
  }

  /** Has the player been on this cell during this run? */
  isVisited(x, y) {
    return this.inBounds(x, y) && this.#visited[this.#index(x, y)] === 1;
  }

  /** Where the player is now: { x, y }. */
  get position() {
    return this.#position;
  }

  /** Cells walked so far, entrance first, current position last. A step back removes the last one. Don't modify. */
  get path() {
    return this.#path;
  }

  /** Increases with every applied event: cheap way to see whether a redraw is needed. */
  get version() {
    return this.#version;
  }

  #index(x, y) {
    return y * this.width + x;
  }

  /* ---- changing (called with the server's events) ---- */

  /** Applies one WebSocket event. Returns false for event types it does not know. */
  apply(event) {
    switch (event?.type) {
      case "started":
        this.status = "running";
        this.#moveTo(toPos(event.position));
        this.#path = [toPos(event.position)];
        if (event.exit) this.exit = toPos(event.exit);
        break;

      case "moved": {
        const to = toPos(event.to);
        if (event.direction === "back") {
          if (this.#path.length > 1) this.#path.pop();
          const last = this.#path[this.#path.length - 1];
          if (!last || last.x !== to.x || last.y !== to.y) this.#path.push(to);
        } else {
          this.#path.push(to);
        }
        this.#moveTo(to);
        this.steps = event.step ?? this.steps + 1;
        this.blocked = null;
        break;
      }

      case "moveBlocked":
        this.blocked = {
          position: toPos(event.position),
          direction: event.direction,
          side: DIRECTION_TO_SIDE[event.direction] ?? null, // null for "back"
          reason: event.reason, // "wall" | "noHistory"
        };
        break;

      case "exitReached":
        this.exitReached = true;
        this.#moveTo(toPos(event.position));
        this.steps = event.steps ?? this.steps;
        break;

      case "finished":
        this.status = "finished";
        this.outcome = event.outcome;
        this.message = event.message ?? null;
        this.steps = event.steps ?? this.steps;
        this.instructions = event.instructions ?? null;
        if (event.position) this.#moveTo(toPos(event.position));
        break;

      default:
        return false;
    }
    this.#version++;
    return true;
  }

  #moveTo(pos) {
    this.#position = pos;
    if (this.inBounds(pos.x, pos.y)) this.#visited[this.#index(pos.x, pos.y)] = 1;
  }
}

/* ------------------------------------------------------------------ */
/* MazeRun: the model plus a delayed event queue                        */
/* ------------------------------------------------------------------ */

/** Events that are shown one after another with a pause. The others are applied as soon as it is their turn. */
const PACED = new Set(["moved", "moveBlocked"]);

export class MazeRun {
  #queue = [];
  #listeners = new Set();
  #timer = null;
  #readyAt = 0;
  #paused = false;
  #pumping = false;

  /**
   * @param raw the maze JSON from createRun() (or an existing MazeModel)
   * @param {{ delayMs?: number }} [options] pause after each move event; can be changed any time
   */
  constructor(raw, { delayMs = 100 } = {}) {
    this.model = raw instanceof MazeModel ? raw : new MazeModel(raw);
    this.delayMs = delayMs;
  }

  /** Number of events received but not yet applied. */
  get pending() {
    return this.#queue.length;
  }

  /** True once `finished` was applied and nothing is waiting any more. */
  get done() {
    return this.model.status === "finished" && this.#queue.length === 0;
  }

  /** fn(event, model) is called after every applied event. Returns an unsubscribe function. */
  subscribe(fn) {
    this.#listeners.add(fn);
    return () => this.#listeners.delete(fn);
  }

  /** Hand over a WebSocket event. It is applied now if its turn has come, otherwise after the delay. */
  enqueue(event) {
    this.#queue.push(event);
    this.#pump();
  }

  pause() {
    this.#paused = true;
    this.#stopTimer();
  }

  resume() {
    this.#paused = false;
    this.#pump();
  }

  /** Apply everything that is waiting right now, without delays (e.g. a "skip" button). */
  flush() {
    this.#stopTimer();
    while (this.#queue.length) this.#applyNext();
  }

  /** Drop everything that is waiting and stop. */
  dispose() {
    this.#stopTimer();
    this.#queue.length = 0;
    this.#listeners.clear();
  }

  #pump() {
    if (this.#pumping || this.#paused) return;
    this.#pumping = true;
    this.#stopTimer();
    try {
      while (this.#queue.length) {
        const wait = this.#readyAt - performance.now();
        if (wait > 0) {
          this.#timer = setTimeout(() => {
            this.#timer = null;
            this.#pump();
          }, wait);
          return;
        }
        this.#applyNext();
      }
    } finally {
      this.#pumping = false;
    }
  }

  #applyNext() {
    const event = this.#queue.shift();
    this.model.apply(event);
    if (PACED.has(event.type)) this.#readyAt = performance.now() + this.delayMs;
    for (const fn of this.#listeners) {
      try {
        fn(event, this.model);
      } catch (e) {
        console.error("MazeRun listener failed:", e);
      }
    }
  }

  #stopTimer() {
    if (this.#timer !== null) clearTimeout(this.#timer);
    this.#timer = null;
  }
}