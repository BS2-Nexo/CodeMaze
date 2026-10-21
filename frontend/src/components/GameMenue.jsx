export default function GameMenue({ restart, moves, status }) {
 
  return (
    <aside className="flex h-full w-auto max-w-full aspect-[593/980] flex-col justify-self-center bg-black bg-[url('/assets/Frame.png')] bg-center bg-no-repeat bg-[length:100%_100%] p-8">
      <div className="mx-auto mt-32 w-full max-w-40 space-y-2 text-center">
        <div>
          <h2 className="text-md uppercase tracking-[0.2em]">Level:</h2>
          <p className="mx-auto w-[80px] bg-[#d6d6d6] px-2 py-0.5 text-xs font-black tracking-[0.2em] text-black">
            001
          </p>
        </div>
        <div>
          <h2 className="text-md uppercase tracking-[0.2em]">Score:</h2>
          <p className="mx-auto w-[80px] bg-[#d6d6d6] py-0.5 text-xs font-black tracking-[0.2em] text-black">
            {moves * 10}
          </p>
        </div>
      </div>

      <div className="mx-auto mt-10 pt-4">
        <div className="space-y-1 text-xs font-black uppercase tracking-[0.15em] sm:text-sm">
          <button
            onClick={restart}
            className="block w-full text-left hover:text-[#ffb900] focus-visible:outline-2 focus-visible:outline-white"
          >
            &gt; Restart
          </button>
          {status === "game over" ? (
            <button
              onClick={restart}
              className="block w-full text-left hover:text-[#ffb900] focus-visible:outline-2 focus-visible:outline-white"
            >
              &gt; Play again
            </button>
          ) : (
            <button
              onClick={() => document.querySelector(".maze-board")?.focus()}
              className="block w-full text-left hover:text-[#ffb900] focus-visible:outline-2 focus-visible:outline-white"
            >
              &gt; Start
            </button>
          )}
        </div>
      </div>
    </aside>
  );
}
