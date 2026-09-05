namespace ChivRcon.App;

/// <summary>
/// What to call the gesture that opens a context menu. Avalonia raises it from a long press
/// on touch, so the desktop wording is wrong on a phone.
/// </summary>
public static class Gestures
{
    public static bool Touch => OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();

    /// <summary>Mid-sentence: "… <c>right-click</c> a player".</summary>
    public static string Secondary => Touch ? "tap and hold" : "right-click";

    /// <summary>Sentence start: "<c>Right-click</c> a player …".</summary>
    public static string SecondaryCapitalised => Touch ? "Tap and hold" : "Right-click";

    /// <summary>Column label in the command reference.</summary>
    public static string RosterMenu => Touch ? "Roster tap-and-hold" : "Roster right-click";
}
