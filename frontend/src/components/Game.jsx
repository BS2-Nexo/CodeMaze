import { useState } from "react";
import { startPosition } from "../game/mazeData";
import { Maze } from "./Maze.jsx";
import GameOverModal from "./GameOverModal.jsx";
import GameMenue from "./GameMenue.jsx";
import GameInput from "./GameInput.jsx";
import LevelCompleteModal from "./LevelCompleteModal.jsx"

export default function Game() {
  const [player, setPlayer] = useState(startPosition);
  const [playerWalking, setPlayerWalking] = useState(false);
  const [moves, setMoves] = useState(0);
  const [status, setStatus] = useState("playing");
  const [inputResetKey, setInputResetKey] = useState(0);

  function restart() {
    setPlayer(startPosition);
    setPlayerWalking(false);
    setMoves(0);
    setStatus("playing");
    setInputResetKey((key) => key + 1);
  }

  function closeModal() {
    setStatus("playing");
  }

  return (
    <main className="bg-[url('/assets/Background.png')] bg-center bg-contain font-[Silkscreen,monospace] text-white p-6">
      <div className="mx-auto max-w-[100%] md:max-w-[50%] border-4 border-[#272a28] bg-[#111] p-3">
        <div className="grid grid-cols-[minmax(0,1fr)_minmax(104px,auto)] grid-rows-[minmax(300px,min(68vh,650px))_auto] gap-2 max-[640px]:grid-rows-[minmax(230px,58vh)_auto] max-[640px]:gap-[5px]">
          <Maze
            player={player}
            playerWalking={playerWalking}
            setPlayerWalking={setPlayerWalking}
            status={status}
            setPlayer={setPlayer}
            setMoves={setMoves}
            setStatus={setStatus}
          />

        <GameMenue 
        restart={restart}
        moves={moves}
        status={status}
        />
        <GameInput key={inputResetKey} status={status} />
        </div>
      </div>
      {status === "game over" && (
       <GameOverModal 
       moves={moves}
       restart={restart}
       onClose={closeModal}/>
      )}
       {status === "level complete" && (
       <LevelCompleteModal 
       moves={moves}
       restart={restart}
       onClose={closeModal}/>
      )}
    </main>
  );
}
