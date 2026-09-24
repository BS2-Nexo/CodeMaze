using System.Numerics;

namespace CodeMaze.MazeGeneration.Maze;

public static class WallExtensions
{
    extension(Walls self)
    {
        public int Count()
        {
            return BitOperations.PopCount((uint)self);
        }

        public Walls Open(Walls walls)
        {
            return self &= ~walls;
        }

        public Walls Close(Walls walls)
        {
            return self |= walls;
        }
    }
}