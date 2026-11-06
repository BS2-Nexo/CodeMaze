import { useEffect } from "react";

const blockSteps = [
  {
    title: "Plane deinen Weg",
    text: "Schau dir das Labyrinth an. Wo startet deine Figur und wie kommt sie zum Ziel?",
  },
  {
    title: "Baue dein Programm",
    text: "Ziehe die passenden Befehlsblöcke in das Eingabefeld. Sie werden in deiner gewählten Reihenfolge ausgeführt.",
  },
  {
    title: "Teste deine Lösung",
    text: "Drücke auf Start und beobachte deine Figur. Erreicht sie das Ziel nicht, passe deine Befehle an und versuche es erneut.",
  },
];

const codeSteps = [
  {
    title: "Plane deinen Weg",
    text: "Schau dir Start, Ziel und Wände an. Überlege, welche Befehle deine Figur zum Ziel führen.",
  },
  {
    title: "Schreibe deinen Code",
    text: "Schreibe einen Befehl pro Zeile, zum Beispiel „gehe hoch“ oder „gehe rechts“. Dein Programm wird von oben nach unten ausgeführt.",
  },
  {
    title: "Teste dein Programm",
    text: "Drücke auf Start und beobachte das Ergebnis. Lies die Rückmeldung und verbessere deinen Code, wenn etwas nicht klappt.",
  },
];

export default function HelpModal({ onClose }) {
  useEffect(() => {
    const onKeyDown = (event) => {
      if (event.key === "Escape") onClose();
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [onClose]);

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/80 p-4 backdrop-blur-[2px]"
      onMouseDown={(event) => event.target === event.currentTarget && onClose()}
    >
      <section
        role="dialog"
        aria-modal="true"
        aria-labelledby="help-title"
        className="relative max-h-[90vh] w-full max-w-3xl overflow-y-auto border-4 border-[#863737] bg-[#0c0c0c] font-pixel text-stone-100 shadow-[inset_0_0_0_4px_#080808,inset_0_0_0_6px_#d99a16]"
      >
        <button
          type="button"
          onClick={onClose}
          aria-label="Hilfe schließen"
          className="absolute right-3 top-3 z-10 border-2 border-amber-400 bg-black px-3 py-1 text-xl font-bold text-amber-400 hover:bg-amber-400 hover:text-black focus-visible:outline-2 focus-visible:outline-white"
        >
          ×
        </button>

        <div className="relative px-6 pb-12 pt-8 sm:px-10">
          <img
            src="/assets/Title.png"
            alt="CodeMaze"
            className="mx-auto mb-8 h-auto w-full max-w-75 [image-rendering:pixelated]"
          />

          <h2 id="help-title" className="mb-3 text-center text-xl font-bold uppercase text-amber-400 sm:text-2xl">
            So spielst du
          </h2>
          <p className="mb-10 text-center text-xs leading-6 text-stone-300">
            Dein Programm zeigt der Figur den Weg zum Ziel.
          </p>

          <HelpSteps title="Spielen mit Blöcken" steps={blockSteps} />
          <HelpSteps title="Spielen mit eigenem Code" steps={codeSteps} />
        </div>
      </section>
    </div>
  );
}

function HelpSteps({ title, steps }) {
  return (
    <section className="mb-10 last:mb-0">
      <h3 className="mb-8 text-center text-xl leading-6 text-amber-400">{title}</h3>
      <ol className="space-y-8">
        {steps.map((step, index) => (
          <li key={step.title}>
            <span className="mb-3 inline-flex h-9 w-9 items-center justify-center border-2 border-amber-400 bg-black text-sm text-amber-400">
              <span className="sr-only">Schritt </span>
              {index + 1}
            </span>
            <h4 className="mb-2 text-sm font-bold text-amber-400">{step.title}</h4>
            <p className="text-sm leading-6 text-stone-200">{step.text}</p>
          </li>
        ))}
      </ol>
    </section>
  );
}
