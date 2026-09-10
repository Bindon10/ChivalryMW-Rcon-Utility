using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace ChivRcon.Core;

/// <summary>
/// Persona-name lookup for a SteamID64, with no API key.
///
/// A ban taken by the console "admin kickban" stores nothing but the uid, so the ban list has
/// no name to show. steamcommunity.com/profiles/&lt;id&gt;?xml=1 is the public profile XML the
/// community site has served forever: no key, no login, and it carries the persona name even
/// for a private profile. It is not a documented API, so every failure here is non-fatal --
/// an id that will not resolve simply keeps whatever the server called it.
/// </summary>
public static class SteamNames
{
    private static readonly ConcurrentDictionary<ulong, string> Cache = new();
    private static readonly ConcurrentDictionary<ulong, byte> Failed = new();

    /// <summary>The community site is not an API. Two at a time, never a burst.</summary>
    private static readonly SemaphoreSlim Gate = new(2, 2);

    /// <summary>One list refresh should not turn into hundreds of requests.</summary>
    private const int MaxPerBatch = 100;

    private static readonly HttpClient Http = CreateClient();

    /// <summary>Off means no outbound request is ever made. Mirrored from AppSettings.</summary>
    public static bool Enabled { get; set; } = true;

    public static string ProfileUrl(ulong steamId64) => $"https://steamcommunity.com/profiles/{steamId64}";

    public static bool TryGetCached(ulong steamId64, out string name)
    {
        name = "";
        return steamId64 != 0 && Cache.TryGetValue(steamId64, out name!) && name.Length > 0;
    }

    /// <summary>Forget past failures, so a retry once the network is back can succeed.</summary>
    public static void ClearFailures() => Failed.Clear();

    /// <summary>Resolve what is not already known. True when at least one new name landed.</summary>
    public static async Task<bool> ResolveAsync(IEnumerable<ulong> steamIds, CancellationToken ct = default)
    {
        if (!Enabled) return false;

        var pending = steamIds
            .Where(id => id != 0 && !Cache.ContainsKey(id) && !Failed.ContainsKey(id))
            .Distinct()
            .Take(MaxPerBatch)
            .ToList();

        if (pending.Count == 0) return false;

        var landed = await Task.WhenAll(pending.Select(id => ResolveOneAsync(id, ct)));
        return landed.Any(ok => ok);
    }

    private static async Task<bool> ResolveOneAsync(ulong steamId64, CancellationToken ct)
    {
        try
        {
            await Gate.WaitAsync(ct);
        }
        catch (OperationCanceledException) { return false; }

        try
        {
            string xml = await Http.GetStringAsync($"{ProfileUrl(steamId64)}?xml=1", ct);
            string name = Extract(xml);

            if (name.Length == 0)
            {
                Failed[steamId64] = 0;
                return false;
            }

            Cache[steamId64] = name;
            return true;
        }
        catch
        {
            // Offline, rate limited, deleted account, markup changed -- all the same to us.
            Failed[steamId64] = 0;
            return false;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Pull the persona name out of the profile XML. Public so the test project can pin the
    /// parse against real markup without going near the network.
    /// </summary>
    public static string Extract(string xml)
    {
        if (string.IsNullOrEmpty(xml)) return "";

        var m = Regex.Match(
            xml,
            @"<steamID>\s*(?:<!\[CDATA\[(?<n>.*?)\]\]>|(?<n>.*?))\s*</steamID>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        return m.Success ? WebUtility.HtmlDecode(m.Groups["n"].Value).Trim() : "";
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ChivRcon");
        return http;
    }
}
