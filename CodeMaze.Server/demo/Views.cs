using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

// Browser view of the runs: serves one page and pushes the maze and the character's state to it.

/// <summary>
/// Tiny web server on top of HttpListener. Serves one page and pushes maze and state updates
/// to it as server-sent events. A browser that connects late is brought up to date from the last messages.
/// </summary>
internal sealed class WebViewer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly List<HttpListenerResponse> _clients = new();
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _firstClient = new();
    private string? _lastMaze;
    private string? _lastState;

    public Uri Url { get; }

    public WebViewer(int port)
    {
        if (port == 0) port = FreePort();
        Url = new Uri($"http://localhost:{port}/");
        _listener.Prefixes.Add(Url.AbsoluteUri);
        _listener.Start();
        _ = Task.Run(AcceptLoop);
    }

    /// <summary>Starts the viewer, opens the browser and waits briefly for it to connect. Null if it cannot start.</summary>
    public static WebViewer? TryStart(Options opts)
    {
        try
        {
            var viewer = new WebViewer(opts.WebPort);
            Console.WriteLine($"Web view started at {viewer.Url}");

            if (opts.OpenBrowser && !viewer.TryOpenBrowser())
            {
                Console.WriteLine("Could not open a browser automatically, open the address above yourself.");
            }

            if (!viewer.WaitForClient(TimeSpan.FromSeconds(10)))
            {
                Console.WriteLine("No browser connected yet, continuing anyway.");
            }
            return viewer;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Could not start the web view ({e.Message}). Continuing without it, events are still logged.");
            return null;
        }
    }

    /// <summary>Keeps the viewer alive after the last run so the final state can be looked at.</summary>
    public void WaitForUser()
    {
        Console.WriteLine($"Web view still running at {Url}");
        if (!Console.IsInputRedirected)
        {
            Console.WriteLine("Press Enter to quit.");
            Console.ReadLine();
        }
        Dispose();
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public bool TryOpenBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Url.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool WaitForClient(TimeSpan timeout) => _firstClient.Wait(timeout);

    public void ShowMaze(MazeView m, RunReport report, int totalRuns)
    {
        var json = JsonSerializer.Serialize(new
        {
            type = "maze",
            run = report.Number,
            total = totalRuns,
            kind = report.Kind,
            width = m.Width,
            height = m.Height,
            entrance = new { y = m.Entrance.Y, x = m.Entrance.X },
            exit = new { y = m.Exit.Y, x = m.Exit.X },
            cells = m.WallBytes.Select(b => (int)b).ToArray(),
        });
        Broadcast(json, isMaze: true);
    }

    public void ShowState(RunSession s)
    {
        var json = JsonSerializer.Serialize(new
        {
            type = "state",
            position = s.Current is null ? null : new { y = s.Current.Y, x = s.Current.X },
            steps = s.Steps,
            last = s.LastEvent,
        });
        Broadcast(json, isMaze: false);
    }

    private void Broadcast(string json, bool isMaze)
    {
        lock (_gate)
        {
            if (isMaze)
            {
                _lastMaze = json;
                _lastState = null;
            }
            else
            {
                _lastState = json;
            }

            for (var i = _clients.Count - 1; i >= 0; i--)
            {
                try
                {
                    Write(_clients[i], json);
                }
                catch (Exception)
                {
                    try { _clients[i].Abort(); } catch (Exception) { /* already gone */ }
                    _clients.RemoveAt(i);
                }
            }
        }
    }

    private static void Write(HttpListenerResponse response, string json)
    {
        var bytes = Encoding.UTF8.GetBytes($"data: {json}\n\n");
        response.OutputStream.Write(bytes);
        response.OutputStream.Flush();
    }

    private async Task AcceptLoop()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync();
            }
            catch (Exception)
            {
                break; // listener closed
            }

            try
            {
                Handle(ctx);
            }
            catch (Exception)
            {
                try { ctx.Response.Abort(); } catch (Exception) { /* already gone */ }
            }
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        var response = ctx.Response;
        var path = ctx.Request.Url?.AbsolutePath ?? "/";

        if (path == "/events")
        {
            response.ContentType = "text/event-stream";
            response.Headers["Cache-Control"] = "no-cache";
            response.SendChunked = true;

            lock (_gate)
            {
                if (_lastMaze is not null) Write(response, _lastMaze);
                if (_lastState is not null) Write(response, _lastState);
                _clients.Add(response);
            }
            _firstClient.Set();
            return; // stays open
        }

        if (path == "/")
        {
            var bytes = Encoding.UTF8.GetBytes(Html);
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes);
            response.Close();
            return;
        }

        response.StatusCode = 404;
        response.Close();
    }

    public void Dispose()
    {
        try { _listener.Close(); } catch (Exception) { /* ignore */ }
    }

    private const string Html = """
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>CodeMaze validator</title>
        <style>
          :root { --bg:#fafafa; --fg:#1b1b1b; --wall:#222; --start:#c8f0c8; --exit:#f4c4c4; --me:#e8590c; --muted:#666; }
          @media (prefers-color-scheme: dark) {
            :root { --bg:#161616; --fg:#eaeaea; --wall:#d8d8d8; --start:#1f4d2b; --exit:#5a2323; --me:#ff922b; --muted:#999; }
          }
          body { margin:0; padding:16px; background:var(--bg); color:var(--fg); font:14px/1.4 system-ui, sans-serif; }
          h1 { font-size:16px; margin:0 0 4px; }
          #status, #conn { font-family:ui-monospace, monospace; color:var(--muted); min-height:1.4em; }
          #conn { margin-bottom:8px; }
          canvas { display:block; }
        </style>
        </head>
        <body>
        <h1 id="title">Waiting for the first run...</h1>
        <div id="status"></div>
        <div id="conn"></div>
        <canvas id="maze"></canvas>
        <script>
        const canvas = document.getElementById('maze');
        const ctx = canvas.getContext('2d');
        const title = document.getElementById('title');
        const statusEl = document.getElementById('status');
        const connEl = document.getElementById('conn');
        let maze = null, state = null;

        const css = name => getComputedStyle(document.documentElement).getPropertyValue(name).trim();

        function draw() {
          if (!maze) return;
          const pad = 8, W = maze.width, H = maze.height, cells = maze.cells;
          const availW = window.innerWidth - 32 - 2 * pad;
          const availH = window.innerHeight - 110 - 2 * pad;
          const cs = Math.max(4, Math.min(40, Math.floor(Math.min(availW / W, availH / H))));
          const dpr = window.devicePixelRatio || 1;
          const pxW = W * cs + 2 * pad, pxH = H * cs + 2 * pad;

          canvas.width = pxW * dpr;
          canvas.height = pxH * dpr;
          canvas.style.width = pxW + 'px';
          canvas.style.height = pxH + 'px';
          ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
          ctx.clearRect(0, 0, pxW, pxH);

          const cx = p => pad + p.x * cs;
          const cy = p => pad + p.y * cs;
          const fill = (p, color) => { ctx.fillStyle = color; ctx.fillRect(cx(p), cy(p), cs, cs); };
          const label = (p, ch, color) => {
            if (cs < 10) return;
            ctx.fillStyle = color;
            ctx.font = 'bold ' + Math.floor(cs * 0.6) + 'px ui-monospace, monospace';
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            ctx.fillText(ch, cx(p) + cs / 2, cy(p) + cs / 2);
          };

          // start (S) and exit (E), then the character (@) on top of both
          fill(maze.entrance, css('--start'));
          fill(maze.exit, css('--exit'));
          label(maze.entrance, 'S', css('--fg'));
          label(maze.exit, 'E', css('--fg'));
          if (state && state.position) {
            const p = state.position;
            ctx.fillStyle = css('--me');
            ctx.beginPath();
            ctx.arc(cx(p) + cs / 2, cy(p) + cs / 2, Math.max(2, cs * 0.35), 0, Math.PI * 2);
            ctx.fill();
            label(p, '@', css('--bg'));
          }

          // walls: 1 = top, 2 = right, 4 = bottom, 8 = left; a shared edge is drawn if either cell has it
          ctx.strokeStyle = css('--wall');
          ctx.lineWidth = cs >= 12 ? 2 : 1;
          ctx.lineCap = 'square';
          ctx.beginPath();
          const line = (x1, y1, x2, y2) => {
            ctx.moveTo(pad + x1 * cs, pad + y1 * cs);
            ctx.lineTo(pad + x2 * cs, pad + y2 * cs);
          };
          for (let y = 0; y < H; y++) {
            for (let x = 0; x < W; x++) {
              const f = cells[y * W + x];
              const top = (f & 1) || (y > 0 && (cells[(y - 1) * W + x] & 4));
              const left = (f & 8) || (x > 0 && (cells[y * W + x - 1] & 2));
              if (top) line(x, y, x + 1, y);
              if (left) line(x, y, x, y + 1);
              if (x === W - 1 && (f & 2)) line(x + 1, y, x + 1, y + 1);
              if (y === H - 1 && (f & 4)) line(x, y + 1, x + 1, y + 1);
            }
          }
          ctx.stroke();
        }

        const source = new EventSource('/events');
        source.onopen = () => { connEl.textContent = ''; };
        source.onerror = () => { connEl.textContent = 'connection lost (the validator may have finished), retrying...'; };
        source.onmessage = ev => {
          const m = JSON.parse(ev.data);
          if (m.type === 'maze') {
            maze = m;
            state = null;
            title.textContent = 'Run ' + m.run + '/' + m.total + ' [' + m.kind + '] - maze ' + m.width + 'x' + m.height;
            statusEl.textContent = '';
          } else if (m.type === 'state') {
            state = m;
            statusEl.textContent = 'step ' + m.steps + '   last: ' + m.last;
          }
          draw();
        };
        window.addEventListener('resize', draw);
        </script>
        </body>
        </html>
        """;
}