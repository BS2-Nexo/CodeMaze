import { useState } from "react";
import CodeBlockButton from "./buttons/CodeBlocks.jsx"

const KEYWORDS = ["gehe", "wiederhole", "mal", "immer", "wenn", "sonst", "ende",
            "funktion", "ergebnis", "nicht", "und", "oder"]
const DIRS =  ["hoch", "runter", "links", "rechts", "rückwärts", "zurück"]
const SENSORS = ["frei", "besucht"]


export default function GameInput({ status }) {
  const [program, setProgram] = useState([]);
  const [isDragOver, setIsDragOver] = useState(false);
  const [draggedProgramIndex, setDraggedProgramIndex] = useState(null);

  function addCodeBlock(code) {
    setProgram((current) => [...current, code]);
  }

  function handleDragStart(event, code) {
    event.dataTransfer.setData("application/x-code-block", code);
    event.dataTransfer.effectAllowed = "copy";
  }

  function handleProgramDragStart(event, index) {
    event.dataTransfer.setData("application/x-program-index", String(index));
    event.dataTransfer.setData("text/plain", `program:${index}`);
    event.dataTransfer.effectAllowed = "move";
    setDraggedProgramIndex(index);
  }

  function handleProgramDragEnd() {
    if (draggedProgramIndex !== null) {
      setProgram((current) => current.filter((_, index) => index !== draggedProgramIndex));
    }
    setDraggedProgramIndex(null);
    setIsDragOver(false);
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
    if ([...KEYWORDS, ...DIRS, ...SENSORS].includes(code)) {
      setProgram((current) => {
        const next = [...current];
        next.splice(Math.max(0, Math.min(insertAt, next.length)), 0, code);
        return next;
      });
    }
  }

  return (
    <section className="col-span-full 
    "aria-label="Code input">
      <div className=" mb-3" aria-label="Draggable code blocks">
        {KEYWORDS.map((code) => (
         <CodeBlockButton
         code={code}
         type="KEYWORDS"
        handleDragStart={handleDragStart}
        addCodeBlock={addCodeBlock}
         >
         {code}
         </CodeBlockButton>
        ))}

        {DIRS.map((code) => (
         <CodeBlockButton
         code={code}
         type="DIRS"
        handleDragStart={handleDragStart}
        addCodeBlock={addCodeBlock}
         >
         {code}
         </CodeBlockButton>
        ))}

         {SENSORS.map((code) => (
         <CodeBlockButton
         code={code}
         type="SENSORS"
        handleDragStart={handleDragStart}
        addCodeBlock={addCodeBlock}
         >
         {code}
         </CodeBlockButton>
        ))}
      </div>

      <div
        onDragOver={(event) => {
          event.preventDefault();
          event.dataTransfer.dropEffect = draggedProgramIndex === null ? "copy" : "move";
        }}
        onDragEnter={() => setIsDragOver(true)}
        onDragLeave={(event) => {
          if (!event.currentTarget.contains(event.relatedTarget)) {
            setIsDragOver(false);
          }
        }}
        onDrop={handleDrop}
        aria-label="Code input field. Drop code blocks here, or click a block to add it."
        className={`min-h-[76px] border-4 bg-[#050505] p-1 text-xs text-[#ffca20] max-[640px]:min-h-[58px] ${
          isDragOver ? "border-[#ffca20]" : "border-[#d8dad7]"
        }`}
      >
        {program.length > 0 ? (
          <div className="flex flex-wrap items-start " aria-live="polite">
            {program.map((code, index) => (
              <button
                key={`${code}-${index}`}
                type="button"
                draggable
                onDragStart={(event) => handleProgramDragStart(event, index)}
                onDragEnd={handleProgramDragEnd}
                onDragOver={(event) => {
                  event.preventDefault();
                  event.dataTransfer.dropEffect = "move";
                }}
                onDrop={(event) => handleDrop(event, index)}
                aria-label={`Move code block ${index + 1}`}
                className={`m-0.5 p-0.5 text-[10px] cursor-grab font-semibold border border-2 text-center  active:cursor-grabbing ${KEYWORDS.includes(code) ? "border-[#F9AB00] text-[#F9AB00]" : DIRS.includes(code) ? "border-[#35B23A] text-[#35B23A]" : "border-[#639BFF] text-[#639BFF]"}`}
              >
                {code}
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
