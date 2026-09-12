namespace AirPodsLink.App;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly StatusForm _statusForm = new();
    private readonly AirPodsBleWatcher _watcher = new();
    private readonly LocalEventLog _log = new();
    private readonly NormalConnectionAccelerator _accelerator;
    private readonly ToolStripMenuItem _accelerationMenuItem;
    private DateTimeOffset _lastNotification = DateTimeOffset.MinValue;
    private byte? _lastLidCounter;
    private bool _audioReady;

    public TrayApplicationContext()
    {
        _accelerator = new NormalConnectionAccelerator(_log);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Mostrar estado", null, (_, _) => ShowStatus());
        menu.Items.Add("Conectar ahora", null, async (_, _) => await ConnectNowAsync());
        _accelerationMenuItem = new ToolStripMenuItem("Acelerar por canal normal")
        {
            Checked = true,
            CheckOnClick = true
        };
        menu.Items.Add(_accelerationMenuItem);
        menu.Items.Add("Abrir registro", null, (_, _) => OpenLog());
        menu.Items.Add("Abrir configuración Bluetooth", null, (_, _) => OpenBluetoothSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => ExitThread());

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = "AirPodsLink · buscando AirPods",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => ShowStatus();
        _watcher.AirPodsSeen += OnAirPodsSeen;
        _watcher.AppleProximityObserved += OnAppleProximityObserved;
        _watcher.Diagnostic += OnWatcherDiagnostic;
        _ = _log.WriteAsync("application-started", new { version = Application.ProductVersion });

        try
        {
            _watcher.Start();
        }
        catch (Exception error)
        {
            _trayIcon.ShowBalloonTip(5000, "AirPodsLink", $"No se pudo iniciar Bluetooth LE: {error.Message}", ToolTipIcon.Error);
        }
    }

    private void OnAirPodsSeen(object? sender, AirPodsSeenEventArgs seen)
    {
        var lidChanged = _lastLidCounter != seen.Advertisement.LidCounter;
        _lastLidCounter = seen.Advertisement.LidCounter;
        var connected = false;
        ConnectionAttemptResult? attempt = null;
        if (seen.Advertisement.BothInCase)
        {
            _audioReady = false;
        }
        connected = _audioReady;

        _statusForm.BeginInvoke(() => _statusForm.UpdateStatus(seen, connected));
        if (attempt is not null)
        {
            _statusForm.BeginInvoke(() => _statusForm.UpdateConnectionAttempt(attempt));
        }
        _trayIcon.Text = BuildTooltip(seen, connected);

        if (lidChanged && DateTimeOffset.UtcNow - _lastNotification > TimeSpan.FromSeconds(8))
        {
            _lastNotification = DateTimeOffset.UtcNow;
            _trayIcon.ShowBalloonTip(
                3500,
                seen.Advertisement.ModelName,
                $"I: {Format(seen.Advertisement.LeftBattery)} · D: {Format(seen.Advertisement.RightBattery)} · Estuche: {Format(seen.Advertisement.CaseBattery)}",
                ToolTipIcon.Info);
        }
    }

    private async void OnAppleProximityObserved(object? sender, EventArgs args)
    {
        if (!_accelerationMenuItem.Checked || _audioReady) return;
        var attempt = await _accelerator.TryConnectAsync();
        if (attempt is null) return;
        _audioReady = attempt.AudioReady;
        _statusForm.BeginInvoke(() => _statusForm.UpdateConnectionAttempt(attempt));
        _trayIcon.Text = attempt.AudioReady ? "AirPodsLink · audio estéreo listo" : "AirPodsLink · conectando por Windows";
    }

    private void OnWatcherDiagnostic(object? sender, string message) =>
        _ = _log.WriteAsync("ble-scanner", new { message });

    private static string BuildTooltip(AirPodsSeenEventArgs seen, bool connected)
    {
        var text = $"AirPodsLink · {(connected ? "conectados" : "detectados")} · I {Format(seen.Advertisement.LeftBattery)} D {Format(seen.Advertisement.RightBattery)}";
        return text.Length <= 127 ? text : text[..127];
    }

    private static string Format(int? value) => value is null ? "—" : $"{value}%";

    private void ShowStatus()
    {
        if (!_statusForm.Visible) _statusForm.Show();
        if (_statusForm.WindowState == FormWindowState.Minimized) _statusForm.WindowState = FormWindowState.Normal;
        _statusForm.Activate();
    }

    private static void OpenBluetoothSettings() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });

    private async Task ConnectNowAsync()
    {
        var result = await _accelerator.TryConnectAsync(force: true);
        if (result is null) return;
        _statusForm.UpdateConnectionAttempt(result);
        _audioReady = result.AudioReady;
        _trayIcon.ShowBalloonTip(3000, "AirPodsLink", result.Summary, result.BluetoothLinkReady ? ToolTipIcon.Info : ToolTipIcon.Warning);
        ShowStatus();
    }

    private void OpenLog()
    {
        var directory = Path.GetDirectoryName(_log.FilePath)!;
        Directory.CreateDirectory(directory);
        if (!File.Exists(_log.FilePath)) File.WriteAllText(_log.FilePath, string.Empty);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_log.FilePath) { UseShellExecute = true });
    }

    protected override void ExitThreadCore()
    {
        _watcher.AirPodsSeen -= OnAirPodsSeen;
        _watcher.AppleProximityObserved -= OnAppleProximityObserved;
        _watcher.Diagnostic -= OnWatcherDiagnostic;
        _watcher.Dispose();
        _accelerator.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _statusForm.Dispose();
        base.ExitThreadCore();
    }
}
