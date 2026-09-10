using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace ChivRcon.Core;

public enum RconState
{
    Disconnected,
    Connecting,
    Authenticating,
    Connected,
}

/// <summary>
/// Async client for the native Chivalry: Medieval Warfare RCON protocol
/// (AOCRCon.uc). Connect with ConnectAsync; incoming server events are raised
/// on the EventReceived event (from a background task - marshal to your UI
/// thread yourself).
/// </summary>
public sealed class RconClient : IDisposable
{
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private Task? _readLoop;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private TaskCompletionSource<bool>? _authTcs;
    private string _password = string.Empty;

    public RconState State { get; private set; } = RconState.Disconnected;

    public event Action<RconEvent>? EventReceived;
    public event Action<RconState>? StateChanged;
    /// <summary>Raised when the connection ends; argument is a human-readable reason.</summary>
    public event Action<string>? Disconnected;

    /// <summary>
    /// Connects and authenticates. The server sends a 10-char challenge; we
    /// reply with SHA1(password + challenge) as a 40-char UPPERCASE hex string
    /// in a single 50-byte packet (the server enforces both the size and the
    /// case-sensitive uppercase compare, per AOCRCon.uc).
    /// On a wrong password the server silently drops the connection.
    /// </summary>
    public async Task ConnectAsync(string host, int port, string password, TimeSpan? timeout = null)
    {
        if (State != RconState.Disconnected)
            throw new InvalidOperationException("Already connected or connecting.");

        timeout ??= TimeSpan.FromSeconds(10);
        _password = password ?? string.Empty;
        _cts = new CancellationTokenSource();
        _authTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        SetState(RconState.Connecting);
        try
        {
            _tcp = new TcpClient { NoDelay = true };
            using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
            {
                connectCts.CancelAfter(timeout.Value);
                await _tcp.ConnectAsync(host, port, connectCts.Token).ConfigureAwait(false);
            }
            _stream = _tcp.GetStream();
            SetState(RconState.Authenticating);
            _readLoop = Task.Run(() => ReadLoopAsync(_cts.Token));

            // AOCRCon closes unauthenticated connections after 2 seconds, so
            // the handshake either completes quickly or the socket dies.
            var authTask = _authTcs.Task;
            var winner = await Task.WhenAny(authTask, Task.Delay(timeout.Value)).ConfigureAwait(false);
            if (winner != authTask || !await authTask.ConfigureAwait(false))
            {
                Teardown("Authentication failed - server dropped the connection. Check AdminPassword and RConPort.");
                throw new RconAuthException("Authentication failed (wrong password, or the port is not an RCON port).");
            }
            SetState(RconState.Connected);
        }
        catch (RconAuthException) { throw; }
        catch (Exception ex)
        {
            Teardown($"Connection failed: {ex.Message}");
            throw;
        }
    }

    public void Disconnect() => Teardown("Disconnected by user.");

    // ---------------- Native commands (to server) ----------------

    public Task SayAllAsync(string message) =>
        SendAsync(new PacketBuilder().AddString(message).Build(RconMessageType.SayAll));

    public Task SayAllBigAsync(string message) =>
        SendAsync(new PacketBuilder().AddString(message).Build(RconMessageType.SayAllBig));

    public Task SayToAsync(ulong steamId64, string message) =>
        SendAsync(new PacketBuilder().AddUInt64(steamId64).AddString(message).Build(RconMessageType.Say));

    public Task ChangeMapAsync(string mapName) =>
        SendAsync(new PacketBuilder().AddString(mapName).Build(RconMessageType.ChangeMap));

    public Task RotateMapAsync() =>
        SendAsync(new PacketBuilder().Build(RconMessageType.RotateMap));

    public Task KickAsync(ulong steamId64, string reason) =>
        SendAsync(new PacketBuilder().AddUInt64(steamId64).AddString(reason).Build(RconMessageType.KickPlayer));

    public Task TempBanAsync(ulong steamId64, string reason, int durationSeconds) =>
        SendAsync(new PacketBuilder().AddUInt64(steamId64).AddString(reason).AddInt32(durationSeconds)
            .Build(RconMessageType.TempBanPlayer));

    public Task BanAsync(ulong steamId64, string reason) =>
        SendAsync(new PacketBuilder().AddUInt64(steamId64).AddString(reason).Build(RconMessageType.BanPlayer));

    public Task UnbanAsync(ulong steamId64) =>
        SendAsync(new PacketBuilder().AddUInt64(steamId64).Build(RconMessageType.UnbanPlayer));

    // ------------- ChivAdmin mutator extensions (optional) -------------

    public Task ConsoleCommandAsync(ulong steamId64, ConsoleCommandScope scope, string command) =>
        SendAsync(new PacketBuilder().AddUInt64(steamId64).AddInt32((int)scope).AddString(command)
            .Build(RconMessageType.ConsoleCommand));

    public Task KillPlayerAsync(ulong steamId64) =>
        SendAsync(new PacketBuilder().AddUInt64(steamId64).Build(RconMessageType.KillPlayer));

    public Task ChangeScoreAsync(ulong steamId64, int score) =>
        SendAsync(new PacketBuilder().AddUInt64(steamId64).AddInt32(score).Build(RconMessageType.ChangeScore));

    /// <summary>
    /// Drunk-camera + slurred audio on one player. ChivAdmin's version was one-way and
    /// wore off on respawn; <see cref="SoberPlayerAsync"/> (XangMod opcode 48) undoes it.
    /// </summary>
    public Task InebriateAsync(ulong steamId64) =>
        SendAsync(new PacketBuilder().AddUInt64(steamId64).Build(RconMessageType.Inebriate));

    public Task ChangeGamePasswordAsync(string newPassword) =>
        SendAsync(new PacketBuilder().AddString(newPassword).Build(RconMessageType.ChangeGamePassword));

    // ------------- XangMod additions (require XangModRCon) -------------

    /// <summary>Request the scoreboard. Replies with PlayerInfoEvent per player, then PlayerListEndEvent.</summary>
    public Task RequestPlayerListAsync() =>
        SendAsync(new PacketBuilder().Build(RconMessageType.PlayerListRequest));

    public Task RequestServerInfoAsync() =>
        SendAsync(new PacketBuilder().Build(RconMessageType.ServerInfoRequest));

    /// <summary>Request the ban list. Replies with BanInfoEvent per ban, then BanListEndEvent.</summary>
    public Task RequestBanListAsync() =>
        SendAsync(new PacketBuilder().Build(RconMessageType.BanListRequest));

    public Task SetTeamAsync(ulong steamId64, int teamId) =>
        SendAsync(new PacketBuilder().AddUInt64(steamId64).AddInt32(teamId).Build(RconMessageType.SetTeam));

    public Task ForceSpectateAsync(ulong steamId64) =>
        SendAsync(new PacketBuilder().AddUInt64(steamId64).Build(RconMessageType.ForceSpectate));

    public Task SetTeamScoreAsync(int teamId, int score) =>
        SendAsync(new PacketBuilder().AddInt32(teamId).AddInt32(score).Build(RconMessageType.SetTeamScore));

    /// <summary>
    /// Request the stored mute list. Replies with MuteInfoEvent per mute, then
    /// MuteListEndEvent. Includes players who are currently offline.
    /// </summary>
    public Task RequestMuteListAsync() =>
        SendAsync(new PacketBuilder().Build(RconMessageType.MuteListRequest));

    /// <summary>Admin text mute. Replaces the old relay AdminForceTextMute/Unmute.</summary>
    public Task MutePlayerAsync(ulong steamId64, bool mute) =>
        SendAsync(new PacketBuilder().AddUInt64(steamId64).AddInt32(mute ? 1 : 0).Build(RconMessageType.MutePlayer));

    public Task SetPauseAsync(bool paused) =>
        SendAsync(new PacketBuilder().AddInt32(paused ? 1 : 0).Build(RconMessageType.SetPause));

    public Task EndMatchAsync(int winningTeamId, string reason) =>
        SendAsync(new PacketBuilder().AddInt32(winningTeamId).AddString(reason).Build(RconMessageType.EndMatch));

    public Task SetAutoBalanceAsync(bool enabled) =>
        SendAsync(new PacketBuilder().AddInt32(enabled ? 1 : 0).Build(RconMessageType.SetAutoBalance));

    /// <summary>Game speed as a percent (100 = normal). Replaces the relay's SLOMO verb.</summary>
    public Task SetGameSpeedAsync(int speedPercent) =>
        SendAsync(new PacketBuilder().AddInt32(speedPercent).Build(RconMessageType.SetGameSpeed));

    /// <summary>Restart the current match. Replaces the relay's RESTART verb.</summary>
    /// <summary>
    /// Tournament mode on/off, with an optional ready threshold in percent (0 leaves it).
    /// Two caveats the server cannot fix and the UI must state: it only takes effect during
    /// the pre-round, and AOCGame.StartRound clears it, so it is one-shot per round.
    /// </summary>
    public Task SetTournamentAsync(bool enabled, int thresholdPercent = 0) =>
        SendAsync(new PacketBuilder().AddInt32(enabled ? 1 : 0).AddInt32(thresholdPercent)
            .Build(RconMessageType.SetTournament));

    /// <summary>
    /// true = force every player ready (vanilla's AdminReadyAll). false = clear all ready
    /// flags AND the admin override, which is what makes a re-match possible; vanilla has
    /// no equivalent of the clear.
    /// </summary>
    public Task ReadyAllAsync(bool ready) =>
        SendAsync(new PacketBuilder().AddInt32(ready ? 1 : 0).Build(RconMessageType.ReadyAll));

    /// <summary>
    /// Freeze or release a player. Uses TB's tutorial input-blocking, which is enforced
    /// CLIENT-side -- fine for holding an ordinary player still, useless against a modified
    /// client. TB say as much in a comment beside ClientScriptToggleInput.
    /// Talk stays unblocked so they can answer you.
    /// </summary>
    public Task SetFrozenAsync(ulong steamId64, bool frozen) =>
        SendAsync(new PacketBuilder().AddUInt64(steamId64).AddInt32(frozen ? 1 : 0)
            .Build(RconMessageType.SetFrozen));

    /// <summary>
    /// Move a player to another class on their current team. immediate kills the pawn so it
    /// lands now rather than on their next spawn.
    ///
    /// classIndex is EAOCClass on the server, and the two games do NOT share it:
    /// Medieval Warfare is 0 Archer, 1 Man-at-Arms, 2 Vanguard, 3 Knight, 4 Siege Engineer;
    /// Deadliest Warrior is 0 Samurai, 1 Spartan, 2 Viking, 3 Knight, 4 Ninja, 5 Pirate.
    /// GameProfile.ClassNames is the table the UI labels its buttons from -- send an index
    /// from there, not a remembered number.
    /// </summary>
    public Task SetClassAsync(ulong steamId64, int classIndex, bool immediate) =>
        SendAsync(new PacketBuilder().AddUInt64(steamId64).AddInt32(classIndex)
            .AddInt32(immediate ? 1 : 0).Build(RconMessageType.SetClass));

    /// <summary>
    /// Ask what weapons this player's class may take. Replies with LoadoutOptionEvent per
    /// weapon, then LoadoutEndEvent.
    /// </summary>
    public Task RequestLoadoutAsync(ulong steamId64) =>
        SendAsync(new PacketBuilder().AddUInt64(steamId64).Build(RconMessageType.LoadoutRequest));

    /// <summary>
    /// Set a loadout by the indices from RequestLoadoutAsync. -1 leaves a slot alone.
    /// Applies on the player's next spawn.
    /// </summary>
    public Task SetLoadoutAsync(ulong steamId64, int primary, int secondary, int tertiary) =>
        SendAsync(new PacketBuilder().AddUInt64(steamId64)
            .AddInt32(primary).AddInt32(secondary).AddInt32(tertiary)
            .Build(RconMessageType.SetLoadout));

    /// <summary>
    /// One position snapshot for every player. Replies with PlayerPosEvent per player, then
    /// PlayerPosEndEvent. Poll it for a live map.
    /// </summary>
    public Task RequestPlayerPositionsAsync() =>
        SendAsync(new PacketBuilder().Build(RconMessageType.PlayerPosRequest));

    /// <summary>
    /// Send one player to another. Both must be alive. The server tries a ring of spots
    /// around the destination, because SetLocation refuses an occupied one -- so this can
    /// legitimately fail, and the audit line says which reason.
    /// In game the same thing is !bring / !goto; over RCON neither end is "you", so both
    /// players are named explicitly.
    /// </summary>
    public Task TeleportAsync(ulong moverSteamId64, ulong destinationSteamId64) =>
        SendAsync(new PacketBuilder().AddUInt64(moverSteamId64).AddUInt64(destinationSteamId64)
            .Build(RconMessageType.Teleport));

    /// <summary>
    /// Launch a player. Power is the upward impulse, clamped 50-2000 server-side.
    /// Does no damage -- KillPlayerAsync exists for that.
    /// </summary>
    public Task SlapAsync(ulong steamId64, int power = 400) =>
        SendAsync(new PacketBuilder().AddUInt64(steamId64).AddInt32(power)
            .Build(RconMessageType.Slap));

    /// <summary>Undo Inebriate. XangMod only -- ChivAdmin had no equivalent.</summary>
    public Task SoberPlayerAsync(ulong steamId64) =>
        SendAsync(new PacketBuilder().AddUInt64(steamId64).Build(RconMessageType.SoberPlayer));

    public Task RestartMatchAsync() =>
        SendAsync(new PacketBuilder().Build(RconMessageType.RestartMatch));

    // ---------------- Internals ----------------

    public static string ComputeAuthToken(string password, string challenge)
    {
        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(password + challenge));
        return Convert.ToHexString(hash); // uppercase, 40 chars
    }

    private async Task SendAsync(byte[] frame)
    {
        var stream = _stream ?? throw new InvalidOperationException("Not connected.");
        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(frame).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }
        finally { _sendLock.Release(); }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var stream = _stream!;
        var buffer = new List<byte>();
        var chunk = new byte[8192];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false);
                if (read <= 0) break;
                for (int i = 0; i < read; i++) buffer.Add(chunk[i]);

                while (buffer.Count >= 6)
                {
                    int payloadLen = (buffer[2] << 24) | (buffer[3] << 16) | (buffer[4] << 8) | buffer[5];
                    if (payloadLen < 0 || payloadLen > 1 << 22)
                        throw new InvalidDataException($"Insane frame length {payloadLen}; protocol desync.");
                    if (buffer.Count < 6 + payloadLen) break;

                    ushort type = (ushort)((buffer[0] << 8) | buffer[1]);
                    var payload = new byte[payloadLen];
                    buffer.CopyTo(6, payload, 0, payloadLen);
                    buffer.RemoveRange(0, 6 + payloadLen);
                    await HandleFrameAsync(type, payload).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            Teardown($"Connection error: {ex.Message}");
            return;
        }
        if (!ct.IsCancellationRequested)
        {
            _authTcs?.TrySetResult(false);
            Teardown("Server closed the connection.");
        }
    }

    private async Task HandleFrameAsync(ushort type, byte[] payload)
    {
        RconEvent evt;
        try
        {
            var r = new PacketReader(payload);
            switch ((RconMessageType)type)
            {
                case RconMessageType.ServerConnect:
                    string challenge = r.ReadString();
                    evt = new ServerConnectEvent(challenge);
                    // Respond immediately: the whole password frame must arrive
                    // as one 50-byte packet or AOCRCon drops us.
                    string token = ComputeAuthToken(_password, challenge);
                    await SendAsync(new PacketBuilder().AddString(token).Build(RconMessageType.Password))
                        .ConfigureAwait(false);
                    break;

                case RconMessageType.ServerConnectSuccess:
                    evt = new AuthSucceededEvent();
                    _authTcs?.TrySetResult(true);
                    break;

                case RconMessageType.PlayerChat:
                    evt = new PlayerChatEvent(r.ReadUInt64(), r.ReadString(), r.Remaining >= 4 ? r.ReadInt32() : -1);
                    break;
                case RconMessageType.PlayerConnect:
                    evt = new PlayerConnectEvent(r.ReadUInt64(), r.ReadString());
                    break;
                case RconMessageType.PlayerDisconnect:
                    evt = new PlayerDisconnectEvent(r.ReadUInt64());
                    break;
                case RconMessageType.MapChanged:
                    evt = new MapChangedEvent(r.ReadInt32(), r.ReadString());
                    break;
                case RconMessageType.MapList:
                    evt = new MapListEntryEvent(r.ReadString());
                    break;
                case RconMessageType.TeamChanged:
                    evt = new TeamChangedEvent(r.ReadUInt64(), r.ReadInt32());
                    break;
                case RconMessageType.NameChanged:
                    evt = new NameChangedEvent(r.ReadUInt64(), r.ReadString());
                    break;
                case RconMessageType.Kill:
                    // Wiki omits it, but AOCRCon.uc also appends the weapon name.
                    evt = new KillEvent(r.ReadUInt64(), r.ReadUInt64(), r.TryReadString());
                    break;
                case RconMessageType.Suicide:
                    evt = new SuicideEvent(r.ReadUInt64());
                    break;
                case RconMessageType.RoundEnd:
                    evt = new RoundEndEvent(r.ReadInt32());
                    break;
                case RconMessageType.Ping:
                    evt = new PingEvent(r.ReadUInt64(), r.ReadInt32());
                    break;
                case RconMessageType.PingExtended:
                    evt = new PingExtendedEvent(r.ReadUInt64(), r.ReadInt32(), r.ReadInt32(),
                        r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
                    break;
                case RconMessageType.PlayerInfo:
                    evt = new PlayerInfoEvent(
                        r.ReadUInt64(), r.ReadString(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
                        r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
                        r.ReadString(), r.ReadInt32() != 0);
                    break;
                case RconMessageType.PlayerListEnd:
                    evt = new PlayerListEndEvent(r.ReadInt32());
                    break;
                case RconMessageType.ServerInfo:
                {
                    // The last two fields are optional: a server older than them simply stops
                    // the payload after NumSpectators, so read them only if there are bytes
                    // left rather than assuming a length.
                    string map = r.ReadString();
                    int num = r.ReadInt32(), max = r.ReadInt32();
                    bool begun = r.ReadInt32() != 0;
                    int specs = r.ReadInt32();
                    string mod = r.Remaining > 0 ? r.ReadString() : "";
                    string game = r.Remaining > 0 ? r.ReadString() : "";

                    // Then {count, (index, name) * count}, also optional.
                    var teams = new List<ServerTeam>();
                    if (r.Remaining > 0)
                    {
                        int teamCount = r.ReadInt32();
                        for (int i = 0; i < teamCount && r.Remaining > 0; i++)
                        {
                            int idx = r.ReadInt32();
                            string name = r.ReadString();
                            // Colour pair is optional too, so a server that sends only
                            // index+name still parses.
                            string cName = r.Remaining > 0 ? r.ReadString() : "";
                            string cHex = r.Remaining > 0 ? r.ReadString() : "";
                            teams.Add(new ServerTeam(idx, name, cName, cHex));
                        }
                    }

                    GameProfile.NoteServerInfo(mod, game);
                    GameProfile.NoteServerTeams(teams);
                    evt = new ServerInfoEvent(map, num, max, begun, specs, mod, game, teams);
                    break;
                }
                case RconMessageType.ConsoleResult:
                    evt = new ConsoleResultEvent(r.ReadString(), r.ReadString());
                    break;
                case RconMessageType.BanInfo:
                    evt = new BanInfoEvent(r.ReadUInt64(), r.ReadString(), r.ReadString(),
                        r.ReadInt32(), r.ReadString(), r.ReadString());
                    break;
                case RconMessageType.MuteInfo:
                {
                    // The trailing "stored" flag is an AdminMod 1.4 addition; older servers
                    // stop after Online and everything they send is stored by definition.
                    ulong muteId = r.ReadUInt64();
                    string muteName = r.ReadString();
                    int muteTeam = r.ReadInt32();
                    bool muteOnline = r.ReadInt32() != 0;
                    bool muteStored = r.Remaining < 4 || r.ReadInt32() != 0;
                    evt = new MuteInfoEvent(muteId, muteName, muteTeam, muteOnline, muteStored);
                    break;
                }
                case RconMessageType.MuteListEnd:
                    evt = new MuteListEndEvent(r.ReadInt32());
                    break;
                case RconMessageType.BanListEnd:
                    evt = new BanListEndEvent(r.ReadInt32());
                    break;
                case RconMessageType.AdminAudit:
                    evt = new AdminAuditEvent(r.ReadString(), r.ReadString());
                    break;
                case RconMessageType.LoadoutOption:
                    evt = new LoadoutOptionEvent(r.ReadUInt64(), r.ReadInt32(), r.ReadInt32(), r.ReadString());
                    break;
                case RconMessageType.LoadoutEnd:
                    evt = new LoadoutEndEvent(r.ReadUInt64(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
                    break;
                case RconMessageType.PlayerPos:
                    evt = new PlayerPosEvent(
                        r.ReadUInt64(), r.ReadString(), r.ReadInt32(),
                        r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
                        r.ReadInt32() != 0, r.ReadInt32());
                    break;
                case RconMessageType.PlayerPosEnd:
                    evt = new PlayerPosEndEvent(r.ReadInt32());
                    break;
                default:
                    evt = new UnknownEvent(type, payload);
                    break;
            }
        }
        catch (Exception)
        {
            evt = new UnknownEvent(type, payload);
        }
        EventReceived?.Invoke(evt);
    }

    private void SetState(RconState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    private void Teardown(string reason)
    {
        if (State == RconState.Disconnected) return;
        try { _cts?.Cancel(); } catch { }
        try { _stream?.Dispose(); } catch { }
        try { _tcp?.Close(); } catch { }
        _stream = null;
        _tcp = null;
        SetState(RconState.Disconnected);
        Disconnected?.Invoke(reason);
    }

    public void Dispose()
    {
        Teardown("Disposed.");
        _cts?.Dispose();
        _sendLock.Dispose();
    }
}

public sealed class RconAuthException : Exception
{
    public RconAuthException(string message) : base(message) { }
}
