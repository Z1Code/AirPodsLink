namespace AirPodsLink.App;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly StatusForm _statusForm = new();
    private readonly AirPodsBleWatcher _watcher = new();
    private readonly LocalEventLog _log = new();
    private readonly NormalConnectionAccelerator _accelerator;
    private readonly ToolStripMenuItem _accelerationMenuItem;
    private readonly BatteryTrayIcon _batteryIcon;
    private readonly System.Windows.Forms.Timer _staleTimer = new() { Interval = 5000 };
    private DateTimeOffset _lastNotification = DateTimeOffset.MinValue;
    private DateTimeOffset _lastSeen = DateTimeOffset.MinValue;
    private DateTimeOffset _lastEndpointCheck = DateTimeOffset.MinValue;
    private byte? _lastLidCounter;
    private volatile bool _audioReady;
    private volatile bool _accelerationEnabled = true;

    public TrayApplicationContext()
    {
        // The status form stays hidden until the user asks for it, but its
        // window handle is the only thread marshalling target available here,
        // so it has to exist before the first Bluetooth callback arrives.
        _ = _statusForm.Handle;
        _accelerator = new NormalConnectionAccelerator(_log);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Mostrar estado", null, (_, _) => ShowStatus());
        menu.Items.Add("Conectar ahora", null, async (_, _) => await ConnectNowAsync());
        _accelerationMenuItem = new ToolStripMenuItem("Acelerar por canal normal")
        {
            Checked = true,
            CheckOnClick = true
        };
        _accelerationMenuItem.CheckedChanged += (_, _) =>
            _accelerationEnabled = _accelerationMenuItem.Checked;
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
        _batteryIcon = new BatteryTrayIcon(_trayIcon);
        _batteryIcon.Update(null, false, stale: true);
        _staleTimer.Tick += (_, _) => DropStaleReading();
        _staleTimer.Start();
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
        // The advertised in-ear/in-case bits are not trustworthy on this
        // firmware (both report set at once), so connection state is never
        // inferred from them. Windows' own endpoint state decides.
        var connected = _audioReady;
        _lastSeen = DateTimeOffset.UtcNow;

        // Bluetooth callbacks arrive on a WinRT pool thread, and neither the
        // form nor NotifyIcon may be touched from there.
        RunOnUi(() =>
        {
            _statusForm.UpdateStatus(seen, connected);
            _trayIcon.Text = BuildTooltip(seen, connected);
            _batteryIcon.Update(
                seen.Advertisement.LowestPodBattery,
                seen.Advertisement.LeftCharging || seen.Advertisement.RightCharging,
                stale: false);

            if (lidChanged && DateTimeOffset.UtcNow - _lastNotification > TimeSpan.FromSeconds(8))
            {
                _lastNotification = DateTimeOffset.UtcNow;
                _trayIcon.ShowBalloonTip(
                    3500,
                    seen.Advertisement.ModelName,
                    $"I: {Format(seen.Advertisement.LeftBattery)} · D: {Format(seen.Advertisement.RightBattery)} · Estuche: {Format(seen.Advertisement.CaseBattery)}",
                    ToolTipIcon.Info);
            }
        });
    }

    private async void OnAppleProximityObserved(object? sender, EventArgs args)
    {
        var observedAt = DateTimeOffset.UtcNow;
        try
        {
            if (!_accelerationEnabled || StillConnected()) return;
            var attempt = await _accelerator.TryConnectAsync(advertisementObservedAt: observedAt);
            if (attempt is null) return;
            _audioReady = attempt.AudioReady;
            RunOnUi(() =>
            {
                _statusForm.UpdateConnectionAttempt(attempt);
                _trayIcon.Text = attempt.AudioReady ? "AirPodsLink · audio estéreo listo" : "AirPodsLink · conectando por Windows";
            });
        }
        catch (Exception error)
        {
            // This handler is void: an escaping exception would terminate the
            // process instead of reaching Program's handler.
            _ = _log.WriteAsync("acceleration-failed", new { message = error.Message });
        }
    }

    /// <summary>
    /// Whether a reconnection would be pointless. Re-checks Windows' endpoint
    /// state occasionally instead of trusting a cached flag, which is what kept
    /// the app reconnecting every few seconds.
    /// </summary>
    private bool StillConnected()
    {
        var now = DateTimeOffset.UtcNow;
        // Keep this cache short: after a quick case close/reopen, a stale true
        // used to postpone reconnection until the next BLE packet (often ~5 s).
        if (now - _lastEndpointCheck < TimeSpan.FromMilliseconds(500))
        {
            return _audioReady;
        }

        _lastEndpointCheck = now;
        _audioReady = _accelerator.IsStereoEndpointActive();
        return _audioReady;
    }

    private void DropStaleReading()
    {
        if (_lastSeen == DateTimeOffset.MinValue || DateTimeOffset.UtcNow - _lastSeen < _watcher.StaleAfter)
        {
            return;
        }

        _lastSeen = DateTimeOffset.MinValue;
        _batteryIcon.Update(null, false, stale: true);
        _trayIcon.Text = "AirPodsLink · buscando AirPods";
    }

    private void RunOnUi(Action action)
    {
        if (_statusForm.IsDisposed)
        {
            return;
        }

        try
        {
            if (_statusForm.InvokeRequired)
            {
                _statusForm.BeginInvoke(action);
            }
            else
            {
                action();
            }
        }
        catch (ObjectDisposedException)
        {
            // A Bluetooth callback can still be in flight while the tray exits.
        }
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
        _staleTimer.Stop();
        _staleTimer.Dispose();
        _watcher.AirPodsSeen -= OnAirPodsSeen;
        _watcher.AppleProximityObserved -= OnAppleProximityObserved;
        _watcher.Diagnostic -= OnWatcherDiagnostic;
        _watcher.Dispose();
        _accelerator.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        // After the tray icon is gone, no one references the generated HICON.
        _batteryIcon.Dispose();
        _statusForm.Dispose();
        base.ExitThreadCore();
    }
}
