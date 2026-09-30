using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using Dango.Protocol;

namespace Dango.MultiplayerSample;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string? sessionPath = null;
        try
        {
            var options = Parse(args);
            var repository = SessionFiles.FindRepository();
            if (options.TryGetValue("session", out var name))
            {
                // Serialize the Exists/CreateDirectory check and hold ownership for the whole run.
                using var sessionOwner = new Mutex(true, "Local\\Dango.MultiplayerSession." + name, out var owned);
                if (!owned) return 4;
                sessionPath = SessionFiles.Create(repository, name);
                try
                {
                    return LocalSession.RunAsync(sessionPath, options["server"], options.ContainsKey("verify"),
                        options.ContainsKey("fallback-b")).GetAwaiter().GetResult();
                }
                finally { sessionOwner.ReleaseMutex(); }
            }
            if (!options.TryGetValue("client", out var role) || role is not ("A" or "B")) return 2;
            sessionPath = SessionFiles.Resolve(repository, options["run-name"]);
            var clientDirectory = SessionFiles.ResolveClient(sessionPath, role);
            using var instance = new Mutex(true, $"Local\\Dango.MultiplayerSample.{options["run-name"]}.{role}", out var acquired);
            if (!acquired) return 4;
            using var bootstrapPipe = new AnonymousPipeClientStream(PipeDirection.In, options["bootstrap-pipe"]);
            using var bootstrapReader = new StreamReader(bootstrapPipe);
            var line = bootstrapReader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            var bootstrap = JsonSerializer.Deserialize<LocalClientBootstrap>(line ?? "", RoomProtocol.Json)
                ?? throw new InvalidDataException("bootstrap_required");
            var endpoint = new Uri(bootstrap.Endpoint);
            if (endpoint.Scheme != "http" || !endpoint.IsLoopback) throw new InvalidDataException("loopback_required");
            using var control = new AnonymousPipeClientStream(PipeDirection.In, options["control-pipe"]);
            using var reports = new AnonymousPipeClientStream(PipeDirection.Out, options["report-pipe"]);
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var window = new MultiplayerWindow(role, clientDirectory, bootstrap, control, reports,
                options.ContainsKey("fallback"));
            var failed = false;
            app.DispatcherUnhandledException += (_, e) =>
            {
                SessionFiles.Write(Path.Combine(sessionPath, role), "failure.json", new { code = "dispatcher_failure", kind = e.Exception.GetType().Name });
                e.Handled = true;
                failed = true;
                window.Close();
            };
            var code = app.Run(window);
            instance.ReleaseMutex();
            return failed || window.HasFailure ? 10 : code;
        }
        catch (Exception exception)
        {
            // Exception messages may contain endpoint or credential data; retain only safe categories.
            if (sessionPath is not null && Directory.Exists(sessionPath))
                SessionFiles.Write(sessionPath, "failure.json", new { code = "session_failed", kind = exception.GetType().Name });
            return 10;
        }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("invalid_option");
            var key = args[i][2..];
            var value = key is "verify" or "fallback" or "fallback-b" ? "true"
                : ++i < args.Length ? args[i] : throw new ArgumentException("option_value_required");
            if (!result.TryAdd(key, value)) throw new ArgumentException("duplicate_option");
        }
        return result;
    }
}
