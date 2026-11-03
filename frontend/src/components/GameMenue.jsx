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

// Border colours, taken from the frame image.
const RED = "border-[#d04545]";
const AMBER_HOVER = "enabled:hover:border-[#ffb900] enabled:hover:bg-[#ffb900]/15";

/**
 * The dashed inner line shared by all framed buttons. It takes the colour of the button's
 * outer border (border-inherit), so hover and selected states colour both lines at once.
 */
function InnerLine({ inset, width = "border-2", className = "" }) {
  return (
    <span
      aria-hidden="true"
      className={`pointer-events-none absolute border-dashed border-inherit ${width} ${className}`}
      style={{ inset }}
    />
  );
}

/**
 * A big, centered menu button with a toned-down version of the frame's border:
 * a solid brick-red outer line and a dashed inner line. `active` fills it in amber
 * (used for the Labyrinth / Code toggle).
 */
function MenuButton({ children, onClick, disabled = false, active = false, pressed }) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-pressed={pressed}
      className={`relative flex min-h-[9cqw] w-full items-center justify-center border-[3px] px-[2.5cqw] py-[2cqw] text-center font-black uppercase tracking-[0.12em] focus-visible:outline-2 focus-visible:outline-white disabled:cursor-not-allowed disabled:opacity-40 ${active
        ? "border-[#d48a00] bg-[#ffb900] text-black"
        : `${RED} bg-black/70 text-white ${AMBER_HOVER} enabled:hover:text-[#ffb900] enabled:active:bg-[#ffb900]/30`
        }`}
    >
      <InnerLine inset="0.8cqw" className={active ? "!border-black/60" : ""} />
      <span className="relative">{children}</span>
    </button>
  );
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

  const tabClass = (name) =>
    `flex-1 border-b-2 py-[1.5cqw] text-center font-black uppercase tracking-[0.15em] ${tab === name ? "border-[#ffb900] text-[#ffb900]" : "border-transparent hover:text-[#ffb900]"
    }`;

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
            <div className="flex flex-col items-center gap-[4cqw]">
              {/* 1. login / logout */}
              <div className="grid w-full grid-cols-2 gap-[2cqw]">
                <MenuButton onClick={onLogin} disabled={loggedIn}>
                  Anmelden
                </MenuButton>
                <MenuButton onClick={onLogout} disabled={!loggedIn}>
                  Abmelden
                </MenuButton>
              </div>

              {/* 2. player figure: the sprite in the available tints (original size) */}
              {onPlayerTintChange && (
                <div role="group" aria-label="Spielfigur" className="flex flex-wrap justify-center gap-[1.5cqw]">
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
                        className={`relative border-[3px] p-[0.8cqw] focus-visible:outline-2 focus-visible:outline-white ${selected
                          ? "border-[#ffb900] bg-[#ffb900]/25"
                          : `${RED} bg-black/70 hover:border-[#ffb900] hover:bg-[#ffb900]/15`
                          }`}
                      >
                        <InnerLine inset="0.15cqw" width="border" />
                        <TintedSprite tint={tint} className="relative h-[9cqw] w-[9cqw]" />
                      </button>
                    );
                  })}
                </div>
              )}

              {/* 3. view toggle */}
              <div className="grid w-full grid-cols-2 gap-[2cqw]">
                <MenuButton onClick={() => onViewChange("maze")} active={view === "maze"} pressed={view === "maze"}>
                  Labyrinth
                </MenuButton>
                <MenuButton onClick={() => onViewChange("code")} active={view === "code"} pressed={view === "code"}>
                  Code
                </MenuButton>
              </div>

              {/* 4. start / cancel */}
              <div className="grid w-full grid-cols-2 gap-[2cqw]">
                <MenuButton onClick={onStart} disabled={!loggedIn || !canStart || busy || active}>
                  {busy ? "Starte …" : "Starten"}
                </MenuButton>
                <MenuButton onClick={onCancel} disabled={!loggedIn || !active}>
                  Abbrechen
                </MenuButton>
              </div>
            </div>
          ) : (
            <Leaderboard api={api} player={player} top={15} />
          )}
        </div>
      </div>
    </aside>
  );
}