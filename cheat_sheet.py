#!/usr/bin/env python3
"""Terminal cheat sheet of the maze DSL (work in progress).

Usage: python syntax_sheet.py [--slow 0.05] [--lang de|en]
  --slow S   pause S seconds after every line (reveal effect for a talk)
Standard library only.
"""
import argparse, os, re, time

W = 76
R, B, DIM = "\033[0m", "\033[1m", "\033[2m"
CYAN, YEL, MAG, GRN, RED, GREY = (f"\033[{c}m" for c in (96, 93, 95, 92, 91, 90))

KEYWORDS = {"gehe", "wiederhole", "mal", "immer", "wenn", "sonst", "ende",
            "funktion", "ergebnis", "nicht", "und", "oder"}
DIRS = {"hoch", "runter", "links", "rechts", "rückwärts", "zurück"}
SENSORS = {"frei", "besucht"}

# (german, english) pairs for every text that is not code
T = {
    "title": ("Maze-DSL  -  Syntax-Übersicht", "Maze DSL  -  syntax overview"),
    "wip": ("Arbeitsstand, nicht final", "work in progress, not final"),
    "action": ("AKTION", "ACTION"), "loops": ("SCHLEIFEN", "LOOPS"),
    "cond": ("BEDINGUNG", "CONDITIONAL"), "dirs": ("RICHTUNGEN", "DIRECTIONS"),
    "sens": ("SENSOREN", "SENSORS"), "logic": ("LOGIK", "LOGIC"),
    "func": ("FUNKTIONEN", "FUNCTIONS"), "misc": ("SONSTIGES", "MISC"),
    "ex": ("BEISPIEL", "EXAMPLE"),
}

def tr(key, lang):
    return T[key][lang]

def sections(lang):
    d = lang == "de"
    return [
        ("action", [
            ("gehe <Richtung>", "eine Zelle in diese Richtung" if d else "one cell in that direction"),
            ("gehe rückwärts", "zur vorherigen Zelle (Verlauf)" if d else "to the previous cell (history)"),
        ]),
        ("loops", [
            ("wiederhole <Zahl> [mal] ... ende", "feste Anzahl" if d else "fixed count"),
            ("immer ... ende", "bis Ziel oder Absturz" if d else "until goal or crash"),
        ]),
        ("cond", [
            ("wenn <Sensor> ... ende", "nur wenn wahr" if d else "only if true"),
            ("sonst wenn <Sensor> ...", "weitere Prüfung" if d else "further check"),
            ("sonst ...", "alles andere" if d else "everything else"),
        ]),
        ("dirs", [
            ("hoch  runter  links  rechts", "absolut, immer gleich" if d else "absolute, never changes"),
            ("rückwärts  (zurück)", "nur nach 'gehe'" if d else "only after 'gehe'"),
        ]),
        ("sens", [
            ("frei <Richtung>", "keine Wand in Richtung" if d else "no wall that way"),
            ("besucht <Richtung>", "Zelle in Richtung schon besucht" if d else "cell that way already visited"),
        ]),
        ("logic", [
            ("nicht A   A und B   A oder B", "Vorrang: nicht > und > oder" if d else "precedence: nicht > und > oder"),
        ]),
        ("func", [
            ("funktion name(p1, p2) ... ende", "eigene Funktion, mehrere Parameter" if d else "own function, several parameters"),
            ("ergebnis <Wert>", "gibt Wert zurück, beendet Funktion" if d else "returns value, ends function"),
            ("name <Arg> <Arg> ...", "Aufruf ohne Klammern" if d else "call without parentheses"),
        ]),
        ("misc", [
            ("# Text", "Kommentar bis Zeilenende" if d else "comment to end of line"),
            ("Groß/Klein, ä/ae", "egal" if d else "ignored"),
        ]),
    ]

EXAMPLE = """funktion neu(r)
  ergebnis frei r und nicht besucht r
ende

immer
  wenn neu hoch
    gehe hoch
  sonst wenn neu rechts
    gehe rechts
  sonst
    gehe rückwärts
  ende
ende"""


def colorize(code):
    def c(m):
        s = m.group(0)
        if s.startswith("<"): return DIM + s + R
        if s.lower() in KEYWORDS: return B + CYAN + s + R
        if s.lower() in DIRS: return YEL + s + R
        if s.lower() in SENSORS: return MAG + s + R
        if s.startswith("#"): return GREY + s + R
        return s
    return re.sub(r"#.*|<[^>]+>|\w+", c, code)


class Out:
    def __init__(self, slow): self.slow = slow
    def line(self, s=""):
        print(s, flush=True)
        if self.slow: time.sleep(self.slow)

def pad(colored, plain, width):
    return colored + " " * max(0, width - len(plain))

def box_line(o, colored, plain):
    o.line(f"{GREY}│{R} {pad(colored, plain, W - 4)} {GREY}│{R}")

def header(o, text, color):
    o.line(f"{GREY}├─{R} {B}{color}{text}{R} {GREY}{'─' * (W - 5 - len(text))}┤{R}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slow", type=float, default=0.0)
    ap.add_argument("--lang", choices=("de", "en"), default="de")
    a = ap.parse_args()
    os.system("")  # ANSI on Windows
    lang, de = (0 if a.lang == "de" else 1), a.lang == "de"
    o = Out(a.slow)

    o.line(f"{GREY}┌{'─' * (W - 2)}┐{R}")
    t, wip = tr("title", lang), tr("wip", lang)
    box_line(o, f"{B}{t}{R}  {RED}[{wip}]{R}", f"{t}  [{wip}]")

    for key, rows in sections(a.lang):
        header(o, tr(key, lang), GRN)
        for code, desc in rows:
            plain = f"{code:<36}{desc}"
            colored = colorize(code) + " " * (36 - len(code)) + GREY + desc + R
            box_line(o, colored, plain)

    header(o, tr("ex", lang), GRN)
    for ln in EXAMPLE.splitlines():
        box_line(o, colorize(ln), ln)

    o.line(f"{GREY}└{'─' * (W - 2)}┘{R}")


if __name__ == "__main__":
    main()
