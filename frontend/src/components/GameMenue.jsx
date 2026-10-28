import { useState } from "react";
import Leaderboard from "./Leaderboard.jsx";

// All values are in cqw = percent of the menu width. Tune these three while looking at the page.
const NAME_TOP = 9; // distance from the top of the image to the name text (inside the banner)
const TABS_TOP = 24; // distance from the top to the tab row (below the banner)
const CONTENT_TOP = 31; // distance from the top to the stats / buttons / leaderboard
const SIDE = 16; // left and right margin of the content, so it stays inside the frame
const BOTTOM = 14; // bottom margin of the content

export default function GameMenue({
  api,
  score,
  active,
  busy,
  canStart,
  player,
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

  const loggedIn = !!player;

  return (
    <aside
      className="relative mx-auto w-full max-w-full justify-self-center bg-black"
      style={{ containerType: "inline-size" }}
    >
      {/* the frame is a normal image now: it defines the size and keeps its proportions */}
      <img
        src="/assets/Frame.png"
        alt=""
        draggable={false}
        className="pointer-events-none block h-auto w-full select-none"
      />

      {/* everything else is laid over the image */}
      <div className="absolute inset-0 flex flex-col" style={{ fontSize: "2.8cqw" }}>
        {/* name: inside the banner */}
        <div
          className="text-center"
          style={{ position: "absolute", top: `${NAME_TOP}cqw`, left: `${SIDE}cqw`, right: `${SIDE}cqw` }}
        >
          <p className="break-all font-black normal-case text-[#ffb900]">
            {loggedIn ? `${player.displayName} ${player.publicId}` : "Nicht angemeldet"}
          </p>
        </div>

        {/* tabs */}
        <div
          className="flex items-stretch"
          style={{ position: "absolute", top: `${TABS_TOP}cqw`, left: `${SIDE}cqw`, right: `${SIDE}cqw`, fontSize: "0.85em" }}
        >
          <button type="button" className={tabClass("game")} onClick={() => setTab("game")}>
            Spiel
          </button>
          <button type="button" className={tabClass("leaderboard")} onClick={() => setTab("leaderboard")}>
            Rangliste
          </button>
        </div>

        {/* content */}
        <div
          className="flex min-h-0 flex-col"
          style={{
            position: "absolute",
            top: `${CONTENT_TOP}cqw`,
            left: `${SIDE}cqw`,
            right: `${SIDE}cqw`,
            bottom: `${BOTTOM}cqw`,
          }}
        >
          {tab === "game" ? (
            <>
              <div className="space-y-[1.5cqw] text-center">
                <div>
                  <h2 className="uppercase tracking-[0.2em]">Level:</h2>
                  <p className="mx-auto w-[40%] bg-[#d6d6d6] py-0.5 text-[0.9em] font-black tracking-[0.2em] text-black">
                    001
                  </p>
                </div>
                <div>
                  <h2 className="uppercase tracking-[0.2em]">Score:</h2>
                  <p className="mx-auto w-[40%] bg-[#d6d6d6] py-0.5 text-[0.9em] font-black tracking-[0.2em] text-black">
                    {score}
                  </p>
                </div>
              </div>

              <div className="mt-[5cqw] space-y-1 font-black uppercase tracking-[0.15em]">
                <button type="button" onClick={onLogin} disabled={loggedIn} className={itemClass}>
                  &gt; Anmelden
                </button>
                <button type="button" onClick={onLogout} disabled={!loggedIn} className={itemClass}>
                  &gt; Abmelden
                </button>

                <div className="pt-[3cqw]" />

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
            </>
          ) : (
            <Leaderboard api={api} sessionId={player?.sessionId} top={15} />
          )}
        </div>
      </div>
    </aside>
  );
}