#!/usr/bin/env python3
"""Prototype: run a German maze DSL program on a maze JSON and animate it.

Usage: python maze_proto.py programm.txt maze.json [--delay 0.15] [--limit 20000]
Standard library only.
"""
import argparse, json, os, re, sys, time

SIDES = {"hoch": (-1, 0, "top"), "runter": (1, 0, "bottom"),
         "links": (0, -1, "left"), "rechts": (0, 1, "right")}
BACK = {"rueckwaerts", "zurueck"}
BUILTINS = {"gehe": 1, "frei": 1, "besucht": 1}          # name -> Parameteranzahl
RESERVED = ({"wiederhole", "mal", "immer", "wenn", "sonst", "ende", "funktion",
             "ergebnis", "nicht", "und", "oder", "wahr", "falsch"}
            | set(SIDES) | BACK | set(BUILTINS))
MAX_DEPTH = 100
sys.setrecursionlimit(20000)


# ---------------------------------------------------------------- lexer
def norm(s):
    s = s.lower()
    for a, b in (("ä", "ae"), ("ö", "oe"), ("ü", "ue"), ("ß", "ss")):
        s = s.replace(a, b)
    return s

def tokenize(src):
    return [(t, n) for n, line in enumerate(src.splitlines(), 1)
            for t in re.findall(r"\d+|\w+|\S", norm(line.split("#")[0]))]


# --------------------------------------------------------------- parser
class Parser:
    def __init__(self, toks):
        self.t, self.i = toks, 0
        self.funcs = dict(BUILTINS)   # Name -> Parameteranzahl (auch eigene)
        self.defs = {}                # Name -> (Parameter, Körper)
        self.params = None            # Parameter der gerade geparsten Funktion
        self.nest = 0

    def peek(self):
        return self.t[self.i][0] if self.i < len(self.t) else None

    def next(self):
        if self.i >= len(self.t):
            raise SyntaxError("Das Programm hört zu früh auf. Fehlt ein 'ende'?")
        self.i += 1
        return self.t[self.i - 1]

    def expect(self, *words):
        t, n = self.next()
        if t not in words:
            raise SyntaxError(f"Zeile {n}: Ich habe {' / '.join(words)} erwartet, "
                              f"aber '{t}' gefunden.")
        return t

    def block(self, stops):
        out = []
        self.nest += 1
        while self.peek() not in stops:
            s = self.stmt()
            if s:
                out.append(s)
        self.nest -= 1
        return out

    # --- Argumente eines Aufrufs: genau so viele Wörter wie Parameter
    def arg(self, fname, n_expected):
        t, n = self.next()
        if t in SIDES:
            return ("dir", t)
        if t in BACK:
            if fname != "gehe":
                raise SyntaxError(f"Zeile {n}: 'rückwärts' geht nur bei 'gehe'.")
            return ("dir", "back")
        if self.params and t in self.params:
            return ("var", t)
        if t in RESERVED or t.isdigit() or not t.isidentifier():
            raise SyntaxError(f"Zeile {n}: '{fname}' braucht {n_expected} Argument(e), "
                              f"aber hier steht '{t}'.")
        raise SyntaxError(f"Zeile {n}: '{t}' ist weder eine Richtung noch ein Parameter.")

    def args(self, fname):
        k = self.funcs[fname]
        return [self.arg(fname, k) for _ in range(k)]

    # --- Ausdrücke: nicht > und > oder
    def expr(self):
        e = self.and_expr()
        while self.peek() == "oder":
            self.next()
            e = ("or", e, self.and_expr())
        return e

    def and_expr(self):
        e = self.not_expr()
        while self.peek() == "und":
            self.next()
            e = ("and", e, self.not_expr())
        return e

    def not_expr(self):
        if self.peek() == "nicht":
            self.next()
            return ("not", self.not_expr())
        t, n = self.next()
        if t in ("wahr", "falsch"):
            return ("lit", t == "wahr")
        if t in self.funcs:
            return ("call", t, self.args(t))
        raise SyntaxError(f"Zeile {n}: Hier erwarte ich eine Bedingung, "
                          f"aber '{t}' ist keine Funktion.")

    # --- Funktionsdefinition
    def funcdef(self, n):
        if self.nest != 1 or self.params is not None:
            raise SyntaxError(f"Zeile {n}: Funktionen darf man nur ganz außen definieren.")
        name, n2 = self.next()
        if name in RESERVED or name in self.funcs or not name.isidentifier() or name.isdigit():
            raise SyntaxError(f"Zeile {n2}: '{name}' ist als Funktionsname nicht möglich.")
        self.expect("(")
        params = []
        if self.peek() != ")":
            while True:
                p, n3 = self.next()
                if p in RESERVED or p in self.funcs or p in params or not p.isidentifier():
                    raise SyntaxError(f"Zeile {n3}: '{p}' ist als Parametername nicht möglich.")
                params.append(p)
                if self.peek() == ",":
                    self.next(); continue
                break
        self.expect(")")
        if not params:
            raise SyntaxError(f"Zeile {n2}: Die Funktion '{name}' braucht mindestens einen Parameter.")
        self.funcs[name] = len(params)       # vorher eintragen: Rekursion möglich
        self.params = set(params)
        body = self.block({"ende"}); self.next()
        self.params = None
        self.defs[name] = (params, body)
        return None

    def stmt(self):
        t, n = self.next()
        if t == "funktion":
            return self.funcdef(n)
        if t == "ergebnis":
            if self.params is None:
                raise SyntaxError(f"Zeile {n}: 'ergebnis' gibt es nur in einer Funktion.")
            return ("ret", self.expr())
        if t == "gehe":
            d = self.arg("gehe", 1)
            if d == ("dir", "back"):
                return ("back",)
            k = int(self.next()[0]) if (self.peek() or "").isdigit() else 1
            return ("go", d, k)
        if t == "wiederhole":
            c, n2 = self.next()
            if not c.isdigit():
                raise SyntaxError(f"Zeile {n2}: Nach 'wiederhole' muss eine Zahl stehen.")
            if self.peek() == "mal":
                self.next()
            body = self.block({"ende"}); self.next()
            return ("rep", int(c), body)
        if t == "immer":
            body = self.block({"ende"}); self.next()
            return ("rep", None, body)
        if t == "wenn":
            branches, other = [], []
            while True:
                cond = self.expr()
                branches.append((cond, self.block({"sonst", "ende"})))
                if self.peek() == "sonst":
                    self.next()
                    if self.peek() == "wenn":
                        self.next(); continue
                    other = self.block({"ende"})
                self.expect("ende"); break
            return ("if", branches, other)
        if t in self.funcs:
            return ("call", t, self.args(t))
        hint = " Ist die Funktion vorher definiert?" if t.isidentifier() and t not in RESERVED else ""
        raise SyntaxError(f"Zeile {n}: Das Wort '{t}' kenne ich nicht.{hint}")


def parse(src):
    p = Parser(tokenize(src))
    prog = p.block({None})
    return prog, p.defs


# ---------------------------------------------------------------- world
class Stop(Exception):
    pass

class World:
    def __init__(self, m, limit):
        e, x = m["entrance"], m["exit"]
        self.pos, self.exit = (e["y"], e["x"]), (x["y"], x["x"])
        self.h, self.w = m["height"], m["width"]
        self.walls = {(c["y"], c["x"]): {s.lower() for s in c["walls"]} for c in m["cells"]}
        self.visited, self.history = {self.pos}, []
        self.steps, self.budget = 0, limit
        self.funcs, self.depth = {}, 0

    def tick(self):
        self.budget -= 1
        if self.budget < 0:
            raise Stop("Zu viele Befehle. Läuft dein Programm im Kreis?")

    def neighbor(self, d):
        dy, dx, _ = SIDES[d]
        return (self.pos[0] + dy, self.pos[1] + dx)

    def target(self, d):
        nxt = self.neighbor(d)
        return nxt if SIDES[d][2] not in self.walls[self.pos] and nxt in self.walls else None

    def frei(self, d):
        return self.target(d) is not None

    def besucht(self, d):
        nxt = self.neighbor(d)
        return nxt in self.walls and nxt in self.visited

    def go(self, d):
        nxt = self.target(d)
        if nxt is None:
            raise Stop(f"Crash! Da ist eine Wand ({d}).")
        self.history.append(self.pos)
        self._arrive(nxt)

    def back(self):
        if not self.history:
            raise Stop("Crash! Es gibt keinen Schritt zurück.")
        self._arrive(self.history.pop())

    def _arrive(self, cell):
        self.pos = cell
        self.visited.add(cell)
        self.steps += 1


# ---------------------------------------------------------- interpreter
def value(a, env):
    return env[a[1]] if a[0] == "var" else a[1]

def cond(e, w, env):
    """Wertet einen Ausdruck aus und verlangt wahr/falsch."""
    v = yield from ev(e, w, env)
    if not isinstance(v, bool):
        who = f" '{e[1]}'" if e[0] == "call" else ""
        raise Stop(f"Die Funktion{who} liefert kein Ergebnis, "
                   f"wird hier aber als Bedingung gebraucht.")
    return v

def ev(e, w, env):
    k = e[0]
    if k == "lit":
        return e[1]
    if k == "not":
        return not (yield from cond(e[1], w, env))
    if k == "and":
        if not (yield from cond(e[1], w, env)):
            return False
        return (yield from cond(e[2], w, env))
    if k == "or":
        if (yield from cond(e[1], w, env)):
            return True
        return (yield from cond(e[2], w, env))
    return (yield from call(e[1], e[2], w, env))

def call(name, args, w, env):
    w.tick()
    vals = [value(a, env) for a in args]
    if name == "frei":
        return w.frei(vals[0])
    if name == "besucht":
        return w.besucht(vals[0])
    if name == "gehe":
        w.go(vals[0]); yield
        return None
    params, body = w.funcs[name]
    w.depth += 1
    if w.depth > MAX_DEPTH:
        raise Stop(f"Die Funktion '{name}' ruft sich zu tief selbst auf.")
    r = yield from run(body, w, dict(zip(params, vals)))
    w.depth -= 1
    return r[1] if r else None

def run(block, w, env):
    """Generator: yields once after every successful move.
    Gibt ("ret", wert) zurück, wenn 'ergebnis' erreicht wurde, sonst None."""
    for st in block:
        w.tick()
        k = st[0]
        if k == "go":
            d = value(st[1], env)
            for _ in range(st[2]):
                w.go(d); yield
        elif k == "back":
            w.back(); yield
        elif k == "call":
            yield from call(st[1], st[2], w, env)
        elif k == "ret":
            return ("ret", (yield from ev(st[1], w, env)))
        elif k == "rep":
            i = 0
            while st[1] is None or i < st[1]:
                w.tick()
                r = yield from run(st[2], w, env)
                if r:
                    return r
                i += 1
        else:  # if
            for c, body in st[1]:
                if (yield from cond(c, w, env)):
                    r = yield from run(body, w, env)
                    break
            else:
                r = yield from run(st[2], w, env)
            if r:
                return r
    return None


# --------------------------------------------------------------- render
def render(w, msg=""):
    def wall(y, x, side):
        return side in w.walls.get((y, x), ())
    lines = []
    for y in range(w.h):
        top, mid = "", ""
        for x in range(w.w):
            top += "+" + ("---" if wall(y, x, "top") else "   ")
            mid += ("|" if wall(y, x, "left") else " ")
            c = (y, x)
            mid += (" @ " if c == w.pos else " E " if c == w.exit
                    else " . " if c in w.visited else "   ")
        lines += [top + "+", mid + ("|" if wall(y, w.w - 1, "right") else " ")]
    lines.append("".join("+" + ("---" if wall(w.h - 1, x, "bottom") else "   ")
                         for x in range(w.w)) + "+")
    print("\033[H\033[2J" + "\n".join(lines))
    print(f"\nSchritte: {w.steps}   {msg}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("program"); ap.add_argument("maze")
    ap.add_argument("--delay", type=float, default=0.15)
    ap.add_argument("--limit", type=int, default=20000)
    a = ap.parse_args()
    os.system("")  # enables ANSI escapes on Windows terminals

    try:
        prog, defs = parse(open(a.program, encoding="utf-8").read())
    except SyntaxError as e:
        sys.exit(f"Fehler: {e}")
    w = World(json.load(open(a.maze, encoding="utf-8")), a.limit)
    w.funcs = defs

    render(w, "Start")
    try:
        for _ in run(prog, w, {}):
            render(w); time.sleep(a.delay)
            if w.pos == w.exit:
                render(w, "Ziel erreicht!"); return
        render(w, "Programm zu Ende, aber das Ziel wurde nicht erreicht.")
    except Stop as e:
        render(w, str(e))


if __name__ == "__main__":
    main()
