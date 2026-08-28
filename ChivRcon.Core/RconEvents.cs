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

// ---- BangMod additions (require BangModRCon) ----

/// <summary>One row of the player list. Kills come from AOCPRI.NumKills.</summary>
public sealed record PlayerInfoEvent(
    ulong SteamId64, string Name, int TeamId, int Score, int Deaths,
    int Kills, int Ping, int Health, int TeamDamageDealt,
    string ClassName, bool IsSpectator) : RconEvent;

public sealed record PlayerListEndEvent(int Count) : RconEvent;

public sealed record ServerInfoEvent(
    string MapName, int NumPlayers, int MaxPlayers,
    bool MatchBegun, int NumSpectators) : RconEvent;

/// <summary>Output of a ConsoleCommand. Vanilla discarded this; BangModRCon returns it.</summary>
public sealed record ConsoleResultEvent(string Command, string Result) : RconEvent;

public sealed record BanInfoEvent(
    ulong SteamId64, string Name, string Reason,
    int DurationSeconds, string NetIdString, string IpPolicy) : RconEvent;

public sealed record BanListEndEvent(int Count) : RconEvent;

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
    public static string Name(int teamId) => teamId switch
    {
        0 => "Agatha",
        1 => "Mason",
        2 => "FFA",
        3 => "Spectator",
        4 => "All",  // EFAC_ALL - all-chat / broadcasts
        _ => $"Team {teamId}",
    };
}
