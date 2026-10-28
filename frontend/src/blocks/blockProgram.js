// src/blocks/blockProgram.js
//
// Block model for MazeLang. Holds the program as a tree and turns it into
// source text on demand. No UI framework: wire it up through subscribe().

const IDENT = /^[a-zäöüß_][a-zäöüß0-9_]*$/i;

/** "Groß/Klein, ä/ae egal": compare names through this. */
export const normalizeName = (s) =>
  String(s).toLowerCase().replace(/ä/g, 'ae').replace(/ö/g, 'oe').replace(/ü/g, 'ue').replace(/ß/g, 'ss');
const norm = normalizeName;

const RESERVED = new Set(
  ['gehe', 'wiederhole', 'mal', 'immer', 'wenn', 'sonst', 'ende', 'funktion', 'ergebnis', 'nicht', 'und',
   'oder', 'frei', 'besucht', 'hoch', 'runter', 'links', 'rechts', 'rückwärts', 'zurück'].map(norm),
);
const isName = (s) => IDENT.test(s) && !RESERVED.has(norm(s));

export const DIRECTIONS = ['hoch', 'runter', 'links', 'rechts', 'rückwärts'];

// What may be dropped into which slot
const ATOMS = ['dir', 'number', 'param'];
const DIR_SLOT = ['dir', 'param'];
const NUM_SLOT = ['number', 'param'];
const CONDITIONS = ['free', 'visited', 'not', 'and', 'or', 'call'];
const VALUES = [...ATOMS, ...CONDITIONS];

// nicht > und > oder. Everything else binds tightest.
const PREC = { or: 1, and: 2, not: 3 };
const rank = (type) => PREC[type] ?? 4;

// What kind of value a built-in slot needs: 'dir' | 'number'
const SLOT_KIND = {
  go: { dir: 'dir' },
  free: { dir: 'dir' },
  visited: { dir: 'dir' },
  repeat: { count: 'number' },
};

const KIND_WORD = { dir: 'Richtung', number: 'Zahl' };

const slot = (label, accepts) => ({ label, accepts });
const body = { kind: 'stmt' };

/**
 * Block definitions. `kinds` says where a block may be placed:
 *   'stmt' = in a statement list (program root or a body)
 *   'expr' = in a slot or an argument list
 */
export const BLOCKS = {
  // --- statements ---
  go:      { label: 'gehe',             category: 'Bewegung',   kinds: ['stmt'],
             slots: { dir: slot('Richtung', DIR_SLOT) } },
  repeat:  { label: 'wiederhole … mal', category: 'Schleifen',  kinds: ['stmt'],
             slots: { count: slot('Anzahl', NUM_SLOT) }, lists: { body } },
  forever: { label: 'immer',            category: 'Schleifen',  kinds: ['stmt'],
             lists: { body } },
  if:      { label: 'wenn',             category: 'Bedingung',  kinds: ['stmt'],
             slots: { cond: slot('Sensor', CONDITIONS) }, lists: { body } },
  else:    { label: 'sonst',            category: 'Bedingung',  kinds: ['stmt'],
             lists: { body } },
  funcDef: { label: 'funktion',         category: 'Funktionen', kinds: ['stmt'],
             props: { name: '', params: [] }, lists: { body } },
  return:  { label: 'ergebnis',         category: 'Funktionen', kinds: ['stmt'],
             slots: { value: slot('Wert', VALUES) } },
  comment: { label: '# Kommentar',      category: 'Sonstiges',  kinds: ['stmt'],
             props: { text: '' } },

  // --- statement OR expression ---
  call:    { label: 'Funktion aufrufen', category: 'Funktionen', kinds: ['stmt', 'expr'],
             props: { name: '' }, lists: { args: { kind: 'expr', accepts: ATOMS } } },

  // --- expressions ---
  dir:     { label: 'Richtung',  category: 'Werte',    kinds: ['expr'], props: { value: 'hoch' } },
  number:  { label: 'Zahl',      category: 'Werte',    kinds: ['expr'], props: { value: 1 } },
  param:   { label: 'Parameter', category: 'Werte',    kinds: ['expr'], props: { name: '' } },
  free:    { label: 'frei',      category: 'Sensoren', kinds: ['expr'], slots: { dir: slot('Richtung', DIR_SLOT) } },
  visited: { label: 'besucht',   category: 'Sensoren', kinds: ['expr'], slots: { dir: slot('Richtung', DIR_SLOT) } },
  not:     { label: 'nicht',     category: 'Logik',    kinds: ['expr'], slots: { a: slot('Bedingung', CONDITIONS) } },
  and:     { label: 'und',       category: 'Logik',    kinds: ['expr'],
             slots: { a: slot('links', CONDITIONS), b: slot('rechts', CONDITIONS) } },
  or:      { label: 'oder',      category: 'Logik',    kinds: ['expr'],
             slots: { a: slot('links', CONDITIONS), b: slot('rechts', CONDITIONS) } },
};

/** Blocks for the palette, ready to group by `category`. */
export function getPalette() {
  return Object.entries(BLOCKS).map(([type, s]) => ({
    type, label: s.label, category: s.category, kinds: s.kinds,
  }));
}

export class BlockProgram {
  constructor() {
    this.root = [];
    this._next = 1;
    this._listeners = new Set();
  }

  // ---------- placement checks ----------

  /**
   * Why a block can't go there, or null if it can.
   *   target = {}                          -> end of the program
   *   target = { index }                   -> into the program at index
   *   target = { parent, list, index? }    -> into a body ("body") or argument list ("args")
   *   target = { parent, slot }            -> into a slot
   * `props` matters for blocks whose placement depends on a value
   * (a "param" block's name, a "dir" block's direction).
   */
  check(type, target = {}, props = {}) {
    let r;
    try {
      r = this._resolve(target);
    } catch (e) {
      return e.message;
    }
    return this._problem(type, props, r, target.index);
  }

  canAdd(type, target = {}, props = {}) {
    return this.check(type, target, props) === null;
  }

  /** Same as canAdd, but for a block that is already in the program. */
  canMove(id, target = {}) {
    return this._tryMove(id, target, false);
  }

  // ---------- editing ----------

  /** Add a block. Returns its id. Throws if it doesn't fit there (see check()). */
  add(type, target = {}, props = {}) {
    const r = this._resolve(target);
    const problem = this._problem(type, props, r, target.index);
    if (problem) throw new Error(problem);
    const node = this._create(type, props);
    this._place(node, r, target.index);
    this._emit();
    return node.id;
  }

  /** Move a block (with everything inside it). Returns false if it doesn't fit there. */
  move(id, target = {}) {
    return this._tryMove(id, target, true);
  }

  /** Remove a block and everything inside it. Call this when a placed block is clicked. */
  remove(id) {
    const hit = this._find(id);
    if (!hit) return false;
    if (Array.isArray(hit.holder)) hit.holder.splice(hit.key, 1);
    else hit.holder[hit.key] = null;
    this._emit();
    return true;
  }

  /** Change values like a number, function name, direction or parameter list. */
  setProps(id, props) {
    const hit = this._find(id);
    if (!hit) throw new Error(`Block ${id} nicht gefunden`);
    Object.assign(hit.node.props, props);
    this._emit();
  }

  clear() {
    this.root = [];
    this._emit();
  }

  // ---------- lookups ----------

  get(id) {
    return this._find(id)?.node ?? null;
  }

  /** Where a block sits, as a target you can pass to add()/check(). */
  locate(id) {
    const h = this._find(id);
    if (!h) return null;
    return Array.isArray(h.holder)
      ? { parent: h.owner?.id, list: h.owner ? h.name : undefined, index: h.key }
      : { parent: h.owner.id, slot: h.name };
  }

  /** Blocks from the program root down to (and including) the given block. */
  path(id) {
    return this._path(id);
  }

  /** All user-defined functions with a valid name. */
  functions() {
    const out = [];
    const visit = (n) => {
      if (n.type === 'funcDef' && isName(n.props.name)) {
        out.push({
          id: n.id,
          node: n,
          name: n.props.name,
          params: n.props.params.filter(isName),
          arity: n.props.params.length,
        });
      }
      Object.values(n.slots).forEach((s) => s && visit(s));
      Object.values(n.lists).forEach((l) => l.forEach(visit));
    };
    this.root.forEach(visit);
    return out;
  }

  // ---------- persistence ----------

  toJSON() {
    return { root: this.root };
  }

  load(data) {
    this.root = structuredClone(data.root ?? []);
    let max = 0;
    const visit = (n) => {
      max = Math.max(max, Number(String(n.id).slice(1)) || 0);
      Object.values(n.slots).forEach((s) => s && visit(s));
      Object.values(n.lists).forEach((l) => l.forEach(visit));
    };
    this.root.forEach(visit);
    this._next = max + 1;
    this._emit();
  }

  // ---------- change notifications ----------

  /** Returns an unsubscribe function. */
  subscribe(fn) {
    this._listeners.add(fn);
    return () => this._listeners.delete(fn);
  }

  // ---------- source text ----------

  /** Source text for the backend. Empty slots appear as "?"; check validate() first. */
  toSource() {
    const lines = this._stmts(this.root, 0);
    return lines.length ? lines.join('\n') + '\n' : '';
  }

  _stmts(list, depth) {
    const pad = '  '.repeat(depth);
    const out = [];
    for (let i = 0; i < list.length; i++) {
      const n = list[i];
      const inner = () => this._stmts(n.lists.body, depth + 1);
      switch (n.type) {
        case 'go':
          out.push(`${pad}gehe ${this._expr(n.slots.dir)}`);
          break;
        case 'repeat':
          out.push(`${pad}wiederhole ${this._expr(n.slots.count)} mal`, ...inner(), `${pad}ende`);
          break;
        case 'forever':
          out.push(`${pad}immer`, ...inner(), `${pad}ende`);
          break;
        case 'if': {
          // a "sonst" right after belongs to this "wenn"
          const els = list[i + 1]?.type === 'else' ? list[++i] : null;
          out.push(...this._chainLines(n, els, depth, 'wenn'), `${pad}ende`);
          break;
        }
        case 'else': // no "wenn" before it (validate() reports this)
          out.push(`${pad}sonst`, ...inner(), `${pad}ende`);
          break;
        case 'funcDef':
          out.push(`${pad}funktion ${n.props.name || '?'}(${n.props.params.join(', ')})`, ...inner(), `${pad}ende`);
          break;
        case 'return':
          out.push(`${pad}ergebnis ${this._expr(n.slots.value)}`);
          break;
        case 'comment':
          out.push(`${pad}# ${String(n.props.text).replace(/\s*\n\s*/g, ' ')}`);
          break;
        case 'call':
          out.push(pad + this._expr(n));
          break;
      }
    }
    return out;
  }

  /**
   * "wenn … sonst …" without the closing "ende". If the "sonst" holds nothing but
   * another "wenn" (with its own optional "sonst"), the two are written as
   * "sonst wenn" and share one "ende".
   */
  _chainLines(ifNode, elseNode, depth, head) {
    const pad = '  '.repeat(depth);
    const out = [`${pad}${head} ${this._expr(ifNode.slots.cond)}`, ...this._stmts(ifNode.lists.body, depth + 1)];
    if (elseNode) {
      const b = elseNode.lists.body;
      if (b[0]?.type === 'if' && (b.length === 1 || (b.length === 2 && b[1].type === 'else'))) {
        out.push(...this._chainLines(b[0], b[1] ?? null, depth, 'sonst wenn'));
      } else {
        out.push(`${pad}sonst`, ...this._stmts(b, depth + 1));
      }
    }
    return out;
  }

  _expr(n) {
    if (!n) return '?';
    switch (n.type) {
      case 'dir':     return n.props.value;
      case 'number':  return String(n.props.value);
      case 'param':   return n.props.name || '?';
      case 'free':    return `frei ${this._expr(n.slots.dir)}`;
      case 'visited': return `besucht ${this._expr(n.slots.dir)}`;
      case 'not':     return `nicht ${this._expr(n.slots.a)}`;
      case 'and':     return `${this._expr(n.slots.a)} und ${this._expr(n.slots.b)}`;
      case 'or':      return `${this._expr(n.slots.a)} oder ${this._expr(n.slots.b)}`;
      case 'call':    return [n.props.name || '?', ...n.lists.args.map((a) => this._expr(a))].join(' ');
      default:        return '?';
    }
  }

  // ---------- parameter kinds (direction / number) ----------

  /** Map: normalized function name -> { arity, params, node, kinds }. First definition wins. */
  _functionTable() {
    const fns = new Map();
    for (const f of this.functions()) {
      const key = norm(f.name);
      if (fns.has(key)) continue;
      fns.set(key, {
        arity: f.arity,
        params: f.node.props.params,
        node: f.node,
        kinds: new Array(f.arity).fill(null), // null | 'dir' | 'number'
      });
    }
    return fns;
  }

  /**
   * Infers whether each parameter is a direction or a number from how it is used.
   * The first use that decides wins; kinds propagate through calls in both
   * directions until nothing changes. Mirrors the compiler.
   */
  _inferKinds(fns) {
    const paramIndex = (fn, name) => fn.params.findIndex((p) => norm(p) === norm(name));

    const kindOf = (fn, v) => {
      if (!v) return null;
      if (v.type === 'dir') return 'dir';
      if (v.type === 'number') return 'number';
      if (v.type === 'param') {
        const i = paramIndex(fn, v.props.name);
        return i >= 0 ? fn.kinds[i] : null;
      }
      return null;
    };

    const constrain = (fn, v, kind) => {
      if (!kind || !v || v.type !== 'param') return false;
      const i = paramIndex(fn, v.props.name);
      if (i < 0 || fn.kinds[i]) return false;
      fn.kinds[i] = kind;
      return true;
    };

    const walk = (fn, n) => {
      let changed = false;
      for (const [slotName, kind] of Object.entries(SLOT_KIND[n.type] ?? {})) {
        changed = constrain(fn, n.slots[slotName], kind) || changed;
      }
      if (n.type === 'call') {
        const callee = fns.get(norm(n.props.name ?? ''));
        if (callee) {
          n.lists.args.forEach((arg, i) => {
            if (i >= callee.kinds.length) return;
            const k = kindOf(fn, arg);
            if (!k) changed = constrain(fn, arg, callee.kinds[i]) || changed;     // callee -> caller
            else if (!callee.kinds[i]) { callee.kinds[i] = k; changed = true; }   // caller -> callee
          });
        }
      }
      for (const s of Object.values(n.slots)) if (s) changed = walk(fn, s) || changed;
      for (const l of Object.values(n.lists)) for (const c of l) changed = walk(fn, c) || changed;
      return changed;
    };

    let changed;
    do {
      changed = false;
      for (const fn of fns.values()) {
        for (const stmt of fn.node.lists.body) changed = walk(fn, stmt) || changed;
      }
    } while (changed);
  }

  // ---------- validation ----------

  /** Returns [{ id, message }]. Empty array means the program can be sent. */
  validate() {
    const errors = [];
    const fns = this._functionTable();
    this._inferKinds(fns);
    this._checkStmts(this.root, { top: true, inFunc: false, params: [], fns, fn: null }, errors);
    return errors;
  }

  _checkStmts(list, ctx, errors) {
    const err = (n, message) => errors.push({ id: n.id, message });

    list.forEach((n, i) => {
      const prev = list[i - 1];
      if (n.type === 'else' && prev?.type !== 'if') err(n, '„sonst“ braucht ein „wenn“ direkt davor');
      if (n.type === 'funcDef' && !ctx.top) {
        err(n, 'Eine Funktion kann nur ganz außen stehen, nicht in einem anderen Block');
      }
      if (n.type === 'return' && !ctx.inFunc) err(n, '„ergebnis“ geht nur in einer Funktion');
      if (n.type === 'funcDef') {
        if (!isName(n.props.name)) err(n, 'Gib der Funktion einen Namen (ein Wort, kein Befehl)');
        n.props.params.forEach((p) => {
          if (!isName(p)) err(n, `„${p}“ geht nicht als Name für einen Parameter`);
        });
      }
      if (n.type === 'comment' && /\n/.test(n.props.text)) err(n, 'Ein Kommentar darf nur eine Zeile lang sein');

      this._checkNode(n, ctx, errors);
      if (n.lists.body) {
        const inner = n.type === 'funcDef'
          ? {
              ...ctx,
              top: false,
              inFunc: true,
              params: [...ctx.params, ...n.props.params],
              fn: ctx.fns.get(norm(n.props.name ?? '')) ?? null,
            }
          : { ...ctx, top: false };
        this._checkStmts(n.lists.body, inner, errors);
      }
    });
  }

  /** `expected` is 'dir' | 'number' | null. Reports mix-ups between directions and numbers. */
  _expectKind(v, expected, ctx, errors) {
    if (!v || !expected) return;
    const err = (message) => errors.push({ id: v.id, message });

    if (v.type === 'dir' && expected === 'number') {
      err('Hier wird eine Zahl erwartet, keine Richtung');
    } else if (v.type === 'number' && expected === 'dir') {
      err('Hier wird eine Richtung erwartet, keine Zahl');
    } else if (v.type === 'param' && ctx.fn) {
      const i = ctx.fn.params.findIndex((p) => norm(p) === norm(v.props.name));
      const actual = i >= 0 ? ctx.fn.kinds[i] : null;
      if (actual && actual !== expected) {
        err(`„${v.props.name}“ wird als ${KIND_WORD[actual]} und als ${KIND_WORD[expected]} benutzt`);
      }
    }
  }

  /** Checks the slots and arguments of one node, recursively. */
  _checkNode(n, ctx, errors, allowBack = false) {
    const err = (node, message) => errors.push({ id: node.id, message });
    const spec = BLOCKS[n.type];

    if (n.type === 'dir' && n.props.value === 'rückwärts' && !allowBack) {
      err(n, '„rückwärts“ geht nur direkt nach „gehe“');
    }
    if (n.type === 'number') {
      const v = n.props.value;
      if (!Number.isInteger(v) || v < 0) err(n, 'Die Zahl muss eine ganze Zahl ab 0 sein');
    }
    if (n.type === 'param' && !ctx.params.some((p) => norm(p) === norm(n.props.name))) {
      err(n, 'Diesen Parameter gibt es hier nicht');
    }
    if (n.type === 'call') {
      const name = n.props.name;
      if (!isName(name)) err(n, 'Funktionsname fehlt');
      else if (!ctx.fns.has(norm(name))) err(n, `Die Funktion „${name}“ gibt es nicht`);
      else {
        const callee = ctx.fns.get(norm(name));
        if (callee.arity !== n.lists.args.length) {
          err(n, `„${name}“ braucht ${callee.arity} Wert${callee.arity === 1 ? '' : 'e'}`);
        } else {
          n.lists.args.forEach((a, i) => this._expectKind(a, callee.kinds[i], ctx, errors));
        }
      }
    }

    for (const [name, def] of Object.entries(spec.slots ?? {})) {
      const child = n.slots[name];
      if (!child) {
        err(n, `Bei „${spec.label}“ fehlt noch: ${def.label}`);
        continue;
      }
      if (rank(child.type) < (PREC[n.type] ?? 0)) {
        err(child, 'Hier ist unklar, was zuerst gilt. Nimm ein zweites „wenn“ in das erste.');
      }
      this._expectKind(child, SLOT_KIND[n.type]?.[name], ctx, errors);
      this._checkNode(child, ctx, errors, n.type === 'go' && name === 'dir');
    }
    for (const child of n.lists.args ?? []) this._checkNode(child, ctx, errors);
  }

  // ---------- internals ----------

  /** Turns a target into the thing it points at. Throws on a bad target. */
  _resolve({ parent = null, slot: slotName, list: listName } = {}) {
    if (parent === null) {
      return { owner: null, slotName: null, container: this.root, kind: 'stmt', accepts: null };
    }
    const owner = this._find(parent)?.node;
    if (!owner) throw new Error(`Block ${parent} nicht gefunden`);
    const ospec = BLOCKS[owner.type];
    if (slotName) {
      const def = ospec.slots?.[slotName];
      if (!def) throw new Error(`${owner.type} hat keinen Slot "${slotName}"`);
      return { owner, slotName, container: null, kind: 'expr', accepts: def.accepts };
    }
    const name = listName ?? Object.keys(ospec.lists ?? {})[0];
    const def = ospec.lists?.[name];
    if (!def) throw new Error(`${owner.type} hat keine Liste "${name}"`);
    return { owner, slotName: null, container: owner.lists[name], kind: def.kind, accepts: def.accepts ?? null };
  }

  /** Placement rules. Returns a message, or null if the block may go there. */
  _problem(type, props, r, index) {
    const spec = BLOCKS[type];
    if (!spec) return `Unbekannter Block: ${type}`;

    if (!spec.kinds.includes(r.kind) || (r.accepts && !r.accepts.includes(type))) {
      return `„${spec.label}“ passt hier nicht hin`;
    }

    const scope = this._scope(r.owner);
    if (type === 'return' && !scope.inFunc) return '„ergebnis“ geht nur in einer Funktion';
    if (type === 'param' && !scope.params.some((p) => norm(p) === norm(props.name ?? ''))) {
      return 'Diesen Parameter gibt es hier nicht';
    }
    if (type === 'dir' && props.value === 'rückwärts' && !(r.owner?.type === 'go' && r.slotName === 'dir')) {
      return '„rückwärts“ geht nur direkt nach „gehe“';
    }

    if (type === 'funcDef' && r.owner) {
      return 'Eine Funktion kann nur ganz außen stehen, nicht in einem anderen Block';
    }

    if (r.kind === 'stmt') {
      const list = r.container;
      const at = index ?? list.length;
      const prev = list[at - 1];
      const next = list[at];
      if (type === 'else' && prev?.type !== 'if') return '„sonst“ braucht ein „wenn“ direkt davor';
      if (prev?.type === 'if' && next?.type === 'else') {
        return 'Zwischen „wenn“ und „sonst“ darf nichts anderes stehen';
      }
    }
    return null;
  }

  /** Enclosing functions of a block: are we inside one, and which parameters exist. */
  _scope(owner) {
    const funcs = owner ? (this._path(owner.id) ?? []).filter((n) => n.type === 'funcDef') : [];
    return { inFunc: funcs.length > 0, params: funcs.flatMap((f) => f.props.params) };
  }

  _place(node, r, index) {
    if (r.slotName) r.owner.slots[r.slotName] = node;
    else r.container.splice(index ?? r.container.length, 0, node);
  }

  _tryMove(id, target, commit) {
    const hit = this._find(id);
    if (!hit) return false;
    // a block can't go into itself
    if (target.parent && (target.parent === id || this._findIn(hit.node, target.parent))) return false;

    let r;
    try {
      r = this._resolve(target);
    } catch {
      return false;
    }

    // target.index counts positions before the block was taken out
    let index = target.index;
    if (index !== undefined && r.container === hit.holder && hit.key < index) index -= 1;

    const detach = () => (Array.isArray(hit.holder) ? hit.holder.splice(hit.key, 1) : (hit.holder[hit.key] = null));
    const reattach = () => (Array.isArray(hit.holder) ? hit.holder.splice(hit.key, 0, hit.node) : (hit.holder[hit.key] = hit.node));

    detach();
    const problem = this._problem(hit.node.type, hit.node.props, r, index);
    if (problem !== null || !commit) {
      reattach();
      return problem === null;
    }
    this._place(hit.node, r, index);
    this._emit();
    return true;
  }

  _create(type, props) {
    const spec = BLOCKS[type];
    return {
      id: `b${this._next++}`,
      type,
      props: { ...structuredClone(spec.props ?? {}), ...props },
      slots: Object.fromEntries(Object.keys(spec.slots ?? {}).map((k) => [k, null])),
      lists: Object.fromEntries(Object.keys(spec.lists ?? {}).map((k) => [k, []])),
    };
  }

  _find(id, list = this.root, owner = null, name = null) {
    for (let i = 0; i < list.length; i++) {
      const n = list[i];
      if (n.id === id) return { node: n, holder: list, key: i, owner, name };
      const hit = this._findIn(n, id);
      if (hit) return hit;
    }
    return null;
  }

  /** Looks for a block below `n` (not `n` itself). */
  _findIn(n, id) {
    for (const [key, child] of Object.entries(n.slots)) {
      if (!child) continue;
      if (child.id === id) return { node: child, holder: n.slots, key, owner: n, name: key };
      const hit = this._findIn(child, id);
      if (hit) return hit;
    }
    for (const [name, l] of Object.entries(n.lists)) {
      const hit = this._find(id, l, n, name);
      if (hit) return hit;
    }
    return null;
  }

  _path(id, nodes = this.root, trail = []) {
    for (const n of nodes) {
      const here = [...trail, n];
      if (n.id === id) return here;
      const kids = [...Object.values(n.slots).filter(Boolean), ...Object.values(n.lists).flat()];
      const hit = this._path(id, kids, here);
      if (hit) return hit;
    }
    return null;
  }

  _emit() {
    this._listeners.forEach((fn) => fn(this));
  }
}