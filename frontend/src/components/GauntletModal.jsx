import { useEffect, useState } from "react";
import { difficultyLabel } from "../api/gauntlet.js";

const btnPrimary =
    "mx-auto w-fit bg-white px-2 font-bold uppercase text-black hover:bg-yellow-300 disabled:cursor-not-allowed disabled:opacity-40";
const btnSecondary = "mx-auto uppercase text-white hover:text-yellow-300";

/**
 * phase: "offer" | "running" | "done"
 * result: { success, completed, total, message }
 */
export default function GauntletModal({ phase, capabilities, result, onStart, onCancel, onClose }) {
    const all = capabilities?.difficulties ?? [];
    const [selected, setSelected] = useState(all);

    const toggle = (d) => setSelected((s) => (s.includes(d) ? s.filter((x) => x !== d) : [...s, d]));

    // while running there is no visible UI; Escape aborts
    useEffect(() => {
        if (phase !== "running") return;
        const onKey = (e) => e.key === "Escape" && onCancel();
        window.addEventListener("keydown", onKey);
        return () => window.removeEventListener("keydown", onKey);
    }, [phase, onCancel]);

    // nothing to show while running: the maze (and its zoom / follow buttons) stays fully usable
    if (phase === "running") return null;

    // a failed gauntlet: no overlay, so the failed maze stays visible and can be inspected (zoom, pan, "Alles", "Folgen")
    if (phase === "done" && result && !result.success) {
        return (
            <div className="pointer-events-none fixed inset-x-0 bottom-0 z-50 flex justify-center p-4">
                <section
                    role="alertdialog"
                    aria-labelledby="gauntlet-title"
                    className="pointer-events-auto flex w-full max-w-sm flex-col gap-1 border-4 border-[#272a28] bg-[#111]/95 p-3 text-center text-sm"
                >
                    <h2 id="gauntlet-title" className="uppercase text-red-400">
                        Härtetest gescheitert
                    </h2>
                    <p>
                        {result.completed} / {result.total} Stufen
                    </p>
                    {result.message && <p className="text-xs text-white/70">{result.message}</p>}
                    <button className={`${btnPrimary} mt-1`} onClick={onClose}>
                        &gt; Weiter
                    </button>
                </section>
            </div>
        );
    }

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/80 p-4 backdrop-blur-[2px]">
            <section
                role="dialog"
                aria-modal="true"
                aria-labelledby="gauntlet-title"
                className="w-full max-w-sm border-4 border-[#272a28] bg-[#111] p-4 text-center"
            >
                <h2 id="gauntlet-title" className="mb-3 text-lg uppercase text-[#35B23A]">
                    Härtetest
                </h2>

                {phase === "offer" && (
                    <div className="flex flex-col gap-3 text-sm">
                        <fieldset className="flex flex-col gap-1 text-left">
                            <legend className="mb-1 uppercase">Schwierigkeitsgrade</legend>
                            {all.map((d) => (
                                <label key={d} className="flex items-center gap-2 uppercase">
                                    <input type="checkbox" checked={selected.includes(d)} onChange={() => toggle(d)} />
                                    {difficultyLabel(d)}
                                </label>
                            ))}
                        </fieldset>

                        <button
                            className={btnPrimary}
                            disabled={selected.length === 0}
                            onClick={() => onStart({ selected })}
                        >
                            &gt; Härtetest starten
                        </button>
                        <button className={btnSecondary} onClick={onCancel}>
                            &gt; Beenden
                        </button>
                    </div>
                )}

                {phase === "done" && result && (
                    <div className="flex flex-col gap-3 text-sm">
                        <p className={result.success ? "text-[#35B23A]" : "text-red-400"}>
                            {result.success ? "Geschafft!" : "Gescheitert"}
                        </p>
                        <p>
                            {result.completed} / {result.total} Stufen
                        </p>
                        {result.message && <p className="text-xs text-white/70">{result.message}</p>}
                        <button className={btnPrimary} onClick={onClose}>
                            &gt; Weiter
                        </button>
                    </div>
                )}
            </section>
        </div>
    );
}