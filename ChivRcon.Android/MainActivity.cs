using Android.App;
using Android.Content.PM;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using ChivRcon.App;
using AvaloniaApp = ChivRcon.App.App;

namespace ChivRcon.Android;

/// <summary>
/// Avalonia 12 bootstraps Android from the Application class, not the Activity: the activity
/// is a plain AvaloniaMainActivity and the AppBuilder is configured here.
/// </summary>
[Application(Label = "Chivalry RCON", Theme = "@style/ChivRconTheme")]
public class MainApplication : AvaloniaAndroidApplication<AvaloniaApp>
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership) : base(handle, ownership) { }

    public override void OnCreate()
    {
        base.OnCreate();

        // A launch crash on a sideloaded build is otherwise invisible without adb. Both hooks
        // write the full exception to logcat (tag ChivRcon) and to files/crash.txt.
        AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => Report("java", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report("clr", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Report("task", e.Exception);
    }

    internal static void Report(string source, Exception? ex)
    {
        string text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}: {ex}";
        try { global::Android.Util.Log.Error("ChivRcon", text); } catch { }
        try
        {
            var dir = global::Android.App.Application.Context.FilesDir?.AbsolutePath;
            if (dir is not null) File.AppendAllText(Path.Combine(dir, "crash.txt"), text + "\n\n");
        }
        catch { }
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // Saved server passwords are encrypted by the platform keystore rather than the
        // desktop build's base64, so a lost phone is not a handover of server admin.
        PwCodec.Protector = new AndroidKeystoreProtector();

        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}

[Activity(
    Label = "Chivalry RCON",
    Theme = "@style/ChivRconTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize
        | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.KeyboardHidden)]
public class MainActivity : AvaloniaMainActivity
{
}
