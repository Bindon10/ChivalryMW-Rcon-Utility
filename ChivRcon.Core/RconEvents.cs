namespace ChivRcon.Core;

/// <summary>Base type for all events pushed by the server.</summary>
public abstract record RconEvent
{
    public DateTime Timestamp { get; } = DateTime.Now;
}

public sealed record ServerConnectEvent(string Challenge) : RconEvent;
public sealed record AuthSucceededEvent : RconEvent;
public sealed record PlayerChatEvent(ulong SteamId64, string Message, int TeamId) : RconEvent;
public sealed record PlayerConnectEvent(ulong SteamId64, string Name) : RconEvent;
public sealed record PlayerDisconnectEvent(ulong SteamId64) : RconEvent;
public sealed record MapChangedEvent(int RotationIndex, string MapName) : RconEvent;
public sealed record MapListEntryEvent(string MapName) : RconEvent;
public sealed record TeamChangedEvent(ulong SteamId64, int TeamId) : RconEvent;
public sealed record NameChangedEvent(ulong SteamId64, string Name) : RconEvent;
public sealed record KillEvent(ulong KillerSteamId64, ulong VictimSteamId64, string? WeaponName) : RconEvent;
public sealed record SuicideEvent(ulong SteamId64) : RconEvent;
public sealed record RoundEndEvent(int WinningTeamId) : RconEvent;
public sealed record PingEvent(ulong SteamId64, int Ping) : RconEvent;

/// <summary>ChivAdmin mutator extension event (requires server-side mutator).</summary>
public sealed record PingExtendedEvent(
    ulong SteamId64, int Ping, int Score, int IdleTime,
    int Kills, int TeamDamageDealt, int Rank) : RconEvent;

// ---- XangMod additions (require XangModRCon) ----

/// <summary>One row of the player list. Kills come from AOCPRI.NumKills.</summary>
public sealed record PlayerInfoEvent(
    ulong SteamId64, string Name, int TeamId, int Score, int Deaths,
    int Kills, int Ping, int Health, int TeamDamageDealt,
    string ClassName, bool IsSpectator) : RconEvent;

public sealed record PlayerListEndEvent(int Count) : RconEvent;

/// <summary>
/// One playable team the server reported, by its real team index.
///
/// Name and ColorName are usually different things on a multi-team map: Deadliest Warrior
/// renames a team to its class when it is restricted to one, so a six-team match reports
/// "Vikings" / "Ninjas" while the scoreboard shows blue / black. ColorHex is the game's own
/// team-text markup colour, e.g. "#388FF2". Both colour fields are empty against a server
/// that does not send them.
/// </summary>
public sealed record ServerTeam(int Index, string Name, string ColorName = "", string ColorHex = "")
{
    /// <summary>
    /// Colour first when we have one — that is what an admin reads off the scoreboard.
    ///
    /// An unrestricted team is already named after its colour ("Blue Team" for colour
    /// "Blue"), so prefixing would read "Blue — Blue Team". Only prefix when the name says
    /// something the colour does not, which is the class-restricted case ("Red — Ninja").
    /// </summary>
    public string Label
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ColorName)) return Name;
            if (string.IsNullOrWhiteSpace(Name)) return ColorName;
            return Name.StartsWith(ColorName, StringComparison.OrdinalIgnoreCase)
                ? Name
                : $"{ColorName} — {Name}";
        }
    }
}

/// <summary>
/// Reply to opcode 36. ModName, GameName and Teams are additions to the layout AdminMod ships
/// and are empty against any server that predates them — which is itself informative, since
/// AdminModDW has never shipped without them. See <see cref="GameProfile.NoteServerInfo"/>.
///
/// Teams is the live team list, not a guess: Deadliest Warrior runs one to six teams
/// depending on the mode and the server's ?NumTeams= option, and the indices are not
/// necessarily contiguous with the array they came from.
/// </summary>
public sealed record ServerInfoEvent(
    string MapName, int NumPlayers, int MaxPlayers,
    bool MatchBegun, int NumSpectators,
    string ModName = "", string GameName = "",
    IReadOnlyList<ServerTeam>? Teams = null) : RconEvent;

/// <summary>Output of a ConsoleCommand. Vanilla discarded this; XangModRCon returns it.</summary>
public sealed record ConsoleResultEvent(string Command, string Result) : RconEvent;

public sealed record BanInfoEvent(
    ulong SteamId64, string Name, string Reason,
    int DurationSeconds, string NetIdString, string IpPolicy) : RconEvent;

public sealed record BanListEndEvent(int Count) : RconEvent;

/// <summary>
/// One stored text mute. The server keeps these in its ini like bans, so a mute survives
/// the player reconnecting and the server restarting. <paramref name="Online"/> is false
/// for someone currently disconnected, in which case <paramref name="Name"/> is whatever
/// they were called when muted and <paramref name="TeamId"/> is -1.
/// </summary>
public sealed record MuteInfoEvent(
    ulong SteamId64, string Name, int TeamId, bool Online) : RconEvent;

public sealed record MuteListEndEvent(int Count) : RconEvent;

/// <summary>One weapon a player's current class may take, by slot and index.</summary>
public sealed record LoadoutOptionEvent(
    ulong SteamId64, int Slot, int Index, string Weapon) : RconEvent;

public sealed record LoadoutEndEvent(
    ulong SteamId64, int PrimaryCount, int SecondaryCount, int TertiaryCount) : RconEvent;

/// <summary>
/// One player's position for the map. Read from the live pawn server-side, so it is current
/// rather than the 2s-stale AOCPRI.PawnLocation; a dead player falls back to where they died.
/// Yaw is already in degrees.
/// </summary>
public sealed record PlayerPosEvent(
    ulong SteamId64, string Name, int TeamId,
    int X, int Y, int Z, int Yaw, bool Alive, int Health) : RconEvent;

public sealed record PlayerPosEndEvent(int Count) : RconEvent;

/// <summary>Server-side audit of an admin action taken over RCON.</summary>
public sealed record AdminAuditEvent(string Action, string Detail) : RconEvent;

/// <summary>Unknown or unparseable message; raw payload preserved.</summary>
public sealed record UnknownEvent(ushort MessageType, byte[] Payload) : RconEvent;

public static class Teams
{
    /// <summary>
    /// Team name for the flavour currently connected. The tables live in
    /// <see cref="GameProfile"/> -- Deadliest Warrior's EAOCFaction is a different enum, six
    /// colour teams rather than two factions, so the same index means a different team.
    /// </summary>
    public static string Name(int teamId) => GameProfile.TeamName(teamId, GameProfile.Current);

    public static string Name(int teamId, GameFlavor flavor) => GameProfile.TeamName(teamId, flavor);
}
