import type { Maze, Position, Wall } from "./types";

/** Stable key for a position, e.g. for a Set of visited cells. */
export const positionKey = (p: Position): string => `${p.y},${p.x}`;

/** grid[y][x] is the list of walls of that cell. Does not depend on the order of `maze.cells`. */
export function buildWallGrid(maze: Maze): Wall[][][] {
  const grid: Wall[][][] = Array.from({ length: maze.height }, () =>
    Array.from({ length: maze.width }, () => [] as Wall[]),
  );

  for (const cell of maze.cells) {
    const row = grid[cell.y];
    if (row && cell.x >= 0 && cell.x < maze.width) row[cell.x] = cell.walls;
  }

  return grid;
}