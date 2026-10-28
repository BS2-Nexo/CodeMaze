export const PREFERRED_MIN = 3; // start of the ladder, clamped to what the server allows
export const SMALL_CAP = 20; // the "small" part of the ladder goes up to this size
export const SMALL_STEPS = 40; // sizes in the small part (before duplicates are dropped)
export const LARGE_STEPS = 13; // sizes from SMALL_CAP up to the maximum (before duplicates are dropped)

/** `steps` sizes from `from` to `to`, both included, spaced geometrically (many small steps, then bigger ones). */
function geometric(from, to, steps) {
    const out = [];
    for (let i = 0; i < steps; i++) out.push(Math.round(from * Math.pow(to / from, i / (steps - 1))));
    return out;
}

/**
 * A fine ladder through the small sizes, then a coarser one up to the server's maximum.
 * No randomness: the same maximum always gives the same sizes.
 * e.g. min 3, max 100 -> 3, 4, 5, ..., 20, 23, 26, 30, 34, 39, 45, 51, 58, 67, 76, 87, 100 (30 sizes)
 */
export function gauntletSizes(minSize, maxSize, smallSteps = SMALL_STEPS, largeSteps = LARGE_STEPS, cap = SMALL_CAP) {
    const min = Math.min(Math.max(PREFERRED_MIN, minSize), maxSize);
    if (maxSize <= min) return [maxSize];
    const top = Math.min(cap, maxSize);
    const sizes = new Set(geometric(min, top, smallSteps));
    if (maxSize > top) geometric(top, maxSize, largeSteps).forEach((n) => sizes.add(n));
    return [...sizes].sort((a, b) => a - b);
}

/**
 * Lowest difficulty first; within a difficulty, sizes ascending, then the next difficulty.
 * `allDifficulties` is the server order (low -> high), `selected` the user's choice.
 */
export function buildGauntlet(allDifficulties, selected, minSize, maxSize) {
    const difficulties = allDifficulties.filter((d) => selected.includes(d));
    const sizes = gauntletSizes(minSize, maxSize);
    return difficulties.flatMap((difficulty) => sizes.map((size) => ({ difficulty, size })));
}