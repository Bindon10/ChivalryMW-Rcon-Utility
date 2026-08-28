using System.Text;
using System.Text.Json;

namespace ChivRcon.App;

/// <summary>Base64 helper shared by settings + bookmarks (obfuscation, not encryption).</summary>
public static class PwCodec
{
    public static string Decode(string b64)
    {
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
        catch { return ""; }
    }

    public static string Encode(string plain) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(plain ?? ""));
}

/// <summary>A saved server profile (bookmark).</summary>
public sealed class ServerBookmark
{
    public string Name { get; set; } = "";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 27960;
    public bool SavePassword { get; set; }
    public string PasswordB64 { get; set; } = "";

    public string Password
    {
        get => PwCodec.Decode(PasswordB64);
        set => PasswordB64 = PwCodec.Encode(value);
    }

    public override string ToString() => Name;
}

public sealed class AppSettings
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 27960;
    public bool SavePassword { get; set; }
    public string PasswordB64 { get; set; } = "";
    public bool LogToFile { get; set; } = true;
    public bool ShowKills { get; set; } = true;
    public bool AutoReconnect { get; set; } = true;

    public List<ServerBookmark> Bookmarks { get; set; } = new();
    public string LastBookmark { get; set; } = "";

    public string Password
    {
        get => PwCodec.Decode(PasswordB64);
        set => PasswordB64 = PwCodec.Encode(value);
    }
}

public static class SettingsStore
{
    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChivRconUtility");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
