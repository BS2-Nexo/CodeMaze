using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// Exercises a running CodeMaze.Server end to end:
// capabilities -> maze -> POST /api/runs -> WebSocket -> events shown in a browser and logged (every odd run is cancelled).
internal static class Program
{
    private const int CancelAfterMoves = 3;

    private static async Task<int> Main(string[] args)
    {
        var opts = Options.Parse(args, out var error);
        if (opts is null)
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine(Options.Usage);
            return 2;
        }

        string code;
        try { code = File.ReadAllText(opts.ProgramPath); }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Cannot read program file '{opts.ProgramPath}': {e.Message}");
            return 2;
        }

        string? fixedMazeJson = null;
        if (opts.MazePath is not null)
        {
            try
            {
                fixedMazeJson = File.ReadAllText(opts.MazePath);
                MazeView.Parse(fixedMazeJson); // only to fail early on a broken file
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Cannot use maze file '{opts.MazePath}': {e.Message}");
                return 2;
            }
        }

        using var http = new HttpClient { BaseAddress = opts.Server, Timeout = TimeSpan.FromSeconds(30) };

        Caps caps;
        try { caps = await Caps.Fetch(http); }
        catch (Exception e)
        {
            Console.Error.WriteLine($"GET /api/capabilities failed: {e.Message}");
            return 1;
        }

        var language = opts.Language ?? "de";
        if (!caps.Languages.Contains(language, StringComparer.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                $"The server does not list the language '{language}' (it lists: {string.Join(", ", caps.Languages)}).");
            return 1;
        }

        Console.WriteLine(
            $"Capabilities: languages [{string.Join(", ", caps.Languages)}], size {caps.MinSize}-{caps.MaxSize}, " +
            $"step delay {caps.MinDelay}-{caps.MaxDelay} ms. Using language '{language}'.");

        var rnd = new Random();
        var viewer = WebViewer.TryStart(opts);
        var reports = new List<RunReport>();

        for (var n = 1; n <= opts.Runs; n++)
        {
            reports.Add(await ExecuteRun(n, opts, caps, language, code, fixedMazeJson, http, rnd, viewer));
            await Task.Delay(500);
        }

        var exitCode = PrintSummary(reports);
        viewer?.WaitForUser();
        return exitCode;
    }

    // ------------------------------------------------------------------ one run

    private static async Task<RunReport> ExecuteRun(
        int n, Options opts, Caps caps, string language, string code,
        string? fixedMazeJson, HttpClient http, Random rnd, WebViewer? viewer)
    {
        var report = new RunReport(n, cancelRun: n % 2 == 1);

        var mazeJson = fixedMazeJson ?? await RequestMaze(http, caps, opts, rnd, report);
        if (mazeJson is null)
        {
            report.Violations.Add("Could not obtain a maze from GET /api/maze.");
            return report;
        }

        MazeView sent;
        try { sent = MazeView.Parse(mazeJson); }
        catch (Exception e)
        {
            report.Violations.Add($"GET /api/maze returned a maze that cannot be parsed: {e.Message}");
            return report;
        }

        var request = new JsonObject
        {
            ["code"] = code,
            ["language"] = language,
            ["maze"] = JsonNode.Parse(mazeJson),
            ["stepDelayMs"] = Math.Clamp(opts.DelayMs, caps.MinDelay, caps.MaxDelay),
        };

        string body;
        int status;
        try
        {
            using var resp = await http.PostAsync(
                "/api/runs", new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json"));
            status = (int)resp.StatusCode;
            body = await resp.Content.ReadAsStringAsync();
        }
        catch (Exception e)
        {
            report.Violations.Add($"POST /api/runs failed: {e.Message}");
            return report;
        }

        if (status != 200)
        {
            var file = ErrorFile.Write(n, status, body);
            report.Result = $"rejected with HTTP {status}, details in {file}";
            Console.WriteLine($"Run {n}: {report.Result}");
            if (status != 400)
            {
                report.Violations.Add($"POST /api/runs answered HTTP {status}, expected 200 or 400.");
            }
            return report;
        }

        MazeView returned;
        string webSocketPath;
        try
        {
            using var doc = JsonDocument.Parse(body);
            webSocketPath = doc.RootElement.GetProperty("webSocketPath").GetString()!;
            returned = MazeView.Parse(doc.RootElement.GetProperty("maze"));
        }
        catch (Exception e)
        {
            report.Violations.Add($"POST /api/runs response is malformed: {e.Message}");
            return report;
        }

        if (!returned.SameAs(sent))
        {
            report.Violations.Add("The maze in the POST /api/runs response differs from the maze that was submitted.");
        }

        var wsUri = new UriBuilder(opts.Server)
        {
            Scheme = opts.Server.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
            Path = webSocketPath,
        }.Uri;

        await Stream(wsUri, returned, report, viewer, opts.Runs);
        return report;
    }

    private static async Task<string?> RequestMaze(
        HttpClient http, Caps caps, Options opts, Random rnd, RunReport report)
    {
        var max = Math.Min(caps.MaxSize, opts.MaxSize ?? caps.MaxSize);
        if (max < caps.MinSize) max = caps.MinSize;

        // A size inside the advertised range can still be a 404 when no generator is configured for it.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var size = rnd.Next(caps.MinSize, max + 1);
            try
            {
                using var resp = await http.GetAsync($"/api/maze?size={size}");
                var text = await resp.Content.ReadAsStringAsync();
                if (resp.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Run {report.Number}: got a {size}x{size} maze.");
                    return text;
                }

                report.Notes.Add($"GET /api/maze?size={size} answered HTTP {(int)resp.StatusCode}: {Trim(text)}");
            }
            catch (Exception e)
            {
                report.Notes.Add($"GET /api/maze?size={size} failed: {e.Message}");
            }
        }

        return null;
    }

    // ----------------------------------------------------------------- websocket

    private static async Task Stream(Uri wsUri, MazeView maze, RunReport report, WebViewer? viewer, int totalRuns)
    {
        var session = new RunSession(maze, report);
        using var ws = new ClientWebSocket();
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var cancelSent = false;

        try
        {
            await ws.ConnectAsync(wsUri, cts.Token);
            viewer?.ShowMaze(maze, report, totalRuns);
            viewer?.ShowState(session);

            while (true)
            {
                var message = await ReceiveText(ws, cts.Token);
                if (message is null) break;
                Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [run {report.Number}] <- {message}");

                try
                {
                    using var doc = JsonDocument.Parse(message);
                    session.Handle(doc.RootElement);
                }
                catch (Exception e) when (e is JsonException or KeyNotFoundException
                                              or InvalidOperationException or FormatException)
                {
                    report.Violations.Add($"Malformed event ({e.Message}): {Trim(message)}");
                }

                viewer?.ShowState(session);

                if (report.CancelRun && !cancelSent && !session.Finished && session.Steps >= CancelAfterMoves)
                {
                    var bytes = Encoding.UTF8.GetBytes("{\"type\":\"cancel\"}");
                    await ws.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token);
                    cancelSent = true;
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [run {report.Number}] -> {{\"type\":\"cancel\"}}");
                }
            }

            if (ws.State == WebSocketState.CloseReceived)
            {
                await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", cts.Token);
            }

            if (ws.CloseStatus is not null && ws.CloseStatus != WebSocketCloseStatus.NormalClosure)
            {
                report.Violations.Add($"The socket was closed with {ws.CloseStatus}, expected a normal closure.");
            }
        }
        catch (OperationCanceledException)
        {
            report.Violations.Add("Timed out waiting for the run to finish.");
        }
        catch (WebSocketException e)
        {
            report.Violations.Add($"WebSocket error: {e.Message}");
        }

        if (!session.Finished)
        {
            report.Violations.Add("The socket ended without a 'finished' event.");
        }

        report.Result = session.Outcome ?? "no outcome";

        if (report.CancelRun)
        {
            if (!cancelSent)
            {
                report.Notes.Add($"Cancel was not exercised: the run ended after {session.Steps} step(s).");
            }
            else if (session.Outcome != "cancelled")
            {
                report.Notes.Add(
                    $"Cancel was sent but the run ended with '{session.Outcome}'. " +
                    "Possible if the program ended first; otherwise the server ignored the cancel.");
            }
        }
    }

    private static async Task<string?> ReceiveText(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();
        WebSocketReceiveResult r;
        do
        {
            r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (r.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, r.Count);
        } while (!r.EndOfMessage);

        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    // ------------------------------------------------------------------- summary

    private static int PrintSummary(List<RunReport> reports)
    {
        Console.WriteLine();
        Console.WriteLine("=== Summary ===");
        foreach (var r in reports)
        {
            var verdict = r.Violations.Count == 0 ? "OK  " : "FAIL";
            Console.WriteLine($"{verdict} run {r.Number} [{r.Kind}] {r.Result}");
            foreach (var v in r.Violations) Console.WriteLine($"       violation: {v}");
            foreach (var note in r.Notes) Console.WriteLine($"       note: {note}");
        }

        var failed = reports.Count(r => r.Violations.Count > 0);
        Console.WriteLine();
        Console.WriteLine($"{reports.Count} run(s), {failed} with violations.");
        Console.WriteLine($"Error files from rejected runs: *{ErrorFile.Extension} (remove with: rm *{ErrorFile.Extension})");
        return failed > 0 ? 1 : 0;
    }

    internal static string Trim(string s) => s.Length <= 200 ? s : s[..200] + "...";
}

// ---------------------------------------------------------------------- options

internal sealed record Options(
    string ProgramPath, string? MazePath, Uri Server, int Runs, int DelayMs, int? MaxSize, string? Language,
    int WebPort, bool OpenBrowser)
{
    public const string Usage = """
        Usage: CodeMaze.Validator <program-file> [options]

          <program-file>        text file with the DSL program (required)
          --maze <file>         JSON maze to use for every run (default: ask the server for a random one)
          --server <url>        server base address (default: http://localhost:5000)
          --runs <n>            number of runs, every odd run is cancelled (default: 4)
          --delay <ms>          stepDelayMs sent with each run (default: 50)
          --max-size <n>        upper bound for the random maze size (default: the server's maximum)
          --language <name>     language to send (default: de)
          --web-port <n>        port of the web view (default: a free port)
          --no-browser          do not open the browser automatically, only print the address

        The maze is shown in a browser. Every received event is also logged to the console.
        """;

    public static Options? Parse(string[] args, out string? error)
    {
        error = null;
        string? program = null, maze = null, language = null;
        var server = new Uri("http://localhost:5000");
        var runs = 4;
        var delay = 50;
        int? maxSize = null;
        var webPort = 0;
        var openBrowser = true;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "--help" or "-h")
            {
                error = "";
                return null;
            }

            if (a == "--no-browser") { openBrowser = false; continue; }

            if (!a.StartsWith("--"))
            {
                if (program is not null)
                {
                    error = $"Unexpected argument '{a}'.";
                    return null;
                }
                program = a;
                continue;
            }

            if (i + 1 >= args.Length)
            {
                error = $"Option {a} needs a value.";
                return null;
            }

            var value = args[++i];
            switch (a)
            {
                case "--maze": maze = value; break;
                case "--language": language = value; break;
                case "--server":
                    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                        || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    {
                        error = "--server must be an http(s) URL.";
                        return null;
                    }
                    server = uri;
                    break;
                case "--runs":
                    if (!int.TryParse(value, out runs) || runs < 1) { error = "--runs must be at least 1."; return null; }
                    break;
                case "--delay":
                    if (!int.TryParse(value, out delay) || delay < 0) { error = "--delay must be 0 or more."; return null; }
                    break;
                case "--max-size":
                    if (!int.TryParse(value, out var ms) || ms < 1) { error = "--max-size must be at least 1."; return null; }
                    maxSize = ms;
                    break;
                case "--web-port":
                    if (!int.TryParse(value, out webPort) || webPort < 0 || webPort > 65535)
                    {
                        error = "--web-port must be between 0 and 65535.";
                        return null;
                    }
                    break;
                default:
                    error = $"Unknown option {a}.";
                    return null;
            }
        }

        if (program is null)
        {
            error = "The program file is required.";
            return null;
        }

        return new Options(program, maze, server, runs, delay, maxSize, language, webPort, openBrowser);
    }
}

// ----------------------------------------------------------------- capabilities

internal sealed record Caps(List<string> Languages, int MinSize, int MaxSize, int MinDelay, int MaxDelay)
{
    public static async Task<Caps> Fetch(HttpClient http)
    {
        using var resp = await http.GetAsync("/api/capabilities");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var maze = root.GetProperty("maze");
        var run = root.GetProperty("run");

        return new Caps(
            root.GetProperty("languages").EnumerateArray().Select(l => l.GetProperty("name").GetString()!).ToList(),
            maze.GetProperty("minSize").GetInt32(),
            maze.GetProperty("maxSize").GetInt32(),
            run.GetProperty("minStepDelayMs").GetInt32(),
            run.GetProperty("maxStepDelayMs").GetInt32());
    }
}

// --------------------------------------------------------------- reports, files

internal sealed class RunReport(int number, bool cancelRun)
{
    public int Number { get; } = number;
    public bool CancelRun { get; } = cancelRun;
    public string Kind => CancelRun ? "cancel" : "normal";
    public string Result { get; set; } = "not started";
    public List<string> Violations { get; } = new();
    public List<string> Notes { get; } = new();
}

internal static class ErrorFile
{
    // Unusual on purpose, so the files are easy to glob and delete.
    public const string Extension = ".asdfghjkl";

    public static string Write(int run, int status, string body)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"run {run}, HTTP {status}, {DateTime.Now:O}");
        sb.AppendLine();

        var formatted = false;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in errors.EnumerateArray())
                {
                    var source = e.TryGetProperty("source", out var s) ? s.GetString() : "?";
                    var message = e.TryGetProperty("message", out var m) ? m.GetString() : "";
                    var at = "";
                    if (e.TryGetProperty("line", out var line) && line.ValueKind == JsonValueKind.Number)
                    {
                        var col = e.TryGetProperty("column", out var c) && c.ValueKind == JsonValueKind.Number
                            ? c.GetInt32().ToString() : "?";
                        at = $"{line.GetInt32()}:{col} ";
                    }
                    sb.AppendLine($"[{source}] {at}{message}");
                }
                formatted = true;
            }
        }
        catch (JsonException)
        {
            // plain-text body, written as is below
        }

        if (!formatted) sb.AppendLine(body);

        var name = $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-run{run}{Extension}";
        File.WriteAllText(name, sb.ToString());
        return Path.GetFullPath(name);
    }
}

// ------------------------------------------------------------------------- maze

internal sealed record Pos(int Y, int X);

internal sealed class MazeView
{
    public const byte Top = 1, Right = 2, Bottom = 4, Left = 8;

    private readonly byte[] _walls;

    public int Width { get; }
    public int Height { get; }
    public Pos Entrance { get; }
    public Pos Exit { get; }
    public byte[] WallBytes => _walls;

    private MazeView(int width, int height, Pos entrance, Pos exit, byte[] walls)
    {
        Width = width;
        Height = height;
        Entrance = entrance;
        Exit = exit;
        _walls = walls;
    }

    public static Pos ReadPos(JsonElement e) => new(e.GetProperty("y").GetInt32(), e.GetProperty("x").GetInt32());

    public static MazeView Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Parse(doc.RootElement);
    }

    public static MazeView Parse(JsonElement root)
    {
        var width = root.GetProperty("width").GetInt32();
        var height = root.GetProperty("height").GetInt32();
        if (width <= 0 || height <= 0) throw new FormatException("width and height must be positive");

        var walls = new byte[width * height];
        foreach (var c in root.GetProperty("cells").EnumerateArray())
        {
            var y = c.GetProperty("y").GetInt32();
            var x = c.GetProperty("x").GetInt32();
            if (y < 0 || y >= height || x < 0 || x >= width)
            {
                throw new FormatException($"cell ({y}, {x}) lies outside the maze");
            }

            byte flags = 0;
            foreach (var w in c.GetProperty("walls").EnumerateArray())
            {
                flags |= w.GetString()?.ToLowerInvariant() switch
                {
                    "top" => Top,
                    "right" => Right,
                    "bottom" => Bottom,
                    "left" => Left,
                    var other => throw new FormatException($"unknown wall '{other}'"),
                };
            }
            walls[y * width + x] = flags;
        }

        return new MazeView(
            width, height, ReadPos(root.GetProperty("entrance")), ReadPos(root.GetProperty("exit")), walls);
    }

    public bool Contains(Pos p) => p.Y >= 0 && p.Y < Height && p.X >= 0 && p.X < Width;

    /// <summary>True if the cell has a wall on that side. False for cells outside the maze.</summary>
    public bool Has(Pos p, byte side) => Contains(p) && (_walls[p.Y * Width + p.X] & side) != 0;

    public bool SameAs(MazeView o) =>
        Width == o.Width && Height == o.Height && Entrance == o.Entrance && Exit == o.Exit
        && _walls.AsSpan().SequenceEqual(o._walls);
}

// ---------------------------------------------------------------- event checking

/// <summary>Tracks one run's events and records every deviation from the documented protocol.</summary>
internal sealed class RunSession(MazeView maze, RunReport report)
{
    private static readonly Dictionary<string, (int Dy, int Dx, byte Side)> Deltas = new()
    {
        ["up"] = (-1, 0, MazeView.Top),
        ["down"] = (1, 0, MazeView.Bottom),
        ["left"] = (0, -1, MazeView.Left),
        ["right"] = (0, 1, MazeView.Right),
    };

    private static readonly HashSet<string> Outcomes =
        ["exitReached", "programEnded", "crashed", "cancelled", "limitExceeded", "fault"];

    private readonly Stack<Pos> _history = new();
    private int _events;
    private bool _started;
    private bool _exitSeen;

    public Pos? Current { get; private set; }
    public int Steps { get; private set; }
    public bool Finished { get; private set; }
    public string? Outcome { get; private set; }
    public string LastEvent { get; private set; } = "connecting";

    private void Fail(string message)
    {
        if (report.Violations.Count < 20) report.Violations.Add($"event {_events}: {message}");
    }

    public void Handle(JsonElement ev)
    {
        _events++;
        var type = ev.GetProperty("type").GetString() ?? "";

        if (Finished) Fail($"'{type}' arrived after 'finished'.");
        if (type != "started" && !_started) Fail($"expected 'started' first but got '{type}'.");

        switch (type)
        {
            case "started": OnStarted(ev); break;
            case "moved": OnMoved(ev); break;
            case "moveBlocked": OnBlocked(ev); break;
            case "exitReached": OnExitReached(ev); break;
            case "finished": OnFinished(ev); break;
            default: Fail($"unknown event type '{type}'."); break;
        }
    }

    private void OnStarted(JsonElement ev)
    {
        if (_started) Fail("'started' was sent twice.");
        _started = true;

        var position = MazeView.ReadPos(ev.GetProperty("position"));
        var exit = MazeView.ReadPos(ev.GetProperty("exit"));
        if (position != maze.Entrance) Fail($"started at {position} but the maze entrance is {maze.Entrance}.");
        if (exit != maze.Exit) Fail($"started reports exit {exit} but the maze exit is {maze.Exit}.");

        Current = position;
        LastEvent = "started";
    }

    private void OnMoved(JsonElement ev)
    {
        var from = MazeView.ReadPos(ev.GetProperty("from"));
        var to = MazeView.ReadPos(ev.GetProperty("to"));
        var direction = ev.GetProperty("direction").GetString() ?? "";
        var step = ev.GetProperty("step").GetInt32();

        if (from != Current) Fail($"moved from {from} but the character was at {Current}.");
        if (step != Steps + 1) Fail($"step is {step}, expected {Steps + 1}.");
        if (!maze.Contains(to)) Fail($"moved outside the maze to {to}.");

        if (direction == "back")
        {
            if (_history.Count == 0 || _history.Pop() != to)
            {
                Fail("a step back did not return to the previous cell.");
            }
        }
        else if (Deltas.TryGetValue(direction, out var d))
        {
            if (to != new Pos(from.Y + d.Dy, from.X + d.Dx)) Fail($"'{direction}' from {from} cannot end at {to}.");
            if (maze.Has(from, d.Side)) Fail($"moved {direction} through a wall at {from}.");
            _history.Push(from);
        }
        else
        {
            Fail($"unknown direction '{direction}'.");
        }

        Current = to;
        Steps = step;
        LastEvent = $"moved {direction}";
    }

    private void OnBlocked(JsonElement ev)
    {
        var position = MazeView.ReadPos(ev.GetProperty("position"));
        var direction = ev.GetProperty("direction").GetString() ?? "";
        var reason = ev.GetProperty("reason").GetString() ?? "";

        if (position != Current) Fail($"moveBlocked at {position} but the character was at {Current}.");

        if (reason == "wall" && Deltas.TryGetValue(direction, out var d))
        {
            var target = new Pos(position.Y + d.Dy, position.X + d.Dx);
            if (!maze.Has(position, d.Side) && maze.Contains(target))
            {
                Fail($"reported '{direction}' from {position} as blocked but the way is open.");
            }
        }
        else if (reason == "noHistory")
        {
            if (direction != "back" || _history.Count > 0) Fail("noHistory reported although there is a step to go back to.");
        }
        else
        {
            Fail($"unexpected blocked reason '{reason}' for direction '{direction}'.");
        }

        LastEvent = $"blocked {direction} ({reason})";
    }

    private void OnExitReached(JsonElement ev)
    {
        var position = MazeView.ReadPos(ev.GetProperty("position"));
        var steps = ev.GetProperty("steps").GetInt32();

        if (_exitSeen) Fail("'exitReached' was sent twice.");
        if (position != maze.Exit) Fail($"exitReached at {position} but the exit is {maze.Exit}.");
        if (position != Current) Fail($"exitReached at {position} but the character was at {Current}.");
        if (steps != Steps) Fail($"exitReached says {steps} steps, counted {Steps}.");

        _exitSeen = true;
        LastEvent = "exit reached";
    }

    private void OnFinished(JsonElement ev)
    {
        var outcome = ev.GetProperty("outcome").GetString() ?? "";
        var position = MazeView.ReadPos(ev.GetProperty("position"));
        var steps = ev.GetProperty("steps").GetInt32();

        if (!Outcomes.Contains(outcome)) Fail($"unknown outcome '{outcome}'.");
        if (outcome == "fault") Fail("the server reported a fault.");
        if (position != Current) Fail($"finished at {position} but the character was at {Current}.");
        if (steps != Steps) Fail($"finished says {steps} steps, counted {Steps}.");
        if (outcome == "exitReached" && !_exitSeen) Fail("outcome exitReached without an 'exitReached' event.");

        Finished = true;
        Outcome = outcome;
        LastEvent = $"finished: {outcome}";
    }
}