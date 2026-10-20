// Types for the CodeMaze.Server API (see docs/API.md).
// Coordinates are zero-based; y is the row (0 = top), x is the column (0 = left).

export interface Position {
  y: number;
  x: number;
}

export type Wall = "top" | "right" | "bottom" | "left";

export interface Cell {
  y: number;
  x: number;
  walls: Wall[];
}

export interface Maze {
  width: number;
  height: number;
  entrance: Position;
  exit: Position;
  /** Every cell, row by row (when it comes from the server). */
  cells: Cell[];
}

export type Direction = "up" | "down" | "left" | "right" | "back";
export type Difficulty = "easy" | "medium" | "hard";

// ------------------------------------------------------------ GET /api/capabilities

export interface LanguageInfo {
  /** Send this as `language` when creating a run. */
  name: string;
  displayName: string;
}

export interface Capabilities {
  languages: LanguageInfo[];
  difficulties: Difficulty[];
  defaultDifficulty: Difficulty;
  maze: { minSize: number; maxSize: number; maxCells: number };
  run: {
    maxCodeLength: number;
    maxLanguageDefinitionLength: number;
    minStepDelayMs: number;
    maxStepDelayMs: number;
    defaultStepDelayMs: number;
  };
  interpreter: {
    /** "crash": hitting a wall ends the run. "ignore": a moveBlocked event is sent and the run continues. */
    onCollision: "crash" | "ignore";
    /** true: the run ends as soon as the exit is reached. */
    stopAtExit: boolean;
  };
}

// ------------------------------------------------------------ POST /api/runs

interface RunSettings {
  code: string;
  /** Use this maze as is. If omitted, `size` (and `difficulty`) are used to generate one. */
  maze?: Maze;
  size?: number;
  difficulty?: Difficulty;
  /** Pause after each "moved" event, for animation. 0 to 2000, default 100. */
  stepDelayMs?: number;
}

/** Exactly one of the two: the name of a language the server has, or the text (TOML) of a custom one. */
type LanguageChoice =
  | { language: string; languageDefinition?: never }
  | { languageDefinition: string; language?: never };

export type CreateRunRequest = RunSettings & LanguageChoice;

export interface CreateRunResponse {
  runId: string;
  webSocketPath: string;
  /** The maze that will be used (the generated one if you did not send one). */
  maze: Maze;
}

/** One entry of a 400 response about the language definition or the program. */
export interface RunError {
  source: "code" | "language";
  message: string;
  /** 1-based; null when the error has no position. */
  line: number | null;
  column: number | null;
}

// ------------------------------------------------------------ WebSocket events

export type RunOutcome =
  | "exitReached"
  | "programEnded"
  | "crashed"
  | "cancelled"
  | "limitExceeded"
  | "fault";

export interface StartedEvent {
  type: "started";
  position: Position;
  exit: Position;
}

export interface MovedEvent {
  type: "moved";
  from: Position;
  to: Position;
  direction: Direction;
  /** Running number of successful moves. */
  step: number;
}

export interface MoveBlockedEvent {
  type: "moveBlocked";
  position: Position;
  direction: Direction;
  reason: "wall" | "noHistory";
}

export interface ExitReachedEvent {
  type: "exitReached";
  position: Position;
  steps: number;
}

/** Always the last event of a run, also after a cancel. */
export interface FinishedEvent {
  type: "finished";
  outcome: RunOutcome;
  message: string | null;
  position: Position;
  steps: number;
  instructions: number;
}

export type RunEvent =
  | StartedEvent
  | MovedEvent
  | MoveBlockedEvent
  | ExitReachedEvent
  | FinishedEvent;