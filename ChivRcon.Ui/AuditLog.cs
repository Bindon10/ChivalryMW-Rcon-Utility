using System.Text.Json;

namespace ChivRcon.App;

/// <summary>
/// Append-only record of admin activity, one file per month under
/// %AppData%\ChivRconUtility\audit\.
///
/// The point is the question you get asked weeks later: who banned this player, when, and
/// why. The server already narrates its side over opcode 35, but that only ever lived in a
/// scrollback that dies with the process, and the server's own log does not know which
/// admin was holding the RCON connection.
///
/// Two kinds of line are written:
///   sent     -- a command this client issued, recorded even if the server never answers
///   audit    -- the server's own opcode 35 confirmation of what it actually did
/// Both matter. A "sent" with no matching "audit" is the signature of a command the server
/// ignored, which is exactly what a vanilla server does with a XangMod opcode.
///
/// JSONL rather than a database: greppable, appendable, survives a crash mid-write with at
/// worst one truncated line, and anyone can read it without this app.
/// </summary>
/// <summary>One line read back out of the log.</summary>
public sealed record AuditEntry(
    DateTimeOffset At, string Kind, string Admin, string Server, string Action, string Detail)
{
    public string TimeText => At.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}

public sealed class AuditLog : IDisposable
{
    private readonly object _gate = new();
    private StreamWriter? _writer;
    private string _monthKey = "";

    /// <summary>Server this session is pointed at, recorded on every line.</summary>
    public string Server { get; set; } = "";

    /// <summary>Whoever is at the keyboard. Windows username unless overridden.</summary>
    public string Admin { get; set; } = Environment.UserName;

    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ChivRconUtility", "audit");

    public void RecordSent(string action, string detail) => Write("sent", action, detail);

    public void RecordServer(string action, string detail) => Write("audit", action, detail);

    private void Write(string kind, string action, string detail)
    {
        try
        {
            lock (_gate)
            {
                Roll();
                if (_writer is null) return;

                _writer.WriteLine(JsonSerializer.Serialize(new
                {
                    at = DateTimeOffset.Now.ToString("o"),
                    kind,
                    admin = Admin,
                    server = Server,
                    action,
                    detail,
                }));
            }
        }
        catch
        {
            // An audit log that can throw is worse than one that misses a line -- it would
            // take a command down with it. Failures here are silent by design.
        }
    }

    /// <summary>Opens (or rolls to) this month's file. Caller holds the lock.</summary>
    private void Roll()
    {
        string key = DateTime.Now.ToString("yyyy-MM");
        if (_writer is not null && key == _monthKey) return;

        _writer?.Dispose();
        _monthKey = key;
        System.IO.Directory.CreateDirectory(Directory);
        _writer = new StreamWriter(Path.Combine(Directory, $"audit-{key}.jsonl"), append: true)
        {
            AutoFlush = true,   // a crash must not cost the last few actions
        };
    }

    /// <summary>Month keys (yyyy-MM) with a log on disk, newest first.</summary>
    public static IReadOnlyList<string> Months()
    {
        try
        {
            if (!System.IO.Directory.Exists(Directory)) return Array.Empty<string>();
            return System.IO.Directory.GetFiles(Directory, "audit-*.jsonl")
                .Select(f => Path.GetFileNameWithoutExtension(f)["audit-".Length..])
                .OrderByDescending(k => k, StringComparer.Ordinal)
                .ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>Read one month, newest first. A truncated last line is skipped, not fatal.</summary>
    public IReadOnlyList<AuditEntry> Read(string monthKey)
    {
        var result = new List<AuditEntry>();
        string path = Path.Combine(Directory, $"audit-{monthKey}.jsonl");

        try
        {
            // The writer is AutoFlush but still open, so share it.
            lock (_gate)
            {
                if (!File.Exists(path)) return result;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                while (sr.ReadLine() is { } line)
                {
                    if (line.Length == 0) continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var r = doc.RootElement;
                        result.Add(new AuditEntry(
                            DateTimeOffset.TryParse(Str(r, "at"), out var at) ? at : default,
                            Str(r, "kind"), Str(r, "admin"), Str(r, "server"),
                            Str(r, "action"), Str(r, "detail")));
                    }
                    catch { /* skip a torn line */ }
                }
            }
        }
        catch { /* an unreadable log shows as empty */ }

        result.Reverse();
        return result;
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
