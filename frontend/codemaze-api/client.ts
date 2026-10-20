import { openRun, type RunConnection, type RunHandlers } from "./runConnection";
import type {
  Capabilities,
  CreateRunRequest,
  CreateRunResponse,
  Difficulty,
  Maze,
  RunError,
} from "./types";

/**
 * Thrown for every non-2xx answer. The server uses three different 400 shapes
 * (run errors, plain text, ASP.NET problem details); they are all turned into this.
 */
export class ApiError extends Error {
  readonly status: number;
  /** Filled for compile / language errors: use these to mark lines in the editor. */
  readonly runErrors: RunError[];

  constructor(status: number, message: string, runErrors: RunError[] = []) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.runErrors = runErrors;
  }
}

export interface CodeMazeApi {
  getCapabilities(signal?: AbortSignal): Promise<Capabilities>;
  generateMaze(size: number, difficulty?: Difficulty, signal?: AbortSignal): Promise<Maze>;
  /** Step 1: compiles the code and settles on a maze. Rejects with an ApiError (check `runErrors`). */
  createRun(request: CreateRunRequest, signal?: AbortSignal): Promise<CreateRunResponse>;
  /** Step 2: opens the WebSocket for a prepared run. Call within 60 seconds, once per run. */
  connectRun(run: CreateRunResponse, handlers: RunHandlers): RunConnection;
}

export interface CodeMazeApiOptions {
  /** e.g. "http://localhost:5000". Leave empty when the API is served from the same origin. */
  baseUrl?: string;
}

export function createCodeMazeApi(options: CodeMazeApiOptions = {}): CodeMazeApi {
  const baseUrl = (options.baseUrl ?? "").replace(/\/+$/, "");

  return {
    getCapabilities: (signal) => requestJson<Capabilities>(`${baseUrl}/api/capabilities`, { signal }),

    generateMaze: (size, difficulty, signal) => {
      const query = new URLSearchParams({ size: String(size) });
      if (difficulty) query.set("difficulty", difficulty);
      return requestJson<Maze>(`${baseUrl}/api/maze?${query}`, { signal });
    },

    createRun: (request, signal) =>
      requestJson<CreateRunResponse>(`${baseUrl}/api/runs`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(request),
        signal,
      }),

    connectRun: (run, handlers) => openRun(toWebSocketUrl(baseUrl, run.webSocketPath), handlers),
  };
}

// ------------------------------------------------------------------ internals

async function requestJson<T>(url: string, init: RequestInit): Promise<T> {
  const response = await fetch(url, init);
  if (!response.ok) throw await toApiError(response);
  return (await response.json()) as T;
}

function toWebSocketUrl(baseUrl: string, path: string): string {
  // Absolute baseUrl wins; an empty one resolves against the page's origin.
  const url = new URL(baseUrl + path, window.location.origin);
  url.protocol = url.protocol === "https:" ? "wss:" : "ws:";
  return url.toString();
}

async function toApiError(response: Response): Promise<ApiError> {
  const raw = await response.text().catch(() => "");
  const fallback = `${response.status} ${response.statusText}`.trim();

  let parsed: unknown = raw;
  if ((response.headers.get("content-type") ?? "").includes("json")) {
    try {
      parsed = JSON.parse(raw);
    } catch {
      // keep the raw text
    }
  }

  // 1. { errors: RunError[] }: compile / language errors
  if (isRunErrorBody(parsed)) {
    const message = parsed.errors.map(formatRunError).join("\n");
    return new ApiError(response.status, message || fallback, parsed.errors);
  }

  // 2. ASP.NET problem details: { title, errors?: Record<string, string[]> }
  if (isProblemDetails(parsed)) {
    const details = Object.values(parsed.errors ?? {}).flat();
    const message = details.length > 0 ? details.join("\n") : (parsed.detail ?? parsed.title ?? fallback);
    return new ApiError(response.status, message);
  }

  // 3. plain text (or a JSON string)
  if (typeof parsed === "string" && parsed.trim() !== "") {
    return new ApiError(response.status, parsed);
  }

  return new ApiError(response.status, fallback);
}

function formatRunError(error: RunError): string {
  return error.line != null ? `Line ${error.line}: ${error.message}` : error.message;
}

function isRunErrorBody(value: unknown): value is { errors: RunError[] } {
  return typeof value === "object" && value !== null && Array.isArray((value as { errors?: unknown }).errors);
}

interface ProblemDetails {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

function isProblemDetails(value: unknown): value is ProblemDetails {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}