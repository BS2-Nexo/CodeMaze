import { useEffect, useState } from "react";

// full class strings, so Tailwind can see them
const PODIUM = {
    1: "border-l-4 border-[#ffd700] bg-[#ffd700]/20 text-[#ffd700] text-xs font-black",
    2: "border-l-4 border-[#c0c0c0] bg-[#c0c0c0]/15 text-[#c0c0c0] text-xs font-black",
    3: "border-l-4 border-[#cd7f32] bg-[#cd7f32]/15 text-[#cd7f32] text-xs font-black",
};
const OWN = "bg-[#35B23A]/20";

/**
 * Top list from GET /api/scores/leaderboard.
 * Props: api, sessionId (highlights the player's own row), top (number of rows, default 15)
 */
export default function Leaderboard({ api, sessionId = null, top = 15 }) {
    const [rows, setRows] = useState(null);
    const [error, setError] = useState(null);
    const [loading, setLoading] = useState(false);
    const [reloadKey, setReloadKey] = useState(0);

    useEffect(() => {
        let cancelled = false;
        setLoading(true);
        setError(null);
        api
            .leaderboard(top)
            .then((data) => !cancelled && setRows(data))
            .catch((e) => !cancelled && setError(e.message))
            .finally(() => !cancelled && setLoading(false));
        return () => {
            cancelled = true;
        };
    }, [api, top, reloadKey]);

    return (
        <div className="flex min-h-0 flex-1 flex-col gap-2">
            <ol className="min-h-0 flex-1 space-y-0.5 overflow-y-auto pr-1">
                {rows?.map((row) => {
                    const podium = PODIUM[row.rank];
                    const own = sessionId && row.session === sessionId;
                    return (
                        <li
                            key={row.session}
                            className={`flex items-center gap-2 px-1 py-0.5 text-[10px] ${podium ?? ""} ${own ? OWN : ""}`}
                        >
                            <span className="w-5 shrink-0 text-right">{row.rank <= 3 ? "★" : row.rank}</span>
                            <span className="min-w-0 flex-1 truncate normal-case">
                                {row.displayName} <span className="opacity-60">{row.playerTag}</span>
                            </span>
                            <span className="shrink-0">{row.totalPoints}</span>
                        </li>
                    );
                })}
                {rows?.length === 0 && <li className="text-center text-[10px]">Noch keine Einträge.</li>}
                {!rows && loading && <li className="text-center text-[10px]">Lade …</li>}
            </ol>

            {error && <p className="whitespace-pre-line text-[10px] text-red-400">{error}</p>}

            <button
                type="button"
                onClick={() => setReloadKey((k) => k + 1)}
                disabled={loading}
                className="block w-full text-left text-xs font-black uppercase tracking-[0.15em] hover:text-[#ffb900] disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:text-inherit"
            >
                &gt; {loading ? "Lade …" : "Aktualisieren"}
            </button>
        </div>
    );
}