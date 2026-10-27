import type { RunEvent } from "./types";

export interface RunHandlers {
  onEvent(event: RunEvent): void;
  /**
   * Called once when the socket closes. `finished` is false when the run ended without a
   * "finished" event: connection lost, run id expired or already used, or closed by you.
   */
  onClose?(info: { finished: boolean }): void;
}

export interface RunConnection {
  /** Asks the server to stop. The run still ends with a "finished" event (outcome "cancelled"). */
  cancel(): void;
  /** Closes the socket. The server treats this as a cancel too, but you get no more events. */
  close(): void;
}

/** Opens the run's WebSocket. The server starts the interpreter as soon as it is connected. */
export function openRun(url: string, handlers: RunHandlers): RunConnection {
  const socket = new WebSocket(url);
  let finished = false;
  let cancelRequested = false;

  const sendCancel = () => socket.send(JSON.stringify({ type: "cancel" }));

  socket.onopen = () => {
    if (cancelRequested) sendCancel(); // cancel() was called while still connecting
  };

  socket.onmessage = (message: MessageEvent) => {
    if (typeof message.data !== "string") return;

    let event: RunEvent;
    try {
      event = JSON.parse(message.data) as RunEvent;
    } catch {
      return; // not JSON, ignore
    }

    if (event.type === "finished") finished = true;
    handlers.onEvent(event);
  };

  socket.onclose = () => handlers.onClose?.({ finished });

  return {
    cancel() {
      if (socket.readyState === WebSocket.CONNECTING) cancelRequested = true;
      else if (socket.readyState === WebSocket.OPEN) sendCancel();
    },
    close() {
      if (socket.readyState === WebSocket.CONNECTING || socket.readyState === WebSocket.OPEN) {
        socket.close();
      }
    },
  };
}