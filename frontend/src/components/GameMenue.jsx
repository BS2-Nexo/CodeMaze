import { useEffect, useRef, useState } from "react";
import Leaderboard from "./Leaderboard.jsx";

// All values are in cqw = percent of the menu width.
const PAD_TOP = 8; // space between the top of the frame and the title
const SIDE = 16; // left and right margin, so content stays inside the frame
const BOTTOM = 14; // bottom margin

// The player figures offered in the menu. `tint: null` is the original (flesh coloured) sprite.
// The tint is a CSS colour whose hue and saturation are applied to the sprite (its brightness and
// outlines are kept), exactly as the maze does it.
export const PLAYER_TINTS = [
  { label: "Standard", tint: null },
  { label: "Rot", tint: "#ff1f1f" },
  { label: "Blau", tint: "#1f5cff" },
  { label: "Grün", tint: "#14e03c" },
  { label: "Gelb", tint: "#ffe500" },
];

const PLAYER_SPRITE = "/assets/Player_stand.png";

/** Player_stand.png with a tint applied (same technique as in Maze.jsx). */
function TintedSprite({ tint, className = "" }) {
  const canvasRef = useRef(null);

  useEffect(() => {
    let cancelled = false;
    const img = new Image();
    img.onload = () => {
      const canvas = canvasRef.current;
      if (cancelled || !canvas) return;
      canvas.width = img.naturalWidth;
      canvas.height = img.naturalHeight;
      const g = canvas.getContext("2d");
      g.clearRect(0, 0, canvas.width, canvas.height);
      g.drawImage(img, 0, 0);
      if (tint) {
        g.globalCompositeOperation = "color"; // take hue + saturation from the tint, brightness from the sprite...
        g.fillStyle = tint;
        g.fillRect(0, 0, canvas.width, canvas.height);
        g.globalCompositeOperation = "destination-in"; // ...then cut it back to the original shape
        g.drawImage(img, 0, 0);
        g.globalCompositeOperation = "source-over";
      }
    };
    img.src = PLAYER_SPRITE;
    return () => {
      cancelled = true;
    };
  }, [tint]);

  return <canvas ref={canvasRef} aria-hidden="true" className={`block [image-rendering:pixelated] ${className}`} />;
}

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
  playerTint = null, // currently selected tint (CSS colour) or null = default figure
  onPlayerTintChange, // called with the new tint (or null); the picker is hidden without it
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

              {/* player figure: the sprite in the available tints, always visible */}
              {onPlayerTintChange && (
                <div role="group" aria-label="Spielfigur" className="flex flex-wrap gap-[1.5cqw] pt-[2cqw]">
                  {PLAYER_TINTS.map(({ label, tint }) => {
                    const selected = tint === playerTint;
                    return (
                      <button
                        key={label}
                        type="button"
                        title={label}
                        aria-label={label}
                        aria-pressed={selected}
                        onClick={() => onPlayerTintChange(tint)}
                        className={`border-2 bg-black/70 p-[0.8cqw] hover:border-[#ffb900] focus-visible:outline-2 focus-visible:outline-white ${selected ? "border-[#ffb900]" : "border-white/40"
                          }`}
                      >
                        <TintedSprite tint={tint} className="h-[9cqw] w-[9cqw]" />
                      </button>
                    );
                  })}
                </div>
              )}

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