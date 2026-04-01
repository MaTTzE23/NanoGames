namespace FiestaLauncher.Models
{
    public enum LauncherSessionState
    {
        LoggedOut,
        Authenticating,
        LoggedIn,
        Starting,
        TrayConnected,
        GameRunning,
        Revoked
    }
}
