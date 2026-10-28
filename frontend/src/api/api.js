/**
 * CodeMaze API client: an ES module with no dependencies, for browsers.
 *
 *   import { CodeMazeApi, normalizeMaze } from "./codemaze-api.js";
 *   const api = new CodeMazeApi("http://localhost:5139");
 *   const caps = await api.capabilities();
 *
 * Note: ES modules cannot be loaded from file:// URLs. Serve this file over
 * http(s), for example from wwwroot.
 */

/* ------------------------------------------------------------------ */
/* Errors                                                              */
/* ------------------------------------------------------------------ */

/** Thrown for any non-2xx response. `body` is the parsed JSON, or the raw text. */
export class ApiError extends Error {
  constructor(status, body) {
    super(ApiError.describe(status, body));
    this.name = "ApiError";
    this.status = status;
    this.body = body;
  }

  /** Turns the server's different error shapes into one readable string. */
static describe(status, body) {
  if (typeof body === "string") return body || `HTTP ${status}`;

  if (body?.errors) {
    // our own compile errors: [{ source, message, line, column }]
    if (Array.isArray(body.errors)) {
      return body.errors
        .map(e => {
          const at = e.line != null ? ` (line ${e.line}${e.column != null ? ":" + e.column : ""})` : "";
          return `[${e.source}] ${e.message}${at}`;
        })
        .join("\n");
    }
    // ASP.NET validation errors: { Field: ["message", ...] }
    if (typeof body.errors === "object") {
      return Object.entries(body.errors)
        .map(([field, msgs]) => `${field}: ${[].concat(msgs).join(", ")}`)
        .join("\n");
    }
  }

  if (body?.error) return body.error + (body.lockedUntil ? ` until ${body.lockedUntil}` : "");
  if (body?.title) return body.title;
  return `HTTP ${status}`;
}
}

/* ------------------------------------------------------------------ */
/* Client                                                              */
/* ------------------------------------------------------------------ */

export class CodeMazeApi {
  /** @param {string} [baseUrl] Empty string means same origin. */
  constructor(baseUrl = "") {
    this.baseUrl = baseUrl.replace(/\/+$/, "");
  }

  async #request(method, path, body) {
    const res = await fetch(this.baseUrl + path, {
      method,
      headers: body !== undefined ? { "Content-Type": "application/json" } : undefined,
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
    const text = await res.text();
    let data = text;
    try {
      data = text ? JSON.parse(text) : null;
    } catch {
      /* plain-text body, keep as string */
    }
    if (!res.ok) throw new ApiError(res.status, data);
    return data;
  }

  /* ---- capabilities ---- */

  /** GET /api/capabilities */
  capabilities() {
    return this.#request("GET", "/api/capabilities");
  }

  /* ---- mazes ---- */

  /**
   * GET /api/maze/{difficulty}?size=  (or /api/maze?size= when difficulty is omitted)
   * @param {number} size
   * @param {string} [difficulty] e.g. "easy" | "medium" | "hard"
   */
  generateMaze(size, difficulty) {
    const d = difficulty ? "/" + encodeURIComponent(difficulty) : "";
    return this.#request("GET", `/api/maze${d}?size=${encodeURIComponent(size)}`);
  }

  /* ---- players ---- */

  /** POST /api/players/reserve. A 409 ApiError means the name is taken. */
  reservePlayer(displayName) {
    return this.#request("POST", "/api/players/reserve", { displayName });
  }

  /** POST /api/players/confirm (204 on success). */
  confirmPlayer(displayName, code) {
    return this.#request("POST", "/api/players/confirm", { displayName, code });
  }

  /** POST /api/players/verify. Returns a new session. */
  verifyPlayer(displayName, code) {
    return this.#request("POST", "/api/players/verify", { displayName, code });
  }

  /* ---- scores ---- */

  /** GET /api/scores/leaderboard. Pass top = 0 for all rows. */
  leaderboard(top = 10) {
    return this.#request("GET", `/api/scores/leaderboard?top=${encodeURIComponent(top)}`);
  }

/** GET /api/scores/{playerId}/{sessionId}. Returns null while the session has no scores (404). */
async playerScore(playerId, sessionId) {
  try {
    return await this.#request(
      "GET",
      `/api/scores/${encodeURIComponent(playerId)}/${encodeURIComponent(sessionId)}`
    );
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) return null;
    throw e;
  }
}

  /* ---- runs ---- */

  /**
   * POST /api/runs. Compiles on the server; compile errors arrive as an ApiError (400).
   * @param {{
   *   code: string,
   *   sessionId: string,
   *   language?: string|null,
   *   languageDefinition?: string|null,
   *   maze?: object|null,
   *   size?: number|null,
   *   difficulty?: string|null,
   *   stepDelayMs?: number|null
   * }} request
   * @returns {Promise<{ runId: string, webSocketPath: string, maze: object }>}
   */
  createRun(request) {
    return this.#request("POST", "/api/runs", request);
  }

  /**
   * Opens the run's WebSocket. The interpreter starts on connect.
   * @param {string} webSocketPath from createRun()
   * @param {{
   *   onEvent?: (event: object) => void,
   *   onClose?: () => void,
   *   onError?: (error: Error) => void
   * }} [handlers]
   * @returns {{ cancel: () => void, close: () => void }}
   */
  openRun(webSocketPath, { onEvent, onClose, onError } = {}) {
    const url = new URL(this.baseUrl + webSocketPath, location.href);
    url.protocol = url.protocol === "https:" ? "wss:" : "ws:";
    const ws = new WebSocket(url);

    ws.onmessage = m => {
      try {
        onEvent?.(JSON.parse(m.data));
      } catch (e) {
        onError?.(e);
      }
    };
    ws.onclose = () => onClose?.();
    ws.onerror = () => onError?.(new Error("WebSocket error"));

    return {
      cancel: () => ws.readyState === WebSocket.OPEN && ws.send(JSON.stringify({ type: "cancel" })),
      close: () => ws.close(),
    };
  }
}

/* ------------------------------------------------------------------ */
/* Maze helpers                                                        */
/* ------------------------------------------------------------------ */

/**
 * Settings for decoding the maze JSON. Adjust them if your MazeSerializer
 * encodes walls differently. Pass overrides to normalizeMaze().
 */
export const defaultMazeFormat = {
  wallBits: { n: 1, e: 2, s: 4, w: 8 }, // numeric values of the Walls flags
  bitMeansWall: true,                   // false if a set bit means "open passage"
};

const DIRECTION_NAMES = {
  north: "n", n: "n", top: "n", up: "n",
  east: "e", e: "e", right: "e",
  south: "s", s: "s", bottom: "s", down: "s",
  west: "w", w: "w", left: "w",
};

const pick = (o, ...keys) => {
  for (const k of keys) if (o?.[k] !== undefined) return o[k];
};

/** Accepts {x,y}, {X,Y}, [x,y] or value-wrapped coordinates. Returns {x, y}. */
export function toPos(p) {
  if (Array.isArray(p)) return { x: p[0], y: p[1] };
  const x = pick(p, "x", "X");
  const y = pick(p, "y", "Y");
  return { x: x?.value ?? x, y: y?.value ?? y };
}

function cellToWalls(cell, { wallBits, bitMeansWall }) {
  let set = { n: false, e: false, s: false, w: false };

  if (typeof cell === "number") {
    for (const k in set) set[k] = (cell & wallBits[k]) !== 0;
  } else if (typeof cell === "string") {
    for (const part of cell.toLowerCase().split(/[,\s|]+/).filter(Boolean)) {
      if (part === "all") set = { n: true, e: true, s: true, w: true };
      else if (DIRECTION_NAMES[part]) set[DIRECTION_NAMES[part]] = true;
    }
  } else if (Array.isArray(cell)) {
    for (const v of cell) {
      if (typeof v === "number") {
        for (const k in set) if (v & wallBits[k]) set[k] = true;
      } else if (DIRECTION_NAMES[String(v).toLowerCase()]) {
        set[DIRECTION_NAMES[String(v).toLowerCase()]] = true;
      }
    }
  } else if (cell && typeof cell === "object") {
    for (const [k, v] of Object.entries(cell)) {
      const dir = DIRECTION_NAMES[k.toLowerCase()];
      if (dir && v) set[dir] = true;
    }
  }

  if (!bitMeansWall) for (const k in set) set[k] = !set[k];
  return set;
}

/**
 * Converts the server's maze JSON into a friendlier shape.
 * y grows downward; North means y - 1.
 * @returns {{
 *   width: number, height: number,
 *   entrance: {x:number,y:number}, exit: {x:number,y:number},
 *   walls: Array<{n:boolean,e:boolean,s:boolean,w:boolean}>,  // row-major: y * width + x
 *   raw: object                                               // original, resend as `maze` in createRun()
 * }}
 */
export function normalizeMaze(raw, format = {}) {
  const fmt = { ...defaultMazeFormat, ...format };
  const width = pick(raw, "width", "Width");
  const height = pick(raw, "height", "Height");
  const cells = pick(raw, "cells", "Cells");
  if (!width || !height || !cells) {
    throw new Error("Unrecognised maze JSON: " + JSON.stringify(raw).slice(0, 200));
  }
  const flat = Array.isArray(cells[0]) ? cells.flat() : cells;
  return {
    width,
    height,
    entrance: toPos(pick(raw, "entrance", "Entrance")),
    exit: toPos(pick(raw, "exit", "Exit")),
    walls: flat.map(c => cellToWalls(c, fmt)),
    raw,
  };
}