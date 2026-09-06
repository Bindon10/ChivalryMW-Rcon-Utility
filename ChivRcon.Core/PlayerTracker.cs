using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ChivRcon.Core;

/// <summary>
/// One row of the roster.
///
/// Raises PropertyChanged so a UI can bind to a stable instance and watch it change, rather
/// than rebuilding its list every tick. That matters for more than efficiency: replacing the
/// rows destroys whatever the user was interacting with, which is how an open context menu
/// ended up closing itself a second after opening.
///
/// INotifyPropertyChanged is System.ComponentModel, not a UI framework -- Core stays
/// toolkit-agnostic. Mutation is expected on one thread (PlayerTracker.Apply's caller);
/// notifications are raised inline, so that caller must be the UI thread.
/// </summary>
public sealed class Player : INotifyPropertyChanged
{
    private string _name = "?";
    private int _teamId = -1;
    private int _ping;
    private int _score;
    private int _kills;
    private int _rank;
    private int _idleSeconds;
    private int _teamDamage;
    private int _deaths;
    private int _health;
    private string _className = "";
    private bool _isSpectator;
    private string _noteBadge = "";

    public ulong SteamId64 { get; init; }
    public DateTime ConnectedAt { get; init; } = DateTime.Now;

    public string Name { get => _name; set => Set(ref _name, value); }
    public int Ping { get => _ping; set => Set(ref _ping, value); }
    public int Score { get => _score; set => Set(ref _score, value); }
    public int Kills { get => _kills; set => Set(ref _kills, value); }
    public int Rank { get => _rank; set => Set(ref _rank, value); }
    public int TeamDamage { get => _teamDamage; set => Set(ref _teamDamage, value); }
    public int Deaths { get => _deaths; set => Set(ref _deaths, value); }
    public int Health { get => _health; set => Set(ref _health, value); }
    public string ClassName { get => _className; set => Set(ref _className, value); }
    public bool IsSpectator { get => _isSpectator; set => Set(ref _isSpectator, value); }

    /// <summary>Bots get a synthetic id from XangModRCon.EnsureUniqueId; real SteamID64s
    /// never have a zero high half.</summary>
    public bool IsBot => (SteamId64 >> 32) == 0 && SteamId64 != 0;

    /// <summary>Steam3 for humans; bots have no Steam identity to show.</summary>
    public string IdentityText => IsBot ? $"BOT #{SteamId64}" : Steam3;

    public int TeamId
    {
        get => _teamId;
        set { if (Set(ref _teamId, value)) Raise(nameof(TeamName)); }
    }

    /// <summary>Seconds since the player last did anything. Only the extended ping carries it.</summary>
    public int IdleSeconds
    {
        get => _idleSeconds;
        set { if (Set(ref _idleSeconds, value)) Raise(nameof(IdleText)); }
    }

    public string TeamName => TeamId < 0 ? "-" : Teams.Name(TeamId);

    /// <summary>Idle time as mm:ss, or "-" when nothing has reported it yet.</summary>
    public string IdleText => IdleSeconds <= 0 ? "-" : $"{IdleSeconds / 60}:{IdleSeconds % 60:00}";

    public string Steam3 => SteamId.ToSteam3(SteamId64);

    /// <summary>
    /// Short admin-notes marker for this player ("2W", "WATCH", "note"), filled in by the
    /// UI layer from its own store. Lives here rather than in a wrapper type so the roster
    /// can keep binding to one stable Player instance per player -- rebuilding rows is what
    /// used to close the context menu. Plain string data; Core stays toolkit-agnostic.
    /// </summary>
    public string NoteBadge { get => _noteBadge; set => Set(ref _noteBadge, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    private void Raise(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Maintains a live roster from the event stream. Feed every RconEvent into
/// Apply(); it returns true when the roster changed in a way worth re-rendering.
/// </summary>
public sealed class PlayerTracker
{
    private readonly ConcurrentDictionary<ulong, Player> _players = new();

    /// <summary>Ids seen in the current 30-burst, so 31 can prune the rest.</summary>
    private readonly HashSet<ulong> _seenInSweep = new();
    private readonly List<string> _mapList = new();
    private readonly object _mapLock = new();

    public string? CurrentMap { get; private set; }
    public int CurrentMapIndex { get; private set; } = -1;

    public IReadOnlyCollection<Player> Players => _players.Values.ToArray();

    public IReadOnlyList<string> MapList
    {
        get { lock (_mapLock) return _mapList.ToArray(); }
    }

    public Player? Find(ulong steamId64) => _players.TryGetValue(steamId64, out var p) ? p : null;

    public string NameOf(ulong steamId64) =>
        _players.TryGetValue(steamId64, out var p) ? p.Name : SteamId.ToSteam3(steamId64);

    public bool Apply(RconEvent evt)
    {
        switch (evt)
        {
            case PlayerConnectEvent e:
                _players.AddOrUpdate(e.SteamId64,
                    _ => new Player { SteamId64 = e.SteamId64, Name = e.Name },
                    (_, p) => { p.Name = e.Name; return p; });
                return true;

            case PlayerDisconnectEvent e:
                _players.TryRemove(e.SteamId64, out _);
                return true;

            case TeamChangedEvent e:
                if (_players.TryGetValue(e.SteamId64, out var pt)) { pt.TeamId = e.TeamId; return true; }
                return false;

            case NameChangedEvent e:
                if (_players.TryGetValue(e.SteamId64, out var pn)) { pn.Name = e.Name; return true; }
                return false;

            case PingEvent e:
                if (_players.TryGetValue(e.SteamId64, out var pp)) { pp.Ping = e.Ping; return true; }
                return false;

            case PingExtendedEvent e:
                if (_players.TryGetValue(e.SteamId64, out var px))
                {
                    px.Ping = e.Ping; px.Score = e.Score; px.Kills = e.Kills; px.Rank = e.Rank;
                    px.IdleSeconds = e.IdleTime; px.TeamDamage = e.TeamDamageDealt;
                    return true;
                }
                return false;

            // The scoreboard sweep: the only source of deaths, health, class, and bots.
            case PlayerInfoEvent e:
                _seenInSweep.Add(e.SteamId64);
                _players.AddOrUpdate(e.SteamId64,
                    _ => Fill(new Player { SteamId64 = e.SteamId64 }, e),
                    (_, p) => Fill(p, e));
                return true;

            // The sweep is authoritative, so prune anyone it skipped. Guarded on a
            // non-empty sweep so an empty reply mid-map-change cannot wipe the roster.
            case PlayerListEndEvent:
                bool pruned = false;
                if (_seenInSweep.Count > 0)
                {
                    foreach (var id in _players.Keys)
                    {
                        if (!_seenInSweep.Contains(id) && _players.TryRemove(id, out _))
                            pruned = true;
                    }
                }
                _seenInSweep.Clear();
                return pruned;

            case MapChangedEvent e:
                CurrentMap = e.MapName;
                CurrentMapIndex = e.RotationIndex;
                // New map: roster events will be re-sent; keep players (they persist across maps).
                return true;

            case MapListEntryEvent e:
                lock (_mapLock)
                {
                    if (!_mapList.Contains(e.MapName, StringComparer.OrdinalIgnoreCase))
                        _mapList.Add(e.MapName);
                }
                return true;

            default:
                return false;
        }
    }

    private static Player Fill(Player p, PlayerInfoEvent e)
    {
        p.Name = e.Name;
        p.TeamId = e.TeamId;
        p.Score = e.Score;
        p.Deaths = e.Deaths;
        p.Kills = e.Kills;
        p.Ping = e.Ping;
        p.Health = e.Health;
        p.TeamDamage = e.TeamDamageDealt;
        p.ClassName = e.ClassName;
        p.IsSpectator = e.IsSpectator;

        // The family class name is the only thing on the wire that names the game:
        // CDWFamilyInfo_* is Deadliest Warrior, AOCFamilyInfo_* is Medieval Warfare. Empty
        // until someone has picked a class, so this refines the bookmark rather than
        // replacing it.
        GameProfile.NoteClassName(e.ClassName);

        return p;
    }

    public void Clear()
    {
        _seenInSweep.Clear();
        _players.Clear();
        lock (_mapLock) _mapList.Clear();
        CurrentMap = null;
        CurrentMapIndex = -1;
    }
}
