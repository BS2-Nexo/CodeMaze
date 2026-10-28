import { useEffect, useReducer, useRef, useState } from "react";
import { CodeMazeApi } from "../api/api.js"; // adjust paths to where the files live
import { MazeRun } from "../api/maze-model.js";
import { Maze } from "./Maze.jsx";
import GameOverModal from "./GameOverModal.jsx";
import GameMenue from "./GameMenue.jsx";
import GameInput from "./GameInput.jsx";
import LevelCompleteModal from "./LevelCompleteModal.jsx";
import PlayerDialog from "./PlayerDialog.jsx";

const api = new CodeMazeApi(import.meta.env.VITE_API_BASE_URL ?? "http://localhost:8080");

const MAZE_SIZE = 10;
const DIFFICULTY = "easy";
const LANGUAGE = "de"; // the block editor writes German keywords
const STEP_DELAY_MS = 120; // client-side pacing of the moves (the server is asked not to wait)

//const buttonClass =
//    "border-2 border-[#35B23A] px-3 py-1 text-xs font-semibold text-[#35B23A] hover:bg-[#35B23A]/20 disabled:cursor-not-allowed disabled:opacity-40";

export default function Game({ sessionId = null }) {
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
    const [score, setScore] = useState(0); // totalPoints from GET /api/scores/{playerId}/{sessionId}

    const runRef = useRef(null);
    const connRef = useRef(null);
    const inputRef = useRef(null);
    const programErrorsRef = useRef([]); // block-editor validation errors

    /** Replaces the current run with a fresh one on `maze`; closes the old connection. */
    function installRun(maze) {
        connRef.current?.close();
        connRef.current = null;
        runRef.current?.dispose();

        const next = new MazeRun(maze, { delayMs: STEP_DELAY_MS });
        next.subscribe((_event, model) => {
            rerender();
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
            connRef.current?.close();
            runRef.current?.dispose();
        };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    async function startRun() {
        if (!player || !rawMaze || busy) return;

        const blockErrors = programErrorsRef.current;
        if (blockErrors.length > 0) {
            setError(blockErrors[0].message);
            return;
        }

        setError(null);
        inputRef.current?.clearHighlight();
        setBusy(true);
        try {
            const res = await api.createRun({
                code: inputRef.current.getSource(),
                sessionId,
                language: LANGUAGE,
                maze: rawMaze,
                stepDelayMs: 0,
                sessionId: player.sessionId,
            });

            const next = installRun(res.maze);
            let gotFinished = false;
            connRef.current = api.openRun(res.webSocketPath, {
                onEvent: (event) => {
                    if (event.type === "finished") gotFinished = true;
                    next.enqueue(event);
                },
                onError: (e) => setError(e.message),
                onClose: () => {
                    // the connection dropped without a result: end the run so the UI does not hang
                    if (!gotFinished) {
                        next.enqueue({
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
        connRef.current?.cancel();
        resetMaze();
    }

    function logout() {
        if (active) cancelRun();
        setPlayer(null);
    }

    function restart() {
        resetMaze();
        programErrorsRef.current = [];
        setInputResetKey((key) => key + 1); // also clears the editor, as before
    }

    function closeModal() {
        setDismissed(true);
    }

    // what the rest of the UI knows as "status", taken from the model (so it follows the delayed playback)
    const model = run?.model ?? null;
    let status = "playing";
    if (model && !dismissed) {
        if (model.exitReached) status = "level complete";
        else if (model.status === "finished" && model.outcome !== "cancelled") status = "game over";
    }
    const moves = model?.steps ?? 0;

    const finished = model?.status === "finished";

    useEffect(() => {
        if (!player) {
            setScore(0);
            return;
        }
        let cancelled = false;
        api
            .playerScore(player.playerId, player.sessionId)
            .then((s) => !cancelled && setScore(s?.totalPoints ?? 0))
            .catch((e) => !cancelled && setError(e.message));
        return () => {
            cancelled = true;
        };
    }, [player, finished]);

    return (
        <main className="bg-[url('/assets/Background.png')] bg-center bg-contain font-[Silkscreen,monospace] text-white p-3 md:p-6">
            <div className="mx-auto w-full max-w-[1500px] border-4 border-[#272a28] bg-[#111] p-3">
                <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(340px,34%)] lg:items-start">
                    {/* the maze is square: as wide as its column, but never taller than the screen */}
                    <Maze
                        model={model}
                        transitionMs={Math.min(STEP_DELAY_MS, 150)}
                        className="mx-auto aspect-square w-full max-w-[calc(100vh-7rem)]"
                    />


                    <div className="flex min-w-0 flex-col gap-2">
                        <GameMenue
                            api={api}
                            score={score}
                            active={active}
                            busy={busy}
                            canStart={!!rawMaze}
                            player={player}
                            onLogin={() => setLoginOpen(true)}
                            onLogout={logout}
                            onStart={startRun}
                            onCancel={cancelRun}
                        />

                        {error && <p className="whitespace-pre-line text-xs text-red-400">{error}</p>}
                    </div>

                </div>

                {/* the editor spans the full width, below the maze and the menu */}
                <div className="mt-3">
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

            {status === "game over" && <GameOverModal moves={moves} restart={restart} onClose={closeModal} />}
            {status === "level complete" && <LevelCompleteModal moves={moves} restart={restart} onClose={closeModal} />}
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