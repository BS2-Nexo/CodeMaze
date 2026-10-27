using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeMaze.MazeGeneration.Algorithms;
using CodeMaze.MazeLang.Interpreter;
using CodeMaze.Server.Services;

namespace CodeMaze.Server.Runs;

/// <summary>
/// Drives one MazeInterpreter run over one WebSocket.
///
/// Server -> client: every InterpreterEvent as a JSON text message (discriminated by "type":
///   started, moved, moveBlocked, exitReached, finished). The last message of a run is always
///   exactly one "finished", including when the run was cancelled.
/// Client -> server:
///   { "type": "cancel" }   (closing the socket also cancels)
///
/// Only the main flow ever sends, and only the receive loop ever receives,
/// so there is never more than one concurrent send or receive.
/// </summary>
public sealed class RunSession
{
    private const int MaxClientMessageBytes = 4 * 1024;
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase), // Direction, RunOutcome, BlockReason
        },
    };

    private readonly InterpreterOptions _options;
    private readonly ILogger<RunSession> _logger;
    private readonly RunResultQueue _queue;

    public RunSession(InterpreterOptions options, ILogger<RunSession> logger, RunResultQueue results)
    {
        _options = options;
        _logger = logger;
        _queue = results;
    }

    public async Task RunAsync(WebSocket socket, PendingRun run, CancellationToken requestAborted)
    {
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        var receiveTask = ReceiveLoopAsync(socket, runCts, requestAborted);
        var interpreter = new MazeInterpreter(run.Program, run.Maze, _options);

        try
        {
            await foreach (var evt in interpreter.RunAsync(run.StepDelay, runCts.Token))
            {
                await SendAsync(socket, evt, requestAborted);
            }
        }
        catch (OperationCanceledException)
        {
            runCts.TryCancel();
        }
        catch (WebSocketException ex)
        {
            _logger.LogInformation(ex, "Run {RunId}: socket failed.", run.Id);
            runCts.TryCancel();
        }
        catch (Exception ex)
        {
            // Not a normal outcome: the interpreter turns its own failures into Finished events.
            _logger.LogError(ex, "Run {RunId}: unexpected failure.", run.Id);
            runCts.TryCancel();

            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                var fault = new Finished(
                    RunOutcome.Fault, "Internal error.",
                    interpreter.State.Position, interpreter.State.Steps, interpreter.InstructionsExecuted);

                try { await SendAsync(socket, fault, CancellationToken.None); }
                catch (WebSocketException) { /* client already gone */ }
            }
        }
        finally
        {
            if (interpreter.TryGetRunStatistics(out var stats))
            {
                _queue.TryEnqueue(new CompletedRun(run, stats));
            }

            await CloseAsync(socket, receiveTask);
        }
    }

    private static async Task SendAsync(WebSocket socket, InterpreterEvent evt, CancellationToken ct)
    {
        // Serialize as the base type, otherwise the "type" discriminator is not written.
        var bytes = JsonSerializer.SerializeToUtf8Bytes<InterpreterEvent>(evt, JsonOptions);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    private static async Task ReceiveLoopAsync(WebSocket socket, CancellationTokenSource runCts, CancellationToken ct)
    {
        var buffer = new byte[1024];

        try
        {
            while (socket.State == WebSocketState.Open)
            {
                using var message = new MemoryStream();
                ValueWebSocketReceiveResult result;

                do
                {
                    result = await socket.ReceiveAsync(buffer.AsMemory(), ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        runCts.TryCancel();
                        return;
                    }

                    message.Write(buffer, 0, result.Count);

                    if (message.Length > MaxClientMessageBytes)
                    {
                        runCts.TryCancel();
                        return;
                    }
                }
                while (!result.EndOfMessage);

                if (IsCancel(message.GetBuffer().AsMemory(0, (int)message.Length)))
                    runCts.TryCancel();
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { runCts.TryCancel(); }
    }

    private static bool IsCancel(ReadOnlyMemory<byte> payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("type", out var type)
                && type.ValueEquals("cancel");
        }
        catch (JsonException)
        {
            return false; // ignore malformed messages
        }
    }

    private static async Task CloseAsync(WebSocket socket, Task receiveTask)
    {
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);

            // Give the client a moment to complete the close handshake.
            await receiveTask.WaitAsync(CloseTimeout);
        }
        catch (TimeoutException)
        {
            socket.Abort();
        }
        catch (WebSocketException) { }
    }
}

internal static class CancellationTokenSourceExtensions
{
    public static void TryCancel(this CancellationTokenSource cts)
    {
        try { cts.Cancel(); }
        catch (ObjectDisposedException) { }
    }
}