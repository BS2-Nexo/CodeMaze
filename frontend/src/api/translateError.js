/**
 * Translates the compiler's English diagnostics into German for display.
 * The server and the compiler keep their English messages; unknown messages are returned unchanged.
 * Text the player wrote (names, characters, keywords) is carried over as it is.
 */

const KIND = { direction: "Richtung", number: "Zahl" };

/** What the parser can expect, as it appears in "Expected <what> but found ...". */
const WHAT = {
    "a function name": "Funktionsname",
    "a parameter name": "Parametername",
    "a condition": "Bedingung",
    "a number": "Zahl",
    "a direction": "Richtung",
};

/** "'ende' erwartet, aber das Programmende wurde erreicht." / "..., aber 'x' gefunden." */
const but = (found) =>
    found === "end of program" ? "aber das Programmende wurde erreicht" : `aber ${found} gefunden`;

const plural = (n, one, many) => (Number(n) === 1 ? one : many);

const RULES = [
    // ---- lexer
    [/^The number '(.+)' is too large\.$/, (n) => `Die Zahl '${n}' ist zu groß.`],
    [/^Unexpected character '(.+)'\.$/, (c) => `Unerwartetes Zeichen '${c}'.`],

    // ---- compiler: specific "Expected ..." messages first
    [/^Expected a number but found a direction\.$/, () => "Zahl erwartet, aber Richtung gefunden."],
    [/^Expected a direction but found a number\.$/, () => "Richtung erwartet, aber Zahl gefunden."],

    // ---- parser: all other "Expected X but found Y."
    [
        /^Expected (.+) but found (.+)\.$/,
        (what, found) => `${WHAT[what] ?? what} erwartet, ${but(found)}.`,
    ],
    [/^Functions can only be declared at the top level\.$/, () => "Funktionen dürfen nur auf der obersten Ebene stehen."],
    [/^Unexpected end of program\.$/, () => "Unerwartetes Programmende."],
    [/^Unexpected '(.+)'\.$/, (t) => `'${t}' ist hier nicht erlaubt.`],

    // ---- compiler
    [/^The function '(.+)' is defined more than once\.$/, (n) => `Die Funktion '${n}' ist mehrfach definiert.`],
    [
        /^The function '(.+)' returns a value on some paths but can also end without one\.$/,
        (n) => `Die Funktion '${n}' gibt auf manchen Wegen einen Wert zurück, kann aber auch ohne Wert enden.`,
    ],
    [
        /^The parameter '(.+)' appears more than once in '(.+)'\.$/,
        (p, f) => `Der Parameter '${p}' kommt in '${f}' mehrmals vor.`,
    ],
    [/^The main program cannot return a value\.$/, () => "Das Hauptprogramm kann keinen Wert zurückgeben."],
    [/^'Back' is only allowed as the target of a move\.$/, () => "„zurück“ ist nur als Ziel einer Bewegung erlaubt."],
    [
        /^The parameter '(.+)' is used as a (direction|number) and as a (direction|number)\.$/,
        (p, a, b) => `Der Parameter '${p}' wird als ${KIND[a]} und als ${KIND[b]} verwendet.`,
    ],
    [/^The parameter '(.+)' is not known here\.$/, (p) => `Der Parameter '${p}' ist hier nicht bekannt.`],
    [/^The function '(.+)' is not defined\.$/, (n) => `Die Funktion '${n}' ist nicht definiert.`],
    [
        /^'(.+)' expects (\d+) argument\(s\) but got (\d+)\.$/,
        (n, want, got) =>
            `'${n}' erwartet ${want} ${plural(want, "Argument", "Argumente")}, ` +
            `aber es ${plural(got, "wurde", "wurden")} ${got} übergeben.`,
    ],
    [
        /^'(.+)' does not return a value and cannot be used as a condition here\.$/,
        (n) => `'${n}' gibt keinen Wert zurück und kann hier nicht als Bedingung verwendet werden.`,
    ],
];

export function translateError(message) {
    if (typeof message !== "string") return message;
    for (const [pattern, build] of RULES) {
        const m = pattern.exec(message);
        if (m) return build(...m.slice(1));
    }
    return message;
}