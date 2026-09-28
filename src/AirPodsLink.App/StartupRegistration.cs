using Microsoft.Win32;

namespace AirPodsLink.App;

/// <summary>
/// Opt-in "start with Windows" entry. Without it the tray shows nothing after
/// a reboot, which reads as the battery having stopped working.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AirPodsLink";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string stored && stored.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <returns>An error to show the user, or null on success.</returns>
    public static string? Set(bool enabled)
    {
        var executable = Environment.ProcessPath;
        if (enabled && string.IsNullOrWhiteSpace(executable))
        {
            return "No se pudo determinar la ruta del ejecutable.";
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return "No se pudo abrir la clave de inicio de Windows.";
            if (enabled) key.SetValue(ValueName, $"\"{executable}\"");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
            return null;
        }
        catch (Exception error)
        {
            return error.Message;
        }
    }
}
