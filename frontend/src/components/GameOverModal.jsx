export default function GameOverModal({ restart, onClose }) {
  
    return (
 <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/80 p-4 backdrop-blur-[2px]">
          <section
            aria-modal="true"
            aria-labelledby="game-over-title"
            role="dialog"
            className="w-full max-w-sm border-4  border-[#272a28]  bg-[url('/assets/GameOver.png')] bg-contain text-center "
          >

            <div className="flex flex-col justify-end  pb-4 gap-1 aspect-917/520">
              <button
                onClick={restart}
                className="mx-auto  bg-white text-md w-fit h-fit px-2 font-bold uppercase text-black hover:bg-yellow-300 "
              >
                &gt; Try again
              </button>
              <button
                onClick={onClose}
                className="mx-auto text-md uppercase text-white hover:text-yellow-300"
              >
                &gt; Exit
              </button>
            </div>
          </section>
        </div>
   )};
