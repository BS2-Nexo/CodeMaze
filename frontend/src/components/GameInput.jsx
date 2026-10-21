import { useState } from "react";

const codeBlocks = ["code-1", "code-2", "code-3"];

export default function GameInput({ status }) {
  const [program, setProgram] = useState([]);
  const [isDragOver, setIsDragOver] = useState(false);

  function addCodeBlock(code) {
    setProgram((current) => [...current, code]);
  }

  function handleDragStart(event, code) {
    event.dataTransfer.setData("application/x-code-block", code);
    event.dataTransfer.effectAllowed = "copy";
  }

  function handleProgramDragStart(event, index) {
    event.dataTransfer.setData("application/x-program-index", String(index));
    event.dataTransfer.effectAllowed = "move";
  }

  function handleDrop(event, insertAt = program.length) {
    event.preventDefault();
    event.stopPropagation();
    setIsDragOver(false);

    const programIndex = event.dataTransfer.getData("application/x-program-index");
    if (programIndex !== "") {
      const from = Number(programIndex);
      setProgram((current) => {
        if (!Number.isInteger(from) || from < 0 || from >= current.length) return current;
        const next = [...current];
        const [code] = next.splice(from, 1);
        const target = Math.max(0, Math.min(insertAt - (from < insertAt ? 1 : 0), next.length));
        next.splice(target, 0, code);
        return next;
      });
      return;
    }

    const code = event.dataTransfer.getData("application/x-code-block");
    if (codeBlocks.includes(code)) {
      setProgram((current) => {
        const next = [...current];
        next.splice(Math.max(0, Math.min(insertAt, next.length)), 0, code);
        return next;
      });
    }
  }

  return (
    <section className="col-span-full min-w-0" aria-label="Code input">
      <div className="flex h-[22px] items-stretch gap-1 mb-3" aria-label="Draggable code blocks">
        {codeBlocks.map((code, index) => (
          <button
            key={code}
            type="button"
            draggable
            onDragStart={(event) => handleDragStart(event, code)}
            onClick={() => addCodeBlock(code)}
            aria-label={`Add code block ${index + 1}`}
            className="min-w-[58px] cursor-grab border border-[#111] bg-[#ffca20]  text-center text-[9px] font-black tracking-[0.08em] text-[#111] active:cursor-grabbing"
          >
            CODE
          </button>
        ))}
      </div>

      <div
        onDragOver={(event) => {
          event.preventDefault();
          event.dataTransfer.dropEffect = "copy";
        }}
        onDragEnter={() => setIsDragOver(true)}
        onDragLeave={(event) => {
          if (!event.currentTarget.contains(event.relatedTarget)) {
            setIsDragOver(false);
          }
        }}
        onDrop={handleDrop}
        aria-label="Code input field. Drop code blocks here, or click a block to add it."
        className={`min-h-[76px] border-4 bg-[#050505] p-2 text-xs text-[#ffca20] max-[640px]:min-h-[58px] ${
          isDragOver ? "border-[#ffca20]" : "border-[#d8dad7]"
        }`}
      >
        {program.length > 0 ? (
          <div className="flex flex-wrap items-start gap-1 " aria-live="polite">
            {program.map((code, index) => (
              <button
                key={`${code}-${index}`}
                type="button"
                draggable
                onDragStart={(event) => handleProgramDragStart(event, index)}
                onDragOver={(event) => {
                  event.preventDefault();
                  event.dataTransfer.dropEffect = "move";
                }}
                onDrop={(event) => handleDrop(event, index)}
                aria-label={`Move code block ${index + 1}`}
                className="cursor-grab border border-[#ffca20] px-2 py-1 active:cursor-grabbing"
              >
                {index + 1}. CODE
              </button>
            ))}
          </div>
        ) : status === "game over" ? (
          "LEVEL COMPLETE"
        ) : (
          <span className="text-white/50">Drag code blocks here</span>
        )}
      </div>
    </section>
  );
}
