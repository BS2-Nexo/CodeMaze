import { useEffect, useReducer, useRef, useState } from "react";
import { CodeMazeApi } from "../api/api.js"; // adjust paths to where the files live
import { MazeRun } from "../api/maze-model.js";
import { Maze } from "./Maze.jsx";
import GameOverModal from "./GameOverModal.jsx";
import GameMenue from "./GameMenue.jsx";
import GameInput from "./GameInput.jsx";
import LevelCompleteModal from "./LevelCompleteModal.jsx";
import PlayerDialog from "./PlayerDialog.jsx";
import GauntletModal from "./GauntletModal.jsx";
import { buildGauntlet, difficultyLabel, outcomeLabel } from "../api/gauntlet.js";

const api = new CodeMazeApi(import.meta.env.VITE_API_BASE_URL ?? "http://localhost:8080");

const MAZE_SIZE = 10;
const DIFFICULTY = "easy";
const LANGUAGE = "de"; // the block editor writes German keywords
const STEP_DELAY_MS = 120; // client-side pacing of the moves in a normal run (the server is asked not to wait)
const GAUNTLET_DELAY_MS = 30; // pacing of the moves during the gauntlet
// The "Schnell" button on the maze switches the pacing off: events are applied as they arrive.

/**
 * Hands the server's events to the MazeRun one by one, `delay()` ms after each move
 * (0 = all at once). The delay is read again for every event, so the speed can change during a run.
 */
function createPump(run, delay) {
    const queue = [];
    let timer = 0;
    let rush = false; // pass everything straight through (the run is being cancelled)

    function release() {
        timer = 0;
        while (queue.length) {
            const event = queue.shift();
            run.enqueue(event);
            const ms = rush ? 0 : delay();
            if (ms > 0 && event.type === "moved") {
                timer = setTimeout(release, ms);
                return;
            }
        }
    }

    return {
        push(event) {
            queue.push(event);
            if (!timer) release();
        },
        /** The speed changed: apply it now instead of after the pending wait. */
        kick() {
            clearTimeout(timer);
            release();
        },
        /** Everything that is still waiting is delivered at once, and so is everything that follows. */
        rushAll() {
            rush = true;
            this.kick();
        },
        stop() {
            clearTimeout(timer);
            timer = 0;
            queue.length = 0;
        },
    };
}

export default function Game() {
    const [rawMaze, setRawMaze] = useState(null); // maze JSON as sent by the server
    const [run, setRun] = useState(null); // MazeRun: model + delayed event queue
    const [active, setActive] = useState(false); // a run is connected / still being animated
    const [busy, setBusy] = useState(false); // waiting for POST /api/runs
    const [dismissed, setDismissed] = useState(false); // result modal closed
    const [error, setError] = useState(null);
    const [inputResetKey, setInputResetKey] = useState(0);
    const [, rerender] = useReducer((n) => n + 1, 0);
    const [player, setPlayer] = useState(null); // { sessionId, playerId, publicId, displayName }
    const [loginOpen, setLoginOpen] = useState(false);
    const [view, setView] = useState("maze"); // "maze" | "code"
    const [fast, setFast] = useState(false); // "Schnell": no pacing of the moves

    // gauntlet
    const [capabilities, setCapabilities] = useState(null); // GET /api/capabilities
    const [offerGauntlet, setOfferGauntlet] = useState(true); // only offered on the first completion
    const [gauntlet, setGauntlet] = useState(null); // null | { phase, progress?, result? }

    const runRef = useRef(null);
    const connRef = useRef(null);
    const inputRef = useRef(null);
    const programErrorsRef = useRef([]); // block-editor validation errors
    const gauntletAbortRef = useRef(false);
    const rerenderQueued = useRef(false);
    const fastRef = useRef(false);
    const paceRef = useRef(STEP_DELAY_MS); // pacing of the current run when not in fast mode
    const pumpRef = useRef(null);

    function toggleFast() {
        fastRef.current = !fastRef.current;
        setFast(fastRef.current);
        pumpRef.current?.kick();
    }

    /** At most one React render per frame, however many events arrive (thousands per second on big mazes). */
    function scheduleRerender() {
        if (rerenderQueued.current) return;
        rerenderQueued.current = true;
        requestAnimationFrame(() => {
            rerenderQueued.current = false;
            rerender();
        });
    }

    /** Replaces the current run with a fresh one on `maze`; closes the old connection. */
    function installRun(maze) {
        connRef.current?.close();
        connRef.current = null;
        pumpRef.current?.stop();
        pumpRef.current = null;
        runRef.current?.dispose();

        // pacing is done by the pump (so it can change during a run), the MazeRun applies events at once
        const next = new MazeRun(maze, { delayMs: 0 });
        next.subscribe((_event, model) => {
            scheduleRerender();
            if (model.status === "finished") setActive(false);
        });
        runRef.current = next;
        setRun(next);
        setActive(false);
        setDismissed(false);
        return next;
    }

    // load a maze when the game opens
    useEffect(() => {
        let cancelled = false;
        api
            .generateMaze(MAZE_SIZE, DIFFICULTY)
            .then((maze) => {
                if (cancelled) return;
                setRawMaze(maze);
                installRun(maze);
            })
            .catch((e) => !cancelled && setError(e.message));
        return () => {
            cancelled = true;
            gauntletAbortRef.current = true;
            connRef.current?.close();
            pumpRef.current?.stop();
            runRef.current?.dispose();
        };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    // what the server supports (difficulties, size limits)
    useEffect(() => {
        let cancelled = false;
        api
            .capabilities()
            .then((c) => !cancelled && setCapabilities(c))
            .catch((e) => !cancelled && setError(e.message));
        return () => {
            cancelled = true;
        };
    }, []);

    /**
     * Prepares a run on `maze` with `code` and opens the socket.
     * Resolves after POST /api/runs succeeded (throws on compile errors etc.) with
     * { next, done }; `done` resolves with the model once the exit is reached or the run is finished.
     */
    async function launch(code, maze, paceMs = STEP_DELAY_MS) {
        const res = await api.createRun({
            code,
            language: LANGUAGE,
            maze,
            stepDelayMs: 0,
            sessionId: player.sessionId,
        });

        const next = installRun(res.maze);
        paceRef.current = paceMs;
        const pump = createPump(next, () => (fastRef.current ? 0 : paceRef.current));
        pumpRef.current = pump;
        let gotFinished = false;

        const done = new Promise((resolve) => {
            next.subscribe((_e, model) => {
                if (model.exitReached || model.status === "finished") resolve(model);
            });
        });

        connRef.current = api.openRun(res.webSocketPath, {
            onEvent: (event) => {
                if (event.type === "finished") gotFinished = true;
                pump.push(event);
            },
            onError: (e) => setError(e.message),
            onClose: () => {
                // the connection dropped without a result: end the run so the UI does not hang
                if (!gotFinished) {
                    pump.push({
                        type: "finished",
                        outcome: "fault",
                        message: "Verbindung getrennt",
                        steps: next.model.steps,
                        instructions: null,
                    });
                }
            },
        });
        setActive(true);
        return { next, done };
    }

    async function startRun() {
        if (!player || !rawMaze || busy || gauntlet) return;

        const blockErrors = programErrorsRef.current;
        if (blockErrors.length > 0) {
            setError(blockErrors[0].message);
            return;
        }

        setError(null);
        inputRef.current?.clearHighlight();
        setBusy(true);
        try {
            await launch(inputRef.current.getSource(), rawMaze); // busy only covers the POST
            setView("maze");
        } catch (e) {
            setError(e.message);
            const list = e.body?.errors; // compile errors: [{ source, message, line, column }]
            if (Array.isArray(list)) inputRef.current?.highlightError(list.filter((x) => x.source === "code"));
        } finally {
            setBusy(false);
        }
    }

    /** Back to the start of the same maze. */
    function resetMaze() {
        if (rawMaze) installRun(rawMaze);
        setError(null);
    }

    /** Stops the running program: tells the server to cancel, then returns to the start of the maze. */
    function cancelRun() {
        pumpRef.current?.rushAll();
        connRef.current?.cancel();
        resetMaze();
    }

    function logout() {
        if (active) cancelRun();
        setPlayer(null);
    }

    /** Back to the start of the same maze, keeping the user's code. */
    function retry() {
        resetMaze();
        setView("code");
    }

    async function nextLevel() {
        const maze = await api.generateMaze(MAZE_SIZE, DIFFICULTY);
        setRawMaze(maze);
        installRun(maze);
        setError(null);
        setView("code");
    }

    /** Full restart: also clears the editor. */
    function restart() {
        resetMaze();
        programErrorsRef.current = [];
        setInputResetKey((key) => key + 1);
        setView("code");
    }

    function closeModal() {
        setDismissed(true);
    }

    // ---------------------------------------------------------------- gauntlet

    function gauntletFailed(message) {
        setGauntlet({ phase: "done", result: { success: false, completed: 0, total: 0, message } });
    }

    /** Runs the user's program on a fixed ladder of mazes: difficulty ascending, size ascending. */
    async function runGauntlet({ selected }) {
        if (!player) return gauntletFailed("Bitte zuerst anmelden.");
        if (programErrorsRef.current.length > 0) return gauntletFailed(programErrorsRef.current[0].message);

        const { minSize, maxSize } = capabilities.maze; // always up to the server's maximum
        const plan = buildGauntlet(capabilities.difficulties, selected, minSize, maxSize);
        const code = inputRef.current.getSource(); // the user's existing program
        gauntletAbortRef.current = false;
        setError(null);
        setView("maze");

        let completed = 0;
        let skipped = 0;
        let message = null;

        for (let i = 0; i < plan.length; i++) {
            if (gauntletAbortRef.current) {
                message = "Abgebrochen";
                break;
            }
            const stage = plan[i];
            setGauntlet({ phase: "running", progress: { index: i, total: plan.length, stage } });

            let maze;
            try {
                maze = await api.generateMaze(stage.size, stage.difficulty);
            } catch (e) {
                if (e.status === 404) {
                    skipped++; // no generator configured for this combination
                    continue;
                }
                message = e.message;
                break;
            }

            try {
                setRawMaze(maze);
                const { done } = await launch(code, maze, GAUNTLET_DELAY_MS);
                const model = await done;
                connRef.current?.cancel(); // needed when stopAtExit is false; harmless otherwise
                if (gauntletAbortRef.current) {
                    message = "Abgebrochen";
                    break;
                }
                if (!model.exitReached) {
                    message =
                        `Gescheitert bei ${difficultyLabel(stage.difficulty)} ${stage.size}×${stage.size}` +
                        (model.outcome ? `: ${outcomeLabel(model.outcome)}` : "");
                    break;
                }
                completed++;
                await new Promise((r) => setTimeout(r, 400)); // let the player see the exit reached
            } catch (e) {
                // compile errors come back from POST /api/runs as e.body.errors
                message = e.body?.errors?.[0]?.message ?? e.message;
                break;
            }
        }

        const total = plan.length - skipped;
        setActive(false);
        setGauntlet({
            phase: "done",
            result: {
                success: !message && total > 0 && completed === total,
                completed,
                total,
                message: message ?? (skipped ? `${skipped} Stufe(n) übersprungen (kein Generator)` : null),
            },
        });
    }

    function cancelGauntlet() {
        gauntletAbortRef.current = true;
        pumpRef.current?.rushAll();
        connRef.current?.cancel();
    }

    /** After the gauntlet: back to an easy MAZE_SIZE x MAZE_SIZE maze, the user's code stays. */
    async function closeGauntlet() {
        setOfferGauntlet(false);
        try {
            const maze = await api.generateMaze(MAZE_SIZE, DIFFICULTY);
            setRawMaze(maze);
            installRun(maze);
        } catch (e) {
            setError(e.message);
            if (rawMaze) installRun(rawMaze);
        }
        setGauntlet(null); // only now, so the finished run doesn't pop up the level-complete modal
        setView("code");
    }

    // ------------------------------------------------------------------ status

    // what the rest of the UI knows as "status", taken from the model (so it follows the delayed playback);
    // the per-run modals are suppressed while a gauntlet is in progress
    const model = run?.model ?? null;
    let status = "playing";
    if (model && !dismissed && !gauntlet) {
        if (model.exitReached) status = "level complete";
        else if (model.status === "finished" && model.outcome !== "cancelled") status = "game over";
    }
    const moves = model?.steps ?? 0;

    const showGauntletOffer = status === "level complete" && offerGauntlet && !!capabilities;

    return (
        <main className="min-h-dvh bg-black bg-[url('/assets/Background.png')] bg-center bg-contain font-[Silkscreen,monospace] text-white p-3 md:p-6">
            <div className="mx-auto w-full max-w-[1500px] border-4 border-[#272a28] bg-[#111] p-3">
                <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(340px,34%)]">
                    {/* one square box; maze and editor are stacked inside it, only one is visible */}
                    <div className="relative mx-auto aspect-square w-full max-w-[calc(100vh-7rem)]">
                        <Maze
                            model={model}
                            transitionMs={fast ? 0 : Math.min(gauntlet ? GAUNTLET_DELAY_MS : STEP_DELAY_MS, 150)}
                            fast={fast}
                            onToggleFast={toggleFast}
                            className={`absolute inset-0 h-full w-full ${view === "maze" ? "" : "invisible"}`}
                        />

                        <div
                            className={`absolute inset-0 overflow-auto [scrollbar-width:none] [&::-webkit-scrollbar]:hidden ${view === "code" ? "" : "invisible"
                                }`}
                        >
                            <GameInput
                                key={inputResetKey}
                                ref={inputRef}
                                status={status}
                                onProgramChange={({ errors }) => {
                                    programErrorsRef.current = errors;
                                }}
                            />
                        </div>
                    </div>

                    {/* the column is as tall as the maze box; on lg the menu is laid over it */}
                    <div
                        className={`relative min-w-0 ${gauntlet?.phase === "running" ? "pointer-events-none opacity-60" : ""}`}
                    >
                        <div className="lg:absolute lg:inset-0">
                            <GameMenue
                                api={api}
                                active={active}
                                busy={busy}
                                canStart={!!rawMaze}
                                player={player}
                                view={view}
                                onViewChange={setView}
                                onLogin={() => setLoginOpen(true)}
                                onLogout={logout}
                                onStart={startRun}
                                onCancel={cancelRun}
                            />
                        </div>
                    </div>
                </div>

                {error && <p className="mt-2 whitespace-pre-line text-xs text-red-400">{error}</p>}
            </div>

            {status === "game over" && <GameOverModal moves={moves} restart={restart} onClose={closeModal} />}

            {(gauntlet || showGauntletOffer) && (
                <GauntletModal
                    phase={gauntlet?.phase ?? "offer"}
                    capabilities={capabilities}
                    result={gauntlet?.result}
                    onStart={runGauntlet}
                    onCancel={() => {
                        if (gauntlet?.phase === "running") cancelGauntlet();
                        else setOfferGauntlet(false); // "Beenden": falls through to the normal level-complete modal
                    }}
                    onClose={closeGauntlet}
                />
            )}

            {status === "level complete" && !showGauntletOffer && (
                <LevelCompleteModal moves={moves} restart={retry} onClose={closeModal} />
            )}

            <PlayerDialog
                api={api}
                open={loginOpen}
                onClose={(result) => {
                    setLoginOpen(false);
                    if (result) setPlayer(result);
                }}
            />
        </main>
    );
}