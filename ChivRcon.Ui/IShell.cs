namespace ChivRcon.App;

/// <summary>
/// What a view needs from its host. Implemented by AppShell so the same views run under a
/// desktop Window and under Android's single-view lifetime.
/// </summary>
public interface IShell
{
    Task ToggleConnectAsync(string host, int port, string password);
}
