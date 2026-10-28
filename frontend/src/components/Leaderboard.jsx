import { useEffect, useState } from "react";

// full class strings, so Tailwind can see them
const PODIUM = {
    1: "border-[#ffd700] bg-[#ffd700]/20 text-[#ffd700]",
    2: "border-[#c0c0c0] bg-[#c0c0c0]/15 text-[#c0c0c0]",
    3: "border-[#cd7f32] bg-[#cd7f32]/15 text-[#cd7f32]",
};
const OWN = "bg-[#35B23A]/20";
const POLL_MS = 10_000;

const sameSession = (a, b) => !!a && !!b && String(a).toLowerCase() === String(b).toLowerCase();

function Row({ row, own }) {
    const podium = PODIUM[row.rank];
    return (
        <li
            className={`flex items-center gap-3 border-l-4 border-transparent px-2 py-1 text-sm ${podium ?? ""} ${own ? OWN : ""}`}
        >
            <span className="w-7 shrink-0 text-right">{row.rank}</span>
            <span className="min-w-0 flex-1 truncate normal-case">{row.displayName}</span>
            <span className="w-20 shrink-0 truncate text-left opacity-60">{row.playerTag}</span>
            <span className="w-16 shrink-0 text-right tabular-nums">{row.totalPoints}</span>
        </li>
    );
}

/**
 * Top list from GET /api/scores/leaderboard, refreshed every 10 seconds.
 * The logged-in player is always shown as the last entry, with his real rank, even if he is not in the top list.
 *
 * Props: api, player ({ sessionId, displayName, publicId } or null), top (number of rows, default 15)
 */
export default function Leaderboard({ api, player = null, top = 15 }) {
    const [rows, setRows] = useState(null);
    const [own, setOwn] = useState(null); // the player's row from the server, if he has scored
    const [error, setError] = useState(null);

    const sessionId = player?.sessionId ?? null;

    useEffect(() => {
        let cancelled = false;

        const load = async () => {
            try {
                const data = await api.leaderboard(top);
                let mine = sessionId ? data.find((r) => sameSession(r.session, sessionId)) ?? null : null;
                if (sessionId && !mine) {
                    // not in the top list: the full list (top = 0) is the only place that shows his rank
                    const all = await api.leaderboard(0);
                    mine = all.find((r) => sameSession(r.session, sessionId)) ?? null;
                }
                if (cancelled) return;
                setRows(data);
                setOwn(mine);
                setError(null);
            } catch (e) {
                if (!cancelled) setError(e.message);
            }
        };

        load();
        const id = setInterval(load, POLL_MS);
        return () => {
            cancelled = true;
            clearInterval(id);
        };
    }, [api, top, sessionId]);

    // a player without any score has no row on the server yet
    const ownRow =
        own ??
        (player
            ? { rank: "–", displayName: player.displayName, playerTag: player.publicId, totalPoints: 0 }
            : null);

    return (
        <div className="flex min-h-0 flex-1 flex-col gap-2">
            <ol className="min-h-0 flex-1 space-y-1 overflow-y-auto pr-1">
                {rows?.map((row) => (
                    <Row key={row.session} row={row} own={sameSession(row.session, sessionId)} />
                ))}
                {rows?.length === 0 && <li className="text-center text-sm">Noch keine Einträge.</li>}
                {!rows && !error && <li className="text-center text-sm">Lade …</li>}
            </ol>

            {ownRow && rows && (
                <ol className="shrink-0 border-t-2 border-white/20 pt-1 pr-1">
                    <Row row={ownRow} own />
                </ol>
            )}

            {error && <p className="whitespace-pre-line text-xs text-red-400">{error}</p>}
        </div>
    );
}