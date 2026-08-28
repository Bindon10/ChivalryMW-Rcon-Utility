namespace ChivRcon.Core;

/// <summary>
/// Helpers for the 8-byte player IDs used by the protocol. On the wire they are
/// the UE3 QWord (Uid.B = high dword, Uid.A = low dword) sent big-endian, which
/// for real players is simply the SteamID64 (high dword 0x01100001, low dword =
/// account id).
/// </summary>
public static class SteamId
{
    public const uint IndividualHighDword = 0x01100001;

    public static uint AccountId(ulong steamId64) => (uint)(steamId64 & 0xFFFFFFFF);

    public static bool IsIndividual(ulong steamId64) => (uint)(steamId64 >> 32) == IndividualHighDword;

    /// <summary>[U:1:accountid] representation (only meaningful for individual accounts).</summary>
    public static string ToSteam3(ulong steamId64) => $"[U:1:{AccountId(steamId64)}]";

    public static ulong FromAccountId(uint accountId) => ((ulong)IndividualHighDword << 32) | accountId;

    /// <summary>
    /// Parses "76561198...", "[U:1:12345]", "U:1:12345" or a bare account id.
    /// </summary>
    public static bool TryParse(string input, out ulong steamId64)
    {
        steamId64 = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;
        input = input.Trim().Trim('[', ']');

        if (input.Contains(':'))
        {
            string last = input[(input.LastIndexOf(':') + 1)..];
            if (uint.TryParse(last, out uint acct))
            {
                steamId64 = FromAccountId(acct);
                return true;
            }
            return false;
        }

        if (ulong.TryParse(input, out ulong id))
        {
            // Heuristic: full SteamID64s start at 0x0110000100000000.
            steamId64 = id > uint.MaxValue ? id : FromAccountId((uint)id);
            return true;
        }
        return false;
    }
}
