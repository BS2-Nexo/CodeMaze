namespace CodeMaze.MazeGeneration.Algorithms;

public sealed record MazeScoreInput(
    int Size,             // square maze: side length
    Difficulty Difficulty,
    long OptimalSteps,     // MazeView.ShortestSteps()
    long ActualSteps,      // steps counted by the run
    bool ExitReached);

public sealed record MazeScore(uint Points, double Efficiency, bool Perfect);

public static class MazeScorer
{
    private const int PointsPerSize = 10;
    private const double Floor = 0.1;            // share of base points for any finished maze
    private const double PerfectBonusShare = 0.25;
    private const int Rounding = 10;

    // Size scaling: small mazes are worth little, then each tier of TierWidth cells
    // adds FactorPerTier on top.
    // <10: 0.4 | 10-14: 1.0 | 15-19: 1.5 | 20-24: 2.0 | 25-29: 2.5 | ...
    private const int SmallMazeLimit = 10;
    private const double SmallMazeFactor = 0.4;
    private const int TierWidth = 5;
    private const double FactorPerTier = 0.5;

    private static double Multiplier(Difficulty d) => d switch
    {
        Difficulty.Easy => 1.0,
        Difficulty.Medium => 1.5,
        Difficulty.Hard => 2.5,
        _ => throw new ArgumentOutOfRangeException(nameof(d)),
    };

    private static double SizeFactor(int size) =>
        size < SmallMazeLimit ? SmallMazeFactor : FactorPerTier * (size / TierWidth);

    public static MazeScore Score(MazeScoreInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!input.ExitReached || input.Size <= 0 || input.ActualSteps <= 0 || input.OptimalSteps <= 0)
        {
            return new MazeScore(0u, 0, false);
        }

        var efficiency = Math.Clamp((double)input.OptimalSteps / input.ActualSteps, 0.0, 1.0);
        var perfect = input.ActualSteps == input.OptimalSteps;

        var maxPoints = input.Size * PointsPerSize * SizeFactor(input.Size) * Multiplier(input.Difficulty);
        var curve = Floor + (1 - Floor) * efficiency * efficiency;

        var raw = maxPoints * curve + (perfect ? maxPoints * PerfectBonusShare : 0);
        var rounded = Math.Round(raw / Rounding, MidpointRounding.AwayFromZero) * Rounding;

        // Clamp so the cast to uint can never overflow.
        var points = (uint)Math.Clamp(rounded, 0.0, uint.MaxValue);

        return new MazeScore(points, efficiency, perfect);
    }
}