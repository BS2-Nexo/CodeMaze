import { useEffect, useRef, useState } from "react";
import { ApiError } from "../api/api.js"; // adjust paths to where the files live

const inputClass =
    "w-full border-2 border-[#35B23A] bg-black px-2 py-1 text-sm text-white outline-none focus:bg-[#35B23A]/10";
const buttonClass =
    "border-2 border-[#35B23A] px-3 py-1 text-xs font-semibold text-[#35B23A] hover:bg-[#35B23A]/20 disabled:cursor-not-allowed disabled:opacity-40";

const ERROR_TEXT = {
    invalid_name: "Dieser Name ist nicht zulässig.",
    confirmation_failed: "Der Code stimmt nicht überein. Versuche es noch einmal.",
    wrong_code: "Falscher Code. Versuche es noch einmal.",
    unknown_player: "Zu diesem Namen gibt es keinen Spieler.",
};

/**
 * Login / registration dialog for the player endpoints.
 *
 * Props:
 *   api      CodeMazeApi instance
 *   open     whether the dialog is shown
 *   onClose  called with { sessionId, playerId, publicId, displayName } on success,
 *            or with null when the dialog was cancelled
 */
export default function PlayerDialog({ api, open, onClose }) {
    const [step, setStep] = useState("name"); // "name" | "login" | "confirm" | "locked"
    const [name, setName] = useState("");
    const [code, setCode] = useState("");
    const [reserved, setReserved] = useState(null); // response of POST /api/players/reserve
    const [error, setError] = useState(null);
    const [lockedUntil, setLockedUntil] = useState(null);
    const [now, setNow] = useState(() => Date.now());
    const [busy, setBusy] = useState(false); // waiting for the server

    const inputRef = useRef(null);

    // fresh state every time the dialog opens
    useEffect(() => {
        if (!open) return;
        setStep("name");
        setName("");
        setCode("");
        setReserved(null);
        setError(null);
        setLockedUntil(null);
        setBusy(false);
    }, [open]);

    // focus the input whenever the step changes
    useEffect(() => {
        if (open) inputRef.current?.focus();
    }, [open, step]);

    // tick while locked, so the countdown runs and the retry button unlocks by itself
    useEffect(() => {
        if (step !== "locked") return;
        setNow(Date.now());
        const timer = setInterval(() => setNow(Date.now()), 1000);
        return () => clearInterval(timer);
    }, [step]);

    if (!open) return null;

    const lockEnd = lockedUntil ? new Date(lockedUntil).getTime() : NaN;
    const secondsLeft = Number.isNaN(lockEnd) ? 0 : Math.max(0, Math.ceil((lockEnd - now) / 1000));

    function lock(until) {
        setLockedUntil(until ?? null);
        setCode("");
        setError(null);
        setStep("locked");
    }

    /** Maps the server's { error: "<code>" } answers onto the dialog's steps. */
    function handleFailure(e) {
        const kind = e instanceof ApiError ? e.body?.error : null;
        const until = e instanceof ApiError ? e.body?.lockedUntil : null;

        if (kind === "locked" || (e instanceof ApiError && e.status === 429)) {
            lock(until);
        } else if (kind === "wrong_code") {
            if (until) {
                lock(until); // this was the attempt that locked the name
            } else {
                setCode("");
                setError(ERROR_TEXT.wrong_code);
            }
        } else if (kind === "unknown_player") {
            setStep("name");
            setCode("");
            setError(ERROR_TEXT.unknown_player);
        } else if (kind && ERROR_TEXT[kind]) {
            if (kind === "confirmation_failed") setCode("");
            setError(ERROR_TEXT[kind]);
        } else {
            setError(e.message); // network errors and everything unexpected
        }
    }

    async function submitName() {
        const displayName = name.trim();
        if (!displayName) return setError("Bitte gib einen Namen ein.");
        setName(displayName);
        setError(null);
        setBusy(true);
        try {
            const res = await api.reservePlayer(displayName);
            setReserved(res); // contains the code to show and the session key
            setCode("");
            setStep("confirm"); // the name was free: the player retypes the code
        } catch (e) {
            if (e instanceof ApiError && e.status === 409) {
                setCode("");
                setStep("login"); // the name is taken: ask for the code
            } else {
                handleFailure(e);
            }
        } finally {
            setBusy(false);
        }
    }

    async function submitCode() {
        const typed = code.trim();
        if (!typed) return setError("Bitte gib den Code ein.");
        setError(null);
        setBusy(true);
        try {
            if (step === "confirm") {
                await api.confirmPlayer(name, typed);
                onClose({
                    sessionId: reserved.sessionId,
                    playerId: reserved.id,
                    publicId: reserved.publicId,
                    displayName: reserved.displayName,
                });
            } else {
                const res = await api.verifyPlayer(name, typed);
                onClose({
                    sessionId: res.sessionId,
                    playerId: res.playerId,
                    publicId: res.publicId,
                    displayName: res.displayName,
                });
            }
        } catch (e) {
            handleFailure(e);
        } finally {
            setBusy(false);
        }
    }

    function backToName() {
        setStep("name");
        setCode("");
        setError(null);
        setReserved(null);
    }

    function retryAfterLock() {
        setError(null);
        setStep("login");
    }

    function onSubmit(e) {
        e.preventDefault();
        if (busy) return;
        if (step === "name") submitName();
        else if (step === "login" || step === "confirm") submitCode();
    }

    const title = {
        name: "Anmelden",
        login: "Code eingeben",
        confirm: "Code bestätigen",
        locked: "Gesperrt",
    }[step];

    return (
        <div
            className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 p-4 font-[Silkscreen,monospace] text-white"
            onKeyDown={(e) => e.key === "Escape" && onClose(null)}
        >
            <form onSubmit={onSubmit} className="w-full max-w-sm space-y-4 border-4 border-[#272a28] bg-[#111] p-5">
                <h2 className="text-sm uppercase tracking-[0.2em]">{title}</h2>

                {step === "name" && (
                    <label className="block space-y-1 text-xs">
                        Spielername
                        <input
                            ref={inputRef}
                            className={inputClass}
                            value={name}
                            onChange={(e) => setName(e.target.value)}
                            disabled={busy}
                            autoComplete="username"
                        />
                    </label>
                )}

                {step === "login" && (
                    <>
                        <p className="text-xs">
                            Der Name „{name}“ ist bereits vergeben. Gib den Code ein, den du bei der Registrierung
                            erhalten hast.
                        </p>
                        <label className="block space-y-1 text-xs">
                            Code
                            <input
                                ref={inputRef}
                                className={inputClass}
                                value={code}
                                onChange={(e) => setCode(e.target.value)}
                                disabled={busy}
                                autoComplete="off"
                            />
                        </label>
                    </>
                )}

                {step === "confirm" && reserved && (
                    <>
                        <p className="text-xs">
                            Der Name „{reserved.displayName}“ ist frei und gehört dir als {reserved.publicId}. Das ist
                            dein Code. Merke ihn dir, du brauchst ihn zum späteren Anmelden.
                        </p>
                        <p className="break-all border-2 border-dashed border-[#ffb900] p-2 text-center text-sm text-[#ffb900]">
                            {reserved.code}
                        </p>
                        <label className="block space-y-1 text-xs">
                            Code zur Bestätigung noch einmal eingeben
                            <input
                                ref={inputRef}
                                className={inputClass}
                                value={code}
                                onChange={(e) => setCode(e.target.value)}
                                disabled={busy}
                                autoComplete="off"
                            />
                        </label>
                    </>
                )}

                {step === "locked" && (
                    <p className="text-xs">
                        Zu viele Fehlversuche. Der Name „{name}“ ist gesperrt
                        {!Number.isNaN(lockEnd) && ` bis ${new Date(lockEnd).toLocaleString("de-DE")}`}.
                        {secondsLeft > 0 && <span className="mt-2 block text-[#ffb900]">Noch {secondsLeft} s</span>}
                    </p>
                )}

                {error && <p className="whitespace-pre-line text-xs text-red-400">{error}</p>}

                <div className="flex flex-wrap justify-end gap-2">
                    {(step === "login" || step === "confirm" || step === "locked") && (
                        <button type="button" className={buttonClass} onClick={backToName} disabled={busy}>
                            Anderer Name
                        </button>
                    )}
                    <button type="button" className={buttonClass} onClick={() => onClose(null)}>
                        Abbrechen
                    </button>
                    {step === "locked" ? (
                        <button type="button" className={buttonClass} disabled={secondsLeft > 0} onClick={retryAfterLock}>
                            Erneut versuchen
                        </button>
                    ) : (
                        <button type="submit" className={buttonClass} disabled={busy}>
                            {busy ? "Bitte warten …" : step === "name" ? "Weiter" : "Bestätigen"}
                        </button>
                    )}
                </div>
            </form>
        </div>
    );
}