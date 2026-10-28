import { useState } from "react";
import Leaderboard from "./Leaderboard.jsx";

// All values are in cqw = percent of the menu width.
const PAD_TOP = 8; // space between the top of the frame and the title
const SIDE = 16; // left and right margin, so content stays inside the frame
const BOTTOM = 14; // bottom margin

export default function GameMenue({
  api,
  active,
  busy,
  canStart,
  player,
  view,
  onViewChange,
  onLogin,
  onLogout,
  onStart,
  onCancel,
}) {
  const [tab, setTab] = useState("game"); // "game" | "leaderboard"

  const itemClass =
    "block w-full text-left hover:text-[#ffb900] focus-visible:outline-2 focus-visible:outline-white disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:text-inherit";
  const tabClass = (name) =>
    `flex-1 border-b-2 text-center font-black uppercase tracking-[0.15em] ${tab === name ? "border-[#ffb900] text-[#ffb900]" : "border-transparent hover:text-[#ffb900]"
    }`;
  const viewClass = (name) =>
    `${itemClass} ${view === name ? "text-[#ffb900]" : ""}`;

  const loggedIn = !!player;

  return (
    // container only: no cqw values on this element itself
    <aside
      className="relative mx-auto flex h-full w-full max-w-full flex-col bg-black"
      style={{ containerType: "inline-size" }}
    >
      <img
        src="/assets/Frame.png"
        alt=""
        draggable={false}
        className="pointer-events-none absolute inset-0 h-full w-full select-none object-fill"
      />

      <div
        className="relative flex min-h-[133.33cqw] flex-1 flex-col overflow-hidden lg:min-h-0"
        style={{
          fontSize: "2.8cqw",
          padding: `${PAD_TOP}cqw ${SIDE}cqw ${BOTTOM}cqw`,
        }}
      >
        <img
          src="/assets/Title.png"
          alt="Title"
          draggable={false}
          className="pointer-events-none mx-auto block h-auto w-full shrink-0 select-none"
        />

        <p className="mt-[2cqw] shrink-0 break-all text-center font-black normal-case text-[#ffb900]">
          {loggedIn ? `${player.displayName} ${player.publicId}` : "Nicht angemeldet"}
        </p>

        <div className="mt-[2cqw] flex shrink-0 items-stretch" style={{ fontSize: "0.85em" }}>
          <button type="button" className={tabClass("game")} onClick={() => setTab("game")}>
            Spiel
          </button>
          <button type="button" className={tabClass("leaderboard")} onClick={() => setTab("leaderboard")}>
            Rangliste
          </button>
        </div>

        <div className="mt-[3cqw] flex min-h-0 flex-1 flex-col overflow-y-auto">
          {tab === "game" ? (
            <div className="space-y-1 font-black uppercase tracking-[0.15em]">
              {/* view toggle */}
              <button type="button" onClick={() => onViewChange("maze")} className={viewClass("maze")}>
                &gt; Labyrinth
              </button>
              <button type="button" onClick={() => onViewChange("code")} className={viewClass("code")}>
                &gt; Code
              </button>

              <div className="pt-[2cqw]" />

              <button type="button" onClick={onLogin} disabled={loggedIn} className={itemClass}>
                &gt; Anmelden
              </button>
              <button type="button" onClick={onLogout} disabled={!loggedIn} className={itemClass}>
                &gt; Abmelden
              </button>

              <div className="pt-[2cqw]" />

              <button
                type="button"
                onClick={onStart}
                disabled={!loggedIn || !canStart || busy || active}
                className={itemClass}
              >
                &gt; {busy ? "Starte …" : "Starten"}
              </button>
              <button type="button" onClick={onCancel} disabled={!loggedIn || !active} className={itemClass}>
                &gt; Abbrechen
              </button>
            </div>
          ) : (
            <Leaderboard api={api} player={player} top={15} />
          )}
        </div>
      </div>
    </aside>
  );
}