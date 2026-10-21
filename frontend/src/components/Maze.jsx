import { useEffect, useCallback } from "react";
import { maze } from "../game/mazeData";

export function Maze({
  player,
  playerWalking,
  setPlayerWalking,
  status,
  setPlayer,
  setMoves,
  setStatus,
}) {
  const move = useCallback(
    (dx, dy) => {
      if (status !== "playing") return;

      const x = player.x + dx;
      const y = player.y + dy;
      const tile = maze[y]?.[x];

      // Prevent movement through walls or outside the maze.
      if (tile === undefined || tile === 1) return;

      setPlayer({ x, y });
      setMoves((count) => count + 1);
      setPlayerWalking((walking) => !walking);

      if (tile === 2) {
        setStatus("game over");
      }
    },
    [player, status, setPlayer, setMoves, setPlayerWalking, setStatus]
  );

  useEffect(() => {
    function handleKeyDown(event) {
      const directions = {
        ArrowUp: [0, -1],
        ArrowDown: [0, 1],
        ArrowLeft: [-1, 0],
        ArrowRight: [1, 0],
        w: [0, -1],
        s: [0, 1],
        a: [-1, 0],
        d: [1, 0],
        W: [0, -1],
        S: [0, 1],
        A: [-1, 0],
        D: [1, 0],
      };

      const direction = directions[event.key];
      if (!direction) return;

      // Don't intercept keyboard input in text fields.
      if (
        event.target instanceof HTMLElement &&
        (event.target.isContentEditable ||
          ["INPUT", "TEXTAREA", "SELECT"].includes(
            event.target.tagName
          ))
      ) {
        return;
      }

      event.preventDefault();
      move(...direction);
    }

    window.addEventListener("keydown", handleKeyDown);

    return () => {
      window.removeEventListener("keydown", handleKeyDown);
    };
  }, [move]);

  return (
    <section className="min-w-0 min-h-0 overflow-hidden bg-black [container-type:size]">
      <div className="flex h-full w-full items-center justify-center overflow-hidden">
        <div
          className="grid size-[min(100%,100cqh)] aspect-square max-h-full max-w-full gap-[2px] outline-none focus-visible:ring-2 focus-visible:ring-[#ffb900]"
          tabIndex={0}
          style={{
            gridTemplateColumns: `repeat(${maze[0].length}, minmax(0, 1fr))`,
          }}
          role="grid"
          aria-label="Labyrinth game board"
        >
          {maze.map((row, y) =>
            row.map((tile, x) => {
              const isPlayer =
                player.x === x && player.y === y;

              const tileStyle =
                tile === 1
                  ? "bg-[url('/assets/Tile.png')] bg-contain bg-center"
                  : tile === 2
                    ? "bg-[url('/assets/FinishFlag.png')] bg-cover bg-center"
                    : "bg-black";

              return (
                <div
                  key={`${x}-${y}`}
                  role="gridcell"
                  aria-label={
                    isPlayer
                      ? "Player position"
                      : tile === 1
                        ? "Wall"
                        : tile === 2
                          ? "Exit"
                          : "Floor"
                  }
                  className={`relative grid aspect-square min-h-0 min-w-0 place-items-center overflow-hidden ${tileStyle}`}
                >
                  {isPlayer && (
                    <img
                      src={playerWalking ? "/assets/Player_walk.png" : "/assets/Player_stand.png"}
                      alt="Player"
                      draggable="false"
                      className="h-full w-full select-none object-contain"
                    />
                  )}

                  {!isPlayer && tile === 2 && (
                    <span className="select-none text-[clamp(6px,1.5vh,20px)] leading-none">
                    </span>
                  )}
                </div>
              );
            })
          )}
        </div>
      </div>
    </section>
  );
}
