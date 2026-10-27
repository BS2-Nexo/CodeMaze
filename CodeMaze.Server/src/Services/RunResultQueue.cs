using System.Threading.Channels;
using CodeMaze.MazeLang.Interpreter;
using CodeMaze.Server.Runs;

namespace CodeMaze.Server.Services;

/// <summary>Everything scoring needs, captured when the run ends.</summary>
public sealed record CompletedRun(PendingRun Run, RunStatistics Stats);

/// <summary>Hand-off between the websocket handler and the scoring worker.</summary>
public sealed class RunResultQueue
{
    private readonly Channel<CompletedRun> _channel =
        Channel.CreateUnbounded<CompletedRun>(new UnboundedChannelOptions { SingleReader = true });

    public bool TryEnqueue(CompletedRun run) => _channel.Writer.TryWrite(run);

    public IAsyncEnumerable<CompletedRun> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}