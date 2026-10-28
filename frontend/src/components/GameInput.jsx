import {
  Fragment,
  forwardRef,
  useCallback,
  useEffect,
  useImperativeHandle,
  useLayoutEffect,
  useRef,
  useState,
} from "react";
import {
  BLOCKS,
  DIRECTIONS,
  BlockProgram,
  getPalette,
  normalizeName,
} from "../blocks/blockProgram.js"; // adjust to where this file lives

// ---------- look ----------

const COLORS = {
  keyword: "border-[#F9AB00] text-[#F9AB00]",
  value: "border-[#35B23A] text-[#35B23A]",
  sensor: "border-[#639BFF] text-[#639BFF]",
  note: "border-white/40 text-white/60",
};
const COLOR_OF = { Werte: "value", Sensoren: "sensor", Logik: "sensor", Sonstiges: "note" };
const colorOf = (type) => COLORS[COLOR_OF[BLOCKS[type].category] ?? "keyword"];

const ORDER = ["Bewegung", "Schleifen", "Bedingung", "Werte", "Sensoren", "Logik", "Funktionen", "Sonstiges"];

function labelOf(node) {
  switch (node.type) {
    case "dir": return node.props.value;
    case "param":
    case "call": return node.props.name || "?";
    case "number": return "Zahl";
    case "repeat": return "wiederhole";
    case "funcDef": return "funktion";
    case "comment": return "#";
    default: return BLOCKS[node.type].label;
  }
}

const inputClass = "border border-white/40 bg-black px-0.5 text-white";

// ---------- palette ----------

/** One palette entry per block; directions, parameters and function calls are expanded. */
function paletteItems(program) {
  const fns = program.functions();
  const params = new Map();
  fns.forEach((f) => f.params.forEach((p) => params.has(normalizeName(p)) || params.set(normalizeName(p), p)));

  const items = [];
  const sorted = getPalette().sort((a, b) => ORDER.indexOf(a.category) - ORDER.indexOf(b.category));
  for (const b of sorted) {
    if (b.type === "dir") {
      DIRECTIONS.forEach((d) => items.push({ ...b, key: `dir-${d}`, label: d, props: { value: d } }));
    } else if (b.type === "param") {
      [...params.values()].forEach((p) => items.push({ ...b, key: `param-${p}`, label: p, props: { name: p } }));
    } else if (b.type === "call") {
      fns.forEach((f) => items.push({ ...b, key: `call-${f.name}`, label: f.name, props: { name: f.name } }));
    } else {
      items.push({ ...b, key: b.type, props: {} });
    }
  }
  return items;
}

// ---------- insertion point ("focus") ----------
// A focus is a target for BlockProgram.add():
//   {}                               end of the program
//   { index }                        in the program, before block #index
//   { parent, list: "body", index }  inside a block's body
//   { parent, list: "args", index }  among a call's arguments
//   { parent, slot }                 into an empty slot
// `index` undefined means "at the end".

/** Drops a focus that no longer exists (block removed, slot filled, list shorter). */
function effectiveFocus(program, f) {
  if (!f.parent) return f.index >= program.root.length ? {} : f;
  const owner = program.get(f.parent);
  if (!owner) return {};
  if (f.slot) return owner.slots[f.slot] ? {} : f;
  const len = owner.lists[f.list].length;
  return f.index >= len ? { parent: f.parent, list: f.list } : f;
}

/** First thing still to fill in inside `node`: an empty slot, or missing call arguments. */
function firstOpen(node, fns) {
  if (node.type === "call") {
    const fn = fns.find((f) => normalizeName(f.name) === normalizeName(node.props.name));
    if (fn && node.lists.args.length < fn.arity) return { parent: node.id, list: "args" };
  }
  for (const name of Object.keys(BLOCKS[node.type].slots ?? {})) {
    const child = node.slots[name];
    if (!child) return { parent: node.id, slot: name };
    const hit = firstOpen(child, fns);
    if (hit) return hit;
  }
  return null;
}

/** Where the next block should go after `id` was added: fill the program in reading order. */
function nextFocus(program, id) {
  const path = program.path(id);
  if (!path) return {};

  // the statement this block belongs to
  let k = path.length - 1;
  while (k > 0 && !path[k - 1].lists.body?.includes(path[k])) k--;
  const stmt = path[k];

  const open = firstOpen(stmt, program.functions());
  if (open) return open;
  if (stmt.lists.body?.length === 0) return { parent: stmt.id, list: "body" };

  // otherwise continue right after the statement
  const parent = k > 0 ? path[k - 1] : null;
  const list = parent ? parent.lists.body : program.root;
  const at = list.indexOf(stmt) + 1;
  const index = at >= list.length ? undefined : at;
  return parent ? { parent: parent.id, list: "body", index } : { index };
}

const targetKey = (t) => `${t.parent ?? ""}|${t.slot ?? ""}|${t.list ?? ""}|${t.index ?? ""}`;

// ---------- pieces ----------

/** A place where a block can go: highlighted when selected, lights up when a dragged block fits. */
function Gap({ target, ctx, inline = false, trailing = false, label = "" }) {
  const focused = ctx.isFocus(target);
  const droppable = ctx.drag !== null && ctx.canDrop(target);

  const color = focused
    ? "border-[#ffca20] bg-[#ffca20]/25 text-[#ffca20]"
    : droppable
      ? "border-[#ffca20]/70 bg-[#ffca20]/10 text-[#ffca20]"
      : "border-white/20 text-white/40";
  const shape = inline
    ? "inline-flex h-5 min-w-6 items-center justify-center border-2 border-dashed px-1"
    : focused || droppable || trailing
      ? "flex min-h-4 w-full items-center border-2 border-dashed px-1"
      : "block h-1.5 w-full";

  return (
    <button
      type="button"
      aria-label="Hier einfügen"
      onClick={() => ctx.select(target)}
      onDragOver={(event) => ctx.dragOver(event, target)}
      onDrop={(event) => ctx.drop(event, target)}
      className={`text-left text-[10px] ${shape} ${color}`}
    >
      {label}
    </button>
  );
}

function StatementList({ items, owner, ctx, emptyLabel }) {
  const base = owner ? { parent: owner.id, list: "body" } : {};
  return (
    <div className="flex flex-col">
      {items.map((node, i) => (
        <Fragment key={node.id}>
          <Gap target={{ ...base, index: i }} ctx={ctx} />
          <Block node={node} ctx={ctx} />
        </Fragment>
      ))}
      <Gap trailing target={base} ctx={ctx} label={items.length === 0 ? emptyLabel : "+"} />
    </div>
  );
}

function SlotView({ node, name, ctx }) {
  const child = node.slots[name];
  if (child) return <Block node={child} ctx={ctx} inline />;
  return (
    <Gap inline target={{ parent: node.id, slot: name }} ctx={ctx} label={BLOCKS[node.type].slots[name].label} />
  );
}

function ParamsInput({ node, program }) {
  const [text, setText] = useState(node.props.params.join(", "));
  return (
    <input
      className={`w-16 ${inputClass}`}
      value={text}
      placeholder="Parameter"
      aria-label="Parameter, mit Komma getrennt"
      onChange={(event) => {
        setText(event.target.value);
        program.setProps(node.id, { params: event.target.value.split(/[,\s]+/).filter(Boolean) });
      }}
    />
  );
}

function Block({ node, ctx, inline = false }) {
  const { program } = ctx;
  const spec = BLOCKS[node.type];
  const infix = node.type === "and" || node.type === "or";
  const slots = Object.keys(spec.slots ?? {}).map((name) => (
    <SlotView key={name} node={node} name={name} ctx={ctx} />
  ));

  const head = (
    <>
      {infix && slots[0]}
      <button
        type="button"
        draggable
        title="Klicken zum Löschen, ziehen zum Verschieben"
        onClick={() => ctx.remove(node.id)}
        onDragStart={(event) => ctx.startMove(event, node.id)}
        onDragEnd={ctx.endDrag}
        className="cursor-grab px-0.5 font-semibold active:cursor-grabbing"
      >
        {labelOf(node)}
      </button>

      {node.type === "number" && (
        <button
          type="button"
          title="Klicken, um die Zahl zu ändern"
          onClick={() => ctx.editNumber(node.id)}
          className={`min-w-5 text-center ${inputClass}`}
        >
          {node.props.value}
        </button>
      )}
      {node.type === "funcDef" && (
        <>
          <input
            className={`w-20 ${inputClass}`}
            value={node.props.name}
            placeholder="Name"
            aria-label="Name der Funktion"
            onChange={(event) => program.setProps(node.id, { name: event.target.value.trim() })}
          />
          (<ParamsInput node={node} program={program} />)
        </>
      )}
      {node.type === "comment" && (
        <input
          className={`w-32 ${inputClass}`}
          value={node.props.text}
          placeholder="Text"
          aria-label="Kommentar"
          onChange={(event) => program.setProps(node.id, { text: event.target.value })}
        />
      )}

      {infix ? slots[1] : slots}
      {node.type === "repeat" && <span>mal</span>}
      {node.type === "call" && (
        <>
          {node.lists.args.map((arg) => (
            <Block key={arg.id} node={arg} ctx={ctx} inline />
          ))}
          <Gap inline target={{ parent: node.id, list: "args" }} ctx={ctx} label="+" />
        </>
      )}
    </>
  );

  const color = colorOf(node.type);
  if (inline) {
    return <span className={`inline-flex flex-wrap items-center gap-1 border-2 px-0.5 ${color}`}>{head}</span>;
  }
  return (
    <div className={`m-0.5 border-2 p-0.5 ${color}`}>
      <div className="flex flex-wrap items-center gap-1">{head}</div>
      {node.lists.body && (
        <div className="ml-2 border-l-2 border-white/20 pl-1">
          <StatementList items={node.lists.body} owner={node} ctx={ctx} emptyLabel="hier einfügen" />
        </div>
      )}
    </div>
  );
}

/** Small popup that reads a number from the keyboard. Empty input means 1. */
function NumberDialog({ initial, onSave, onClose }) {
  const [text, setText] = useState(initial);
  const save = () => onSave(text === "" ? 1 : Number(text));

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60" onMouseDown={onClose}>
      <form
        role="dialog"
        aria-label="Zahl eingeben"
        onMouseDown={(event) => event.stopPropagation()}
        onSubmit={(event) => {
          event.preventDefault();
          save();
        }}
        onKeyDown={(event) => {
          if (event.key === "Escape") onClose();
        }}
        className="relative flex w-72 max-w-[90vw] flex-col gap-4 border-4 border-[#d8dad7] bg-[#050505] p-6 text-[#35B23A]"
      >
        <button
          type="button"
          aria-label="Schließen"
          onClick={onClose}
          className="absolute right-1 top-1 flex h-8 w-8 items-center justify-center text-lg text-white/60 hover:bg-white/10 hover:text-white"
        >
          ✕
        </button>

        <h2 className="pr-6 text-sm font-semibold">Gib eine Zahl ein</h2>

        <input
          autoFocus
          inputMode="numeric"
          maxLength={4}
          placeholder="1"
          aria-label="Zahl"
          value={text}
          onFocus={(event) => event.target.select()}
          onChange={(event) => setText(event.target.value.replace(/\D/g, "").replace(/^0+(?=\d)/, ""))}
          className="w-full border-2 border-white/40 bg-black py-2 text-center text-3xl text-white"
        />

        <button
          type="submit"
          className="w-full border-2 border-[#35B23A] py-2 text-sm font-semibold hover:bg-[#35B23A]/20"
        >
          Speichern
        </button>
      </form>
    </div>
  );
}

// ---------- mode switch ----------

/** Toggle between block input and plain text input. */
function ModeSwitch({ mode, onChange }) {
  const isText = mode === "text";
  return (
    <div className="mb-3 flex shrink-0 items-center gap-2 text-[10px] font-semibold">
      <span className={isText ? "text-white/40" : "text-[#ffca20]"}>Blöcke</span>
      <button
        type="button"
        role="switch"
        aria-checked={isText}
        aria-label="Texteingabe statt Blöcke verwenden"
        onClick={() => onChange(isText ? "blocks" : "text")}
        className={`relative h-5 w-10 border-2 ${isText ? "border-[#ffca20] bg-[#ffca20]/25" : "border-white/40 bg-black"
          }`}
      >
        <span
          className={`absolute top-0.5 h-3 w-3 bg-[#ffca20] transition-all ${isText ? "left-6" : "left-0.5"}`}
        />
      </button>
      <span className={isText ? "text-[#ffca20]" : "text-white/40"}>Text</span>
    </div>
  );
}

// ---------- plain text input with error highlighting ----------

const LINE_HEIGHT = 20; // px, must match `leading-5`
const CHAR_WIDTH = 7.2; // px, approx. width of one monospace char at `text-xs`
const EDITOR_TEXT = "font-mono text-xs leading-5 whitespace-pre p-2";

/** Accepts a RunError in PascalCase or camelCase. */
function normalizeError(e) {
  return {
    source: e.source ?? e.Source ?? "",
    message: e.message ?? e.Message ?? "",
    line: e.line ?? e.Line ?? null,
    column: e.column ?? e.Column ?? null,
  };
}

/**
 * Turns a 1-based line/column into a character range of `text`.
 * With a column: the word (or single character) starting there. Without one: the whole line.
 */
function errorRange(text, error) {
  if (!error.line || error.line < 1) return null;
  const lines = text.split("\n");
  const lineIdx = Math.min(error.line, lines.length) - 1;
  const line = lines[lineIdx];
  let lineStart = 0;
  for (let i = 0; i < lineIdx; i++) lineStart += lines[i].length + 1;

  let start = lineStart;
  let end = lineStart + line.length;
  let col = 0;
  const columnUsable = error.column && error.column >= 1 && error.line <= lines.length;
  if (columnUsable && error.column - 1 < line.length) {
    col = error.column - 1;
    start = lineStart + col;
    const word = /^[\p{L}\p{N}_]+/u.exec(line.slice(col));
    end = start + (word ? word[0].length : 1);
  }
  return { start, end, lineIdx, col };
}

/** The invisible copy of the text behind the textarea; only the marked ranges show. */
function Highlighted({ text, ranges }) {
  const parts = [];
  let cursor = 0;
  ranges.forEach((r, i) => {
    if (r.start < cursor) return; // overlapping: skip
    parts.push(text.slice(cursor, r.start));
    parts.push(
      <mark key={i} className="bg-red-500/40 text-transparent underline decoration-red-400 decoration-wavy">
        {r.end > r.start ? text.slice(r.start, r.end) : " "}
      </mark>,
    );
    cursor = r.end;
  });
  parts.push(text.slice(cursor));
  return <>{parts}</>;
}

function TextEditor({ value, onChange, errors }) {
  const areaRef = useRef(null);
  const innerRef = useRef(null);
  const gutterRef = useRef(null);

  const ranges = errors
    .map((e) => errorRange(value, e))
    .filter(Boolean)
    .sort((a, b) => a.start - b.start);

  const lineCount = value.split("\n").length;
  const errorLines = new Set(ranges.map((r) => r.lineIdx));
  const digits = String(lineCount).length;

  function syncScroll() {
    const area = areaRef.current;
    if (!area) return;
    if (innerRef.current) {
      innerRef.current.style.transform = `translate(${-area.scrollLeft}px, ${-area.scrollTop}px)`;
    }
    if (gutterRef.current) {
      gutterRef.current.style.transform = `translateY(${-area.scrollTop}px)`;
    }
  }

  // when new errors arrive: put the caret on the first one and scroll it into view
  useLayoutEffect(() => {
    const area = areaRef.current;
    const first = ranges[0];
    if (!area || !first) return;
    area.focus({ preventScroll: true });
    area.setSelectionRange(first.start, first.start);
    area.scrollTop = Math.max(0, first.lineIdx * LINE_HEIGHT + 8 - area.clientHeight / 2);
    area.scrollLeft = Math.max(0, first.col * CHAR_WIDTH - area.clientWidth / 2);
    syncScroll();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [errors]);

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <div className="flex min-h-0 flex-1 border-4 border-[#d8dad7] bg-[#050505]">
        {/* line numbers: same font and line height as the text, moved along with its scroll position */}
        <div
          className="shrink-0 select-none overflow-hidden border-r border-white/20 bg-black/40 font-mono text-xs"
          style={{ width: `${digits + 2}ch` }}
          aria-hidden="true"
        >
          <div ref={gutterRef} className="py-2 pr-2 text-right leading-5 whitespace-pre">
            {Array.from({ length: lineCount }, (_, i) => (
              <div key={i} className={errorLines.has(i) ? "text-red-400" : "text-white/40"}>
                {i + 1}
              </div>
            ))}
          </div>
        </div>

        <div className="relative min-w-0 flex-1">
          <div className="pointer-events-none absolute inset-0 overflow-hidden" aria-hidden="true">
            <div ref={innerRef} className={`${EDITOR_TEXT} min-w-max text-transparent`}>
              <Highlighted text={value} ranges={ranges} />
            </div>
          </div>
          <textarea
            ref={areaRef}
            wrap="off"
            spellCheck={false}
            autoCapitalize="off"
            autoComplete="off"
            autoCorrect="off"
            value={value}
            placeholder="Schreibe deinen Code hier …"
            aria-label="Code-Eingabefeld (Text)"
            onChange={(event) => onChange(event.target.value)}
            onScroll={syncScroll}
            className={`${EDITOR_TEXT} absolute inset-0 h-full w-full resize-none overflow-auto bg-transparent text-[#ffca20] caret-white outline-none [scrollbar-width:none] [&::-webkit-scrollbar]:hidden`}
          />
        </div>
      </div>

      {errors.length > 0 && (
        <ul className="mt-1 shrink-0 text-[10px] text-red-400" aria-live="polite">
          {errors.map((e, i) => (
            <li key={i}>
              {e.line ? `Zeile ${e.line}${e.column ? `, Spalte ${e.column}` : ""}: ` : ""}
              {e.message}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

// ---------- main component ----------

/**
 * Props:
 *   status           "game over" shows the level-complete hint in the empty block field
 *   onProgramChange  ({ mode, source, errors }) on every edit; errors are block-validation
 *                    errors (always [] in text mode)
 *
 * Methods on the ref (works the same in both modes):
 *   getSource()               current program as a plain string
 *   highlightError(err|[err]) mark RunError(s) in the text input; returns true if something
 *                             was marked (false in block mode or without a line)
 *   clearHighlight()          remove the marks
 *   getMode()                 "blocks" | "text"
 */
const GameInput = forwardRef(function GameInput({ status, onProgramChange }, ref) {
  const [program] = useState(() => new BlockProgram());
  const [, setVersion] = useState(0);
  const [focus, setFocus] = useState({});
  const [drag, setDrag] = useState(null); // { kind: "new", type, props } | { kind: "move", id }
  const [isDragOver, setIsDragOver] = useState(false);
  const [numberDialog, setNumberDialog] = useState(null); // { id, isNew }
  const [mode, setMode] = useState("blocks"); // "blocks" | "text"
  const [text, setText] = useState("");
  const [textErrors, setTextErrors] = useState([]); // normalized RunErrors shown in the text input
  const overRef = useRef(false);
  const notifyRef = useRef(onProgramChange);
  notifyRef.current = onProgramChange;
  const modeRef = useRef(mode);
  const textRef = useRef(text);

  const getSource = useCallback(
    () => (modeRef.current === "blocks" ? program.toSource() : textRef.current),
    [program],
  );

  const emit = useCallback(() => {
    notifyRef.current?.({
      mode: modeRef.current,
      source: getSource(),
      errors: modeRef.current === "blocks" ? program.validate() : [],
    });
  }, [program, getSource]);

  // re-render on every block change; tell the parent the new source text
  useEffect(
    () =>
      program.subscribe(() => {
        setVersion((v) => v + 1);
        if (modeRef.current === "blocks") emit();
      }),
    [program, emit],
  );

  const highlightError = useCallback((errors) => {
    if (modeRef.current !== "text") return false;
    const list = [].concat(errors ?? []).map(normalizeError);
    setTextErrors(list);
    return list.some((e) => e.line);
  }, []);

  const clearHighlight = useCallback(() => setTextErrors([]), []);

  useImperativeHandle(
    ref,
    () => ({ getSource, highlightError, clearHighlight, getMode: () => modeRef.current }),
    [getSource, highlightError, clearHighlight],
  );

  function changeMode(next) {
    if (next === modeRef.current) return;
    modeRef.current = next;
    setMode(next);
    setDrag(null);
    setOver(false);
    setNumberDialog(null);
    setTextErrors([]);
    emit();
  }

  function changeText(value) {
    textRef.current = value;
    setText(value);
    setTextErrors([]); // positions are stale after an edit
    emit();
  }

  const focusTarget = effectiveFocus(program, focus);

  function setOver(value) {
    overRef.current = value;
    setIsDragOver(value);
  }

  function addFromPalette(item) {
    if (program.check(item.type, focusTarget, item.props) !== null) return;
    const id = program.add(item.type, focusTarget, item.props);
    setFocus(nextFocus(program, id));
    if (item.type === "number") setNumberDialog({ id, isNew: true });
  }

  function removeBlock(id) {
    const where = program.locate(id); // keep the insertion point where the block was
    if (program.remove(id) && where) setFocus(where);
  }

  const canDrop = (target) =>
    drag !== null &&
    (drag.kind === "new"
      ? program.canAdd(drag.type, target, drag.props)
      : program.canMove(drag.id, target));

  const ctx = {
    program,
    drag,
    isFocus: (target) => targetKey(target) === targetKey(focusTarget),
    select: setFocus,
    canDrop,
    remove: removeBlock,
    editNumber: (id) => setNumberDialog({ id, isNew: false }),

    dragOver(event, target) {
      event.stopPropagation();
      if (!canDrop(target)) return; // not allowed here: no drop cursor
      event.preventDefault();
      event.dataTransfer.dropEffect = drag.kind === "new" ? "copy" : "move";
    },

    drop(event, target) {
      event.stopPropagation();
      if (!canDrop(target)) return;
      event.preventDefault();
      if (drag.kind === "new") {
        const id = program.add(drag.type, target, drag.props);
        setFocus(nextFocus(program, id));
        if (drag.type === "number") setNumberDialog({ id, isNew: true });
      } else {
        program.move(drag.id, target);
      }
      setDrag(null);
      setOver(false);
    },

    startMove(event, id) {
      event.dataTransfer.setData("text/plain", `block:${id}`);
      event.dataTransfer.effectAllowed = "move";
      setDrag({ kind: "move", id });
    },

    // a placed block dropped outside the input field is thrown away
    endDrag(event) {
      if (drag?.kind === "move" && event.dataTransfer.dropEffect === "none" && !overRef.current) {
        removeBlock(drag.id);
      }
      setDrag(null);
      setOver(false);
    },
  };

  function startNew(event, item) {
    event.dataTransfer.setData("text/plain", item.type);
    event.dataTransfer.effectAllowed = "copy";
    setDrag({ kind: "new", type: item.type, props: item.props });
  }

  return (
    <section className="col-span-full flex min-h-full flex-col" aria-label="Code-Eingabe">
      <ModeSwitch mode={mode} onChange={changeMode} />

      {mode === "text" ? (
        <TextEditor value={text} onChange={changeText} errors={textErrors} />
      ) : (
        <>
          <div className="mb-3" aria-label="Code-Bausteine">
            {paletteItems(program).map((item) => {
              const problem = program.check(item.type, focusTarget, item.props);
              const enabled = problem === null;
              return (
                <button
                  key={item.key}
                  type="button"
                  disabled={!enabled}
                  draggable={enabled}
                  title={problem ?? ""}
                  onClick={() => addFromPalette(item)}
                  onDragStart={(event) => startNew(event, item)}
                  onDragEnd={ctx.endDrag}
                  className={`m-0.5 border-2 p-0.5 text-[10px] font-semibold ${colorOf(item.type)} ${enabled ? "cursor-grab hover:bg-white/10" : "cursor-not-allowed opacity-30"
                    }`}
                >
                  {item.label}
                </button>
              );
            })}
          </div>

          <div
            onDragEnter={() => setOver(true)}
            onDragLeave={(event) => {
              if (!event.currentTarget.contains(event.relatedTarget)) setOver(false);
            }}
            aria-label="Code-Eingabefeld. Klicke einen Baustein an, um ihn an der gelben Stelle einzufügen, oder zieh ihn hinein. Klicke einen eingebauten Baustein an, um ihn zu löschen."
            className={`min-h-[76px] border-4 bg-[#050505] p-1 text-xs text-[#ffca20] max-[640px]:min-h-[58px] ${isDragOver ? "border-[#ffca20]" : "border-[#d8dad7]"
              }`}
          >
            <StatementList
              items={program.root}
              owner={null}
              ctx={ctx}
              emptyLabel={status === "game over" ? "LEVEL GESCHAFFT" : "Zieh die Bausteine hierher"}
            />
          </div>

          {numberDialog && program.get(numberDialog.id) && (
            <NumberDialog
              key={numberDialog.id}
              initial={numberDialog.isNew ? "" : String(program.get(numberDialog.id).props.value)}
              onSave={(value) => {
                program.setProps(numberDialog.id, { value });
                setNumberDialog(null);
              }}
              onClose={() => setNumberDialog(null)}
            />
          )}
        </>
      )}
    </section>
  );
});

export default GameInput;