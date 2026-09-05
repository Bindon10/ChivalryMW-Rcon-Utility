using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChivRcon.App;

/// <summary>What we remember about one player, across sessions and across admins.</summary>
public sealed class PlayerNote
{
    public ulong SteamId64 { get; set; }

    /// <summary>Free text. The reason this feature exists.</summary>
    public string Note { get; set; } = "";

    /// <summary>Formal warnings issued. "This is his third" is the whole point.</summary>
    public int Warnings { get; set; }

    /// <summary>Flagged for attention on connect.</summary>
    public bool Watch { get; set; }

    public DateTime FirstSeen { get; set; } = DateTime.Now;
    public DateTime LastSeen { get; set; } = DateTime.Now;

    /// <summary>Every name we have seen, newest last. Name changes are a classic dodge.</summary>
    public List<string> Names { get; set; } = new();

    [JsonIgnore]
    public string LatestName => Names.Count > 0 ? Names[^1] : "";

    /// <summary>Compact roster cell: warn count and flags, or blank when we know nothing.</summary>
    [JsonIgnore]
    public string Badge
    {
        get
        {
            var parts = new List<string>();
            if (Warnings > 0) parts.Add($"{Warnings}W");
            if (Watch) parts.Add("WATCH");
            if (parts.Count == 0 && Note.Length > 0) parts.Add("note");
            return string.Join(" ", parts);
        }
    }

    [JsonIgnore]
    public bool IsEmpty => Warnings == 0 && !Watch && Note.Length == 0;
}

/// <summary>
/// Notes keyed by SteamID64, in %AppData%\ChivRconUtility\notes.json.
///
/// Keyed by ID rather than name on purpose: names are the one thing a player being noted
/// can change at will, and NameChangedEvent tells us when they do -- so the alias list is
/// itself evidence rather than a lookup key.
///
/// Held entirely in memory and saved on change. The file is small (a busy community is
/// thousands of entries, not millions), and keeping it loaded means the roster can render
/// a badge per row without touching the disk on every 1.5s tick.
/// </summary>
public sealed class PlayerNotes
{
    private readonly Dictionary<ulong, PlayerNote> _byId = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ChivRconUtility", "notes.json");

    public PlayerNotes() => Load();

    /// <summary>The stored note, or null when we have never recorded anything.</summary>
    public PlayerNote? Find(ulong steamId64) => _byId.GetValueOrDefault(steamId64);

    /// <summary>The stored note, creating an empty one if needed.</summary>
    public PlayerNote GetOrCreate(ulong steamId64)
    {
        if (_byId.TryGetValue(steamId64, out var existing)) return existing;
        var created = new PlayerNote { SteamId64 = steamId64 };
        _byId[steamId64] = created;
        return created;
    }

    public string BadgeFor(ulong steamId64) => Find(steamId64)?.Badge ?? "";

    /// <summary>
    /// Records that we have seen this player under this name. Called on connect and on
    /// rename, so the alias trail builds itself without anyone having to remember to.
    /// </summary>
    public void Observe(ulong steamId64, string name)
    {
        if (steamId64 == 0) return;

        var note = GetOrCreate(steamId64);
        note.LastSeen = DateTime.Now;

        if (!string.IsNullOrWhiteSpace(name)
            && (note.Names.Count == 0 || !note.Names[^1].Equals(name, StringComparison.Ordinal)))
        {
            note.Names.Remove(name);            // seen before under this name: move it to newest
            note.Names.Add(name);
            if (note.Names.Count > 12) note.Names.RemoveAt(0);
        }

        // Only worth persisting once there is something a human put there. Otherwise every
        // connect on a busy server would rewrite the file.
        if (!note.IsEmpty) Save();
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            // Only entries carrying real content. Observe() creates a row for everyone who
            // connects; writing those would grow the file without recording anything.
            var keep = _byId.Values.Where(n => !n.IsEmpty || n.Names.Count > 0).ToList();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(keep, Json));
        }
        catch { /* notes must never take a command down */ }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var list = JsonSerializer.Deserialize<List<PlayerNote>>(File.ReadAllText(FilePath));
            if (list is null) return;
            foreach (var n in list) _byId[n.SteamId64] = n;
        }
        catch { /* a corrupt file starts empty rather than blocking startup */ }
    }
}
