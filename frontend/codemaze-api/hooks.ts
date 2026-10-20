import { useCallback, useEffect, useReducer, useRef, useState } from "react";
import type { CodeMazeApi } from "./client";
import type { RunConnection } from "./runConnection";
import type {
  Capabilities,
  CreateRunRequest,
  FinishedEvent,
  Maze,
  MoveBlockedEvent,
  Position,
  RunEvent,
} from "./types";

// ---------------------------------------------------------------- capabilities

export interface CapabilitiesState {
  capabilities: Capabilities | null;
  loading: boolean;
  error: Error | null;
}

/** Loads GET /api/capabilities once. `api` must be a stable reference (module level or useMemo). */
export function useCapabilities(api: CodeMazeApi): CapabilitiesState {
  const [state, setState] = useState<CapabilitiesState>({ capabilities: null, loading: true, error: null });

  useEffect(() => {
    const abort = new AbortController();

    api.getCapabilities(abort.signal).then(
      (capabilities) => setState({ capabilities, loading: false, error: null }),
      (error: unknown) => {
        if (abort.signal.aborted) return;
        setState({ capabilities: null, loading: false, error: toError(error) });
      },
    );

    return () => abort.abort();
  }, [api]);

  return state;
}

// ------------------------------------------------------------------- running

/**
 * idle       nothing started
 * preparing  POST /api/runs in flight (compiling, generating the maze)
 * running    socket open, events arriving
 * finished   the server sent its "finished" event (see `result.outcome`)
 * failed     the request was rejected (ApiError, see `runErrors`) or the connection was lost
 */
export type RunStatus = "idle" | "preparing" | "running" | "finished" | "failed";

export interface RunState {
  status: RunStatus;
  maze: Maze | null;
  /** Where the runner currently is. */
  position: Position | null;
  /** Every position walked so far, starting with the entrance. */
  path: Position[];
  steps: number;
  /** The most recent blocked move (only happens when the server's onCollision is "ignore"). */
  lastBlocked: MoveBlockedEvent | null;
  exitReached: boolean;
  result: FinishedEvent | null;
  error: Error | null;
}

const initialState: RunState = {
  status: "idle",
  maze: null,
  position: null,
  path: [],
  steps: 0,
  lastBlocked: null,
  exitReached: false,
  result: null,
  error: null,
};

type Action =
  | { type: "reset" }
  | { type: "preparing" }
  | { type: "prepared"; maze: Maze }
  | { type: "event"; event: RunEvent }
  | { type: "failed"; error: Error }
  | { type: "connectionLost" };

function reducer(state: RunState, action: Action): RunState {
  switch (action.type) {
    case "reset":
      return initialState;

    case "preparing":
      return { ...initialState, status: "preparing" };

    case "prepared":
      return { ...state, status: "running", maze: action.maze, position: action.maze.entrance };

    case "failed":
      return { ...state, status: "failed", error: action.error };

    case "connectionLost":
      return state.status === "running"
        ? { ...state, status: "failed", error: new Error("The connection to the server was lost.") }
        : state;

    case "event": {
      const event = action.event;
      switch (event.type) {
        case "started":
          return { ...state, position: event.position, path: [event.position] };
        case "moved":
          return { ...state, position: event.to, path: [...state.path, event.to], steps: event.step };
        case "moveBlocked":
          return { ...state, lastBlocked: event };
        case "exitReached":
          return { ...state, exitReached: true };
        case "finished":
          return { ...state, status: "finished", position: event.position, steps: event.steps, result: event };
      }
    }
  }
}

export interface MazeRun {
  state: RunState;
  /** Prepares and starts a run. Call it from an event handler, not from an effect. */
  start(request: CreateRunRequest): Promise<void>;
  /** Asks the server to stop. `state.status` becomes "finished" with outcome "cancelled" shortly after. */
  cancel(): void;
  /** Stops everything and goes back to idle. */
  reset(): void;
}

/**
 * Drives one run at a time. Starting a new run stops the previous one; unmounting stops the current one.
 * `api` must be a stable reference (module level or useMemo).
 */
export function useMazeRun(api: CodeMazeApi): MazeRun {
  const [state, dispatch] = useReducer(reducer, initialState);

  const connection = useRef<RunConnection | null>(null);
  const abort = useRef<AbortController | null>(null);
  // Identifies the current run, so late results of an older one are ignored.
  const attempt = useRef(0);

  const teardown = useCallback(() => {
    attempt.current += 1;
    abort.current?.abort();
    abort.current = null;
    connection.current?.close(); // the server treats a closed socket as a cancel
    connection.current = null;
  }, []);

  const start = useCallback(
    async (request: CreateRunRequest) => {
      teardown();
      const mine = attempt.current;
      const controller = new AbortController();
      abort.current = controller;

      dispatch({ type: "preparing" });

      try {
        const run = await api.createRun(request, controller.signal);
        if (mine !== attempt.current) return;

        dispatch({ type: "prepared", maze: run.maze });

        connection.current = api.connectRun(run, {
          onEvent: (event) => {
            if (mine === attempt.current) dispatch({ type: "event", event });
          },
          onClose: ({ finished }) => {
            if (mine === attempt.current && !finished) dispatch({ type: "connectionLost" });
          },
        });
      } catch (error) {
        if (mine !== attempt.current) return; // superseded or aborted
        dispatch({ type: "failed", error: toError(error) });
      }
    },
    [api, teardown],
  );

  const cancel = useCallback(() => connection.current?.cancel(), []);

  const reset = useCallback(() => {
    teardown();
    dispatch({ type: "reset" });
  }, [teardown]);

  useEffect(() => teardown, [teardown]);

  return { state, start, cancel, reset };
}

function toError(error: unknown): Error {
  return error instanceof Error ? error : new Error(String(error));
}