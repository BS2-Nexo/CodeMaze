#!/usr/bin/env python3
"""Prototype: run a German maze DSL program on a maze JSON and animate it.

Usage: python maze_proto.py programm.txt maze.json [--delay 0.15] [--limit 20000]
Standard library only.
"""
import argparse, json, os, re, sys, time

# direction word -> (dy, dx, wall name in the JSON)
SIDES = {"hoch": (-1, 0, "top"), "runter": (1, 0, "bottom"),
         "links": (0, -1, "left"), "rechts": (0, 1, "right")}
BACK = {"rueckwaerts", "zurueck"}  # after umlaut normalisation


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
        while self.peek() not in stops:
            out.append(self.stmt())
        return out

    def stmt(self):
        t, n = self.next()
        if t == "gehe":
            d = self.expect(*SIDES, *BACK)
            if d in BACK:
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
                d = self.expect(*SIDES)
                cond = (self.expect("frei", "neu"), d)
                branches.append((cond, self.block({"sonst", "ende"})))
                if self.peek() == "sonst":
                    self.next()
                    if self.peek() == "wenn":
                        self.next(); continue
                    other = self.block({"ende"})
                self.expect("ende"); break
            return ("if", branches, other)
        raise SyntaxError(f"Zeile {n}: Das Wort '{t}' kenne ich nicht.")


def parse(src):
    p = Parser(tokenize(src))
    prog = p.block({None})
    return prog


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

    def tick(self):
        self.budget -= 1
        if self.budget < 0:
            raise Stop("Zu viele Befehle. Läuft dein Programm im Kreis?")

    def target(self, d):
        dy, dx, side = SIDES[d]
        nxt = (self.pos[0] + dy, self.pos[1] + dx)
        return nxt if side not in self.walls[self.pos] and nxt in self.walls else None

    def sense(self, kind, d):
        nxt = self.target(d)
        return nxt is not None and (kind == "frei" or nxt not in self.visited)

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


def run(block, w):
    """Generator: yields once after every successful move."""
    for st in block:
        w.tick()
        if st[0] == "go":
            for _ in range(st[2]):
                w.go(st[1]); yield
        elif st[0] == "back":
            w.back(); yield
        elif st[0] == "rep":
            i = 0
            while st[1] is None or i < st[1]:
                w.tick()
                yield from run(st[2], w)
                i += 1
        else:  # if
            for cond, body in st[1]:
                if w.sense(*cond):
                    yield from run(body, w); break
            else:
                yield from run(st[2], w)


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
        prog = parse(open(a.program, encoding="utf-8").read())
    except SyntaxError as e:
        sys.exit(f"Fehler: {e}")
    w = World(json.load(open(a.maze, encoding="utf-8")), a.limit)

    render(w, "Start")
    try:
        for _ in run(prog, w):
            render(w); time.sleep(a.delay)
            if w.pos == w.exit:
                render(w, "Ziel erreicht!"); return
        render(w, "Programm zu Ende, aber das Ziel wurde nicht erreicht.")
    except Stop as e:
        render(w, str(e))


if __name__ == "__main__":
    main()
