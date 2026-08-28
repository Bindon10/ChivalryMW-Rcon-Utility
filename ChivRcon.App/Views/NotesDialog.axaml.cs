using Avalonia.Controls;
using Avalonia.Interactivity;
using ChivRcon.App.Platform;

namespace ChivRcon.App.Views;

/// <summary>
/// View and edit what we remember about one player. Deliberately small: a warning count,
/// a watchlist flag, free text, and the alias trail we collected without being asked.
/// </summary>
public partial class NotesDialog : Window
{
    private PlayerNote _note = null!;

    public NotesDialog()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            if (OperatingSystem.IsWindows())
                WindowsChrome.Attach(this, Avalonia.Media.Color.Parse("#22252C"));
        };
    }

    /// <summary>Returns true when the note was saved.</summary>
    public static async Task<bool> ShowAsync(Window owner, PlayerNotes store, ulong steamId64,
        string displayName, int addWarnings = 0)
    {
        var dlg = new NotesDialog();
        var note = store.GetOrCreate(steamId64);
        dlg._note = note;

        // Seeds the spinner only -- writing it onto the note here was the "cancel still
        // adds the warning" bug. Save_Click is the only writer.
        int seededWarnings = Math.Max(0, note.Warnings + addWarnings);

        dlg.TitleText.Text = string.IsNullOrWhiteSpace(displayName) ? "Player notes" : displayName;
        dlg.SubText.Text = $"{SteamIdText(steamId64)}  ·  first seen {note.FirstSeen:yyyy-MM-dd}"
                         + $"  ·  last seen {note.LastSeen:yyyy-MM-dd HH:mm}";
        dlg.WarnBox.Value = seededWarnings;
        dlg.WatchBox.IsChecked = note.Watch;
        dlg.NoteBox.Text = note.Note;
        dlg.NamesText.Text = note.Names.Count > 0
            ? string.Join("  ·  ", note.Names)
            : "none recorded yet";

        bool saved = await dlg.ShowDialog<bool>(owner);
        if (saved) store.Save();
        return saved;
    }

    private static string SteamIdText(ulong id) =>
        $"{ChivRcon.Core.SteamId.ToSteam3(id)}  ·  {id}";

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        _note.Warnings = (int)(WarnBox.Value ?? 0);
        _note.Watch = WatchBox.IsChecked == true;
        _note.Note = NoteBox.Text ?? "";
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
