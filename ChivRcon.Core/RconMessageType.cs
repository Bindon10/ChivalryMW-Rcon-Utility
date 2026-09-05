namespace ChivRcon.Core;

/// <summary>
/// Message type IDs from AOCRCon.uc (native protocol, 0-22) plus the
/// ChivAdmin mutator extensions (23-28) which only work when the ChivAdmin
/// server mutator is installed.
/// </summary>
public enum RconMessageType : ushort
{
    ServerConnect = 0,
    ServerConnectSuccess = 1,
    Password = 2,
    PlayerChat = 3,
    PlayerConnect = 4,
    PlayerDisconnect = 5,
    SayAll = 6,
    SayAllBig = 7,
    Say = 8,
    MapChanged = 9,
    MapList = 10,
    ChangeMap = 11,
    RotateMap = 12,
    TeamChanged = 13,
    NameChanged = 14,
    Kill = 15,
    Suicide = 16,
    KickPlayer = 17,
    TempBanPlayer = 18,
    BanPlayer = 19,
    UnbanPlayer = 20,
    RoundEnd = 21,
    Ping = 22,

    // --- ChivAdmin mutator extensions (require server-side mutator) ---
    PingExtended = 23,
    ChangeScore = 24,
    KillPlayer = 25,
    Inebriate = 26,
    ChangeGamePassword = 27,
    ConsoleCommand = 28,

    // --- XangMod additions (require XangModRCon; see RCON_PROTOCOL.md) ---
    PlayerListRequest = 29,
    PlayerInfo = 30,
    PlayerListEnd = 31,
    SetTeam = 32,
    ForceSpectate = 33,
    SetTeamScore = 34,
    AdminAudit = 35,
    ServerInfoRequest = 36,
    ServerInfo = 37,
    ConsoleResult = 38,
    BanListRequest = 39,
    BanInfo = 40,
    BanListEnd = 41,
    MutePlayer = 42,
    SetPause = 43,
    EndMatch = 44,
    SetAutoBalance = 45,
    SetGameSpeed = 46,
    RestartMatch = 47,
    SoberPlayer = 48,
    SetTournament = 49,
    ReadyAll = 50,
    SetFrozen = 51,
    SetClass = 52,
    LoadoutRequest = 53,
    LoadoutOption = 54,
    LoadoutEnd = 55,
    SetLoadout = 56,
    PlayerPosRequest = 57,
    PlayerPos = 58,
    PlayerPosEnd = 59,
    Teleport = 60,
    Slap = 61,
    MuteListRequest = 62,
    MuteInfo = 63,
    MuteListEnd = 64,
}

/// <summary>Scope for the mutator-only ConsoleCommand message.</summary>
public enum ConsoleCommandScope
{
    /// <summary>Run against the GameInfo.</summary>
    Game = 0,
    /// <summary>Run against one player's SERVER-SIDE controller. Not their machine.</summary>
    Player = 1,
    /// <summary>Run against every connected player's server-side controller.</summary>
    AllPlayers = 2,
}
