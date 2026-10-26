using CodeMaze.MazeGeneration;
using CodeMaze.MazeGeneration.Abstractions;

namespace CodeMaze.MazeGeneration.Algorithms.RecursiveDivision;

/// <summary>
/// Recursive division. Starts with an open room (only the outer boundary has walls), then
/// repeatedly splits a chamber with a wall that has one random gap, and recurses into both halves.
/// Produces long straight walls. Unlike the other algorithms this one ADDS walls, so it expects a
/// draft created with <see cref="Walls.None"/> as the initial layout. Implemented with an explicit
/// stack, so large mazes cannot overflow the call stack.
/// </summary>
public sealed class RecursiveDivision : IGeneratorAlgorithm
{
    private readonly Random _pseudoRandomNumbers;

    public RecursiveDivision()
    {
        _pseudoRandomNumbers = new Random();
    }

    public RecursiveDivision(Random rngesus)
    {
        _pseudoRandomNumbers = rngesus;
    }

    public DraftMaze Generate(int height, int width)
    {
        var draft = new DraftMaze(height, width, Walls.None);
        return Generate(draft);
    }

    public DraftMaze Generate(DraftMaze draft)
    {
        var chambers = new Stack<(int X, int Y, int Width, int Height)>();
        chambers.Push((0, 0, draft.Width, draft.Height));

        while (chambers.Count > 0)
        {
            var (x, y, w, h) = chambers.Pop();

            if (w < 2 || h < 2)
            {
                continue;
            }

            var horizontal = w == h ? _pseudoRandomNumbers.Next(2) == 0 : w < h;

            if (horizontal)
            {
                // Wall runs between row wallY and wallY + 1.
                var wallY = y + _pseudoRandomNumbers.Next(h - 1);
                var gapX = x + _pseudoRandomNumbers.Next(w);

                for (var cx = x; cx < x + w; cx++)
                {
                    if (cx == gapX)
                    {
                        continue;
                    }

                    draft[At(wallY, cx)] |= Walls.Bottom;
                    draft[At(wallY + 1, cx)] |= Walls.Top;
                }

                chambers.Push((x, y, w, wallY - y + 1));
                chambers.Push((x, wallY + 1, w, y + h - 1 - wallY));
            }
            else
            {
                // Wall runs between column wallX and wallX + 1.
                var wallX = x + _pseudoRandomNumbers.Next(w - 1);
                var gapY = y + _pseudoRandomNumbers.Next(h);

                for (var cy = y; cy < y + h; cy++)
                {
                    if (cy == gapY)
                    {
                        continue;
                    }

                    draft[At(cy, wallX)] |= Walls.Right;
                    draft[At(cy, wallX + 1)] |= Walls.Left;
                }

                chambers.Push((x, y, wallX - x + 1, h));
                chambers.Push((wallX + 1, y, x + w - 1 - wallX, h));
            }
        }

        draft.PlaceRandomEntranceAndExit(_pseudoRandomNumbers);

        return draft;
    }

    private static CellPosition At(int y, int x) => new(new YCoordinate(y), new XCoordinate(x));
}