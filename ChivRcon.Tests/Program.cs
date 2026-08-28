// Test harness: spins up a mock AOCRCon server that faithfully reproduces the
// behaviour in AOCRCon.uc (challenge handshake, strict 50-byte password packet,
// uppercase SHA-1 compare, event stream), then drives RconClient against it.
// Exits 0 on success, 1 on any failure.

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using ChivRcon.Core;

int failures = 0;
void Check(bool cond, string what)
{
    Console.WriteLine((cond ? "PASS " : "FAIL ") + what);
    if (!cond) failures++;
}

// ---------- unit checks ----------
Check(RconClient.ComputeAuthToken("hunter2", "ABCDEFGHIJ") ==
      Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes("hunter2ABCDEFGHIJ"))),
      "auth token = SHA1(password+challenge)");
Check(RconClient.ComputeAuthToken("x", "y").All(c => char.IsDigit(c) || (c >= 'A' && c <= 'F')),
      "auth token is uppercase hex");

var frame = new PacketBuilder().AddString("hello").Build(RconMessageType.SayAll);
Check(frame.Length == 6 + 4 + 5, "frame length");
Check(frame[0] == 0 && frame[1] == 6, "message type big-endian");
Check(frame[5] == 9, "payload size big-endian");
Check(frame[9] == 5 && frame[10] == (byte)'h', "string length prefix + utf8");

ulong sid = SteamId.FromAccountId(11111111);
Check(sid == 0x0110000100000000UL + 11111111, "steamid64 from account id");
Check(SteamId.ToSteam3(sid) == "[U:1:11111111]", "steam3 formatting");
Check(SteamId.TryParse("[U:1:42]", out var p1) && p1 == SteamId.FromAccountId(42), "parse steam3");
Check(SteamId.TryParse("76561197971111111", out var p2) && p2 == 76561197971111111UL, "parse steamid64");

var qw = new PacketBuilder().AddUInt64(sid).Build(RconMessageType.Suicide);
Check(qw[6] == 0x01 && qw[7] == 0x10 && qw[8] == 0x00 && qw[9] == 0x01, "qword: Uid.B (0x01100001) first");

// ---------- mock server integration ----------
const string Password = "s3cretAdmin";
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;

var serverReceived = new List<(ushort type, byte[] payload)>();
var serverDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

_ = Task.Run(async () =>
{
    try
    {
        using var sock = await listener.AcceptTcpClientAsync();
        var ns = sock.GetStream();
        const string challenge = "KXWQZLMNOP";

        byte[] MakeFrame(RconMessageType t, Action<PacketBuilder>? build = null)
        {
            var b = new PacketBuilder();
            build?.Invoke(b);
            return b.Build(t);
        }

        await ns.WriteAsync(MakeFrame(RconMessageType.ServerConnect, b => b.AddString(challenge)));

        // AOCRCon: the very first read must be exactly a 50-byte password packet.
        var pw = new byte[50];
        int got = 0;
        while (got < 50)
        {
            int n = await ns.ReadAsync(pw.AsMemory(got, 50 - got));
            if (n <= 0) throw new Exception("client closed during auth");
            got += n;
        }
        Check(pw[0] == 0 && pw[1] == 2, "server saw PASSWORD type");
        int plen = (pw[2] << 24) | (pw[3] << 16) | (pw[4] << 8) | pw[5];
        Check(plen == 44, "password payload is 44 bytes (50 total)");
        string token = Encoding.UTF8.GetString(pw, 10, 40);
        string expected = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(Password + challenge)));
        Check(token == expected, "server accepted uppercase SHA1 token");

        await ns.WriteAsync(MakeFrame(RconMessageType.ServerConnectSuccess));

        // Simulate the initial burst: players, map, map list - deliberately
        // written in fragmented + coalesced chunks to test reassembly.
        ulong id1 = SteamId.FromAccountId(1001), id2 = SteamId.FromAccountId(2002);
        var burst = new List<byte>();
        burst.AddRange(MakeFrame(RconMessageType.PlayerConnect, b => b.AddUInt64(id1).AddString("Sir Töst")));
        burst.AddRange(MakeFrame(RconMessageType.PlayerConnect, b => b.AddUInt64(id2).AddString("PeasantSlayer")));
        burst.AddRange(MakeFrame(RconMessageType.TeamChanged, b => b.AddUInt64(id1).AddInt32(0)));
        burst.AddRange(MakeFrame(RconMessageType.TeamChanged, b => b.AddUInt64(id2).AddInt32(1)));
        burst.AddRange(MakeFrame(RconMessageType.MapChanged, b => b.AddInt32(3).AddString("AOCTO-Stoneshill_P")));
        burst.AddRange(MakeFrame(RconMessageType.MapList, b => b.AddString("AOCTO-Stoneshill_P")));
        burst.AddRange(MakeFrame(RconMessageType.MapList, b => b.AddString("AOCLTS-Arena3_P")));
        burst.AddRange(MakeFrame(RconMessageType.Kill, b => b.AddUInt64(id1).AddUInt64(id2).AddString("Messer")));
        burst.AddRange(MakeFrame(RconMessageType.PlayerChat, b => b.AddUInt64(id2).AddString("gg wp").AddInt32(1)));
        burst.AddRange(MakeFrame(RconMessageType.Ping, b => b.AddUInt64(id1).AddInt32(47)));
        burst.AddRange(MakeFrame(RconMessageType.RoundEnd, b => b.AddInt32(0)));

        var all = burst.ToArray();
        // send in awkward chunk sizes (7 bytes at a time for the first 50, then the rest)
        for (int off = 0; off < Math.Min(50, all.Length); off += 7)
            await ns.WriteAsync(all.AsMemory(off, Math.Min(7, Math.Min(50, all.Length) - off)));
        if (all.Length > 50)
            await ns.WriteAsync(all.AsMemory(50));

        // Now read commands from the client until it disconnects.
        var buf = new List<byte>();
        var chunk = new byte[4096];
        while (serverReceived.Count < 4)
        {
            int n = await ns.ReadAsync(chunk);
            if (n <= 0) break;
            for (int i = 0; i < n; i++) buf.Add(chunk[i]);
            while (buf.Count >= 6)
            {
                int len = (buf[2] << 24) | (buf[3] << 16) | (buf[4] << 8) | buf[5];
                if (buf.Count < 6 + len) break;
                ushort t = (ushort)((buf[0] << 8) | buf[1]);
                serverReceived.Add((t, buf.Skip(6).Take(len).ToArray()));
                buf.RemoveRange(0, 6 + len);
            }
        }
        serverDone.TrySetResult(true);
    }
    catch (Exception ex)
    {
        Console.WriteLine("MOCK SERVER ERROR: " + ex);
        serverDone.TrySetResult(false);
    }
});

var events = new List<RconEvent>();
var tracker = new PlayerTracker();
var client = new RconClient();
client.EventReceived += e => { lock (events) { events.Add(e); tracker.Apply(e); } };

await client.ConnectAsync("127.0.0.1", port, Password, TimeSpan.FromSeconds(10));
Check(client.State == RconState.Connected, "client authenticated");

// give the burst a moment to arrive
for (int i = 0; i < 100; i++)
{
    lock (events) { if (events.OfType<RoundEndEvent>().Any()) break; }
    await Task.Delay(50);
}

lock (events)
{
    Check(events.OfType<PlayerConnectEvent>().Count() == 2, "2 player connects parsed");
    Check(events.OfType<PlayerConnectEvent>().First().Name == "Sir Töst", "utf-8 name decoded");
    var kill = events.OfType<KillEvent>().FirstOrDefault();
    Check(kill is not null && kill.WeaponName == "Messer", "kill event with weapon name");
    var chat = events.OfType<PlayerChatEvent>().FirstOrDefault();
    Check(chat is not null && chat.Message == "gg wp" && chat.TeamId == 1, "chat parsed");
    Check(events.OfType<MapChangedEvent>().FirstOrDefault()?.MapName == "AOCTO-Stoneshill_P", "map changed parsed");
    Check(events.OfType<RoundEndEvent>().FirstOrDefault()?.WinningTeamId == 0, "round end parsed");
}

Check(tracker.Players.Count == 2, "tracker has 2 players");
Check(tracker.Find(SteamId.FromAccountId(1001))?.TeamId == 0, "tracker team agatha");
Check(tracker.Find(SteamId.FromAccountId(1001))?.Ping == 47, "tracker ping updated");
Check(tracker.MapList.Count == 2, "tracker map list");
Check(tracker.CurrentMap == "AOCTO-Stoneshill_P", "tracker current map");

// send commands and verify the server decodes them
ulong target = SteamId.FromAccountId(2002);
await client.SayAllAsync("Server restarting soon");
await client.KickAsync(target, "AFK");
await client.TempBanAsync(target, "TK", 3600);
await client.ChangeMapAsync("AOCLTS-Arena3_P");

Check(await serverDone.Task.WaitAsync(TimeSpan.FromSeconds(10)), "mock server completed");

Check(serverReceived.Count >= 4, "server received 4 commands");
if (serverReceived.Count >= 4)
{
    Check(serverReceived[0].type == 6 &&
          new PacketReader(serverReceived[0].payload).ReadString() == "Server restarting soon", "SAY_ALL round-trip");

    var kr = new PacketReader(serverReceived[1].payload);
    Check(serverReceived[1].type == 17 && kr.ReadUInt64() == target && kr.ReadString() == "AFK", "KICK round-trip");

    var tr = new PacketReader(serverReceived[2].payload);
    Check(serverReceived[2].type == 18 && tr.ReadUInt64() == target && tr.ReadString() == "TK" && tr.ReadInt32() == 3600,
          "TEMP_BAN round-trip");

    Check(serverReceived[3].type == 11 &&
          new PacketReader(serverReceived[3].payload).ReadString() == "AOCLTS-Arena3_P", "CHANGE_MAP round-trip");
}

client.Disconnect();
listener.Stop();


// The relay text-protocol tests were removed with RelayClient: BangModRCon serves
// console commands, mute and game speed natively over RCON (opcodes 28, 42, 46).

Console.WriteLine(failures == 0 ? "\nALL TESTS PASSED" : $"\n{failures} FAILURES");
return failures == 0 ? 0 : 1;
