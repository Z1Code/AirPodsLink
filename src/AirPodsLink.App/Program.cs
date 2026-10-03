namespace AirPodsLink.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            if (args.Contains("--connect-once", StringComparer.OrdinalIgnoreCase))
            {
                using var accelerator = new NormalConnectionAccelerator(new LocalEventLog());
                _ = accelerator.TryConnectAsync(force: true).GetAwaiter().GetResult();
                return;
            }

            // With "start with Windows" enabled, opening the exe by hand used to
            // start a second tray app: two icons, two scanners and every
            // connection routed twice.
            using var instance = new Mutex(initiallyOwned: true, @"Local\AirPodsLink.Tray", out var isFirst);
            if (!isFirst) return;

            Application.Run(new TrayApplicationContext());
        }
        catch (Exception error)
        {
            var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AirPodsLink");
            Directory.CreateDirectory(logDirectory);
            var logPath = Path.Combine(logDirectory, "startup-error.log");
            File.WriteAllText(logPath, $"{DateTimeOffset.Now:O}{Environment.NewLine}{error}");
            MessageBox.Show($"AirPodsLink no pudo iniciar. Se guardó el diagnóstico en:{Environment.NewLine}{logPath}", "AirPodsLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
