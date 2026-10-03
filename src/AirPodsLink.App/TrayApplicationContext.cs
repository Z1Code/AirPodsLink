using AirPodsLink.Core;

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
    // Mutated only on the UI thread, together with what it feeds.
    private readonly BatteryTracker _battery = new();
    private readonly System.Windows.Forms.Timer _staleTimer = new() { Interval = 5000 };
    private readonly AirPodsEndpointMonitor _endpointMonitor = new();
    private string? _lastRoutedEndpointId;
    private DateTimeOffset _lastRouted = DateTimeOffset.MinValue;
    private DateTimeOffset _lastNotification = DateTimeOffset.MinValue;
    private long _lastSeenTicks = DateTimeOffset.MinValue.UtcTicks;
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

        var startupMenuItem = new ToolStripMenuItem("Iniciar con Windows")
        {
            Checked = StartupRegistration.IsEnabled(),
            CheckOnClick = true
        };
        menu.Items.Add(startupMenuItem);
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
        // Attached once the tray icon exists, since the handler reports through it.
        startupMenuItem.CheckedChanged += (_, _) =>
        {
            var error = StartupRegistration.Set(startupMenuItem.Checked);
            if (error is not null)
            {
                _trayIcon.ShowBalloonTip(4000, "AirPodsLink", error, ToolTipIcon.Warning);
            }
        };
        _batteryIcon = new BatteryTrayIcon(_trayIcon);
        _batteryIcon.Update(null, false, BatteryFreshness.Unknown);
        _staleTimer.Tick += (_, _) => DropStaleReading();
        _staleTimer.Start();
        _watcher.AirPodsSeen += OnAirPodsSeen;
        _watcher.AppleProximityObserved += OnAppleProximityObserved;
        _watcher.Diagnostic += OnWatcherDiagnostic;
        _endpointMonitor.StereoEndpointActivated += OnStereoEndpointActivated;
        _endpointMonitor.Diagnostic += OnWatcherDiagnostic;
        _endpointMonitor.Start();
        _ = _log.WriteAsync("application-started", new { version = Application.ProductVersion });

        // Windows may have connected the AirPods before this process existed,
        // in which case no state change will ever arrive.
        var alreadyActive = _endpointMonitor.FindActiveStereoEndpointId();
        if (alreadyActive is not null) OnStereoEndpointActivated(this, alreadyActive);

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
        Interlocked.Exchange(ref _lastSeenTicks, DateTimeOffset.UtcNow.UtcTicks);

        // Bluetooth callbacks arrive on a WinRT pool thread, and neither the
        // form nor NotifyIcon may be touched from there.
        RunOnUi(() =>
        {
            _battery.Observe(seen.Advertisement, DateTimeOffset.UtcNow);
            _statusForm.UpdateStatus(seen, _battery, connected);
            RefreshBattery(connected ? "conectados" : "detectados");

            if (lidChanged && DateTimeOffset.UtcNow - _lastNotification > TimeSpan.FromSeconds(8))
            {
                _lastNotification = DateTimeOffset.UtcNow;
                _trayIcon.ShowBalloonTip(
                    3500,
                    seen.Advertisement.ModelName,
                    $"I: {Format(_battery.Left)} · D: {Format(_battery.Right)} · Estuche: {Format(_battery.Case)}",
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
        if (!_audioReady)
        {
            // Forget the routing so a quick reconnection is routed again
            // instead of being swallowed by the duplicate-notification guard.
            _lastRoutedEndpointId = null;
        }
        return _audioReady;
    }

    /// <summary>
    /// Hands the AirPods to Windows as the default output as soon as they are
    /// playable. This is what lets FxSound adopt them: it follows the default
    /// device and then restores itself as the default.
    /// </summary>
    private async void OnStereoEndpointActivated(object? sender, string endpointId)
    {
        var now = DateTimeOffset.UtcNow;
        if (_lastRoutedEndpointId == endpointId && now - _lastRouted < TimeSpan.FromSeconds(10))
        {
            // Several notifications describe one arrival; route it once.
            return;
        }

        _lastRoutedEndpointId = endpointId;
        _lastRouted = now;
        _audioReady = true;

        try
        {
            var error = await _accelerator.RouteToStereoEndpointAsync(endpointId);
            RunOnUi(() => _trayIcon.Text = error is null
                ? "AirPodsLink · AirPods como salida predeterminada"
                : "AirPodsLink · AirPods conectados; no se pudo cambiar la salida");
        }
        catch (Exception error)
        {
            // Raised on a thread pool thread: an escape would end the process.
            _ = _log.WriteAsync("stereo-endpoint-routing-failed", new { endpointId, message = error.Message });
        }
    }

    private void DropStaleReading()
    {
        if (_battery.LowestPod is not null && !_battery.IsPodReadingLive(DateTimeOffset.UtcNow))
        {
            // Worn pods keep advertising without a charge; age the figure anyway.
            RefreshBattery(_audioReady ? "conectados" : "detectados");
        }

        var lastSeenTicks = Interlocked.Read(ref _lastSeenTicks);
        if (lastSeenTicks == DateTimeOffset.MinValue.UtcTicks ||
            DateTimeOffset.UtcNow - new DateTimeOffset(lastSeenTicks, TimeSpan.Zero) < _watcher.StaleAfter)
        {
            return;
        }

        Interlocked.Exchange(ref _lastSeenTicks, DateTimeOffset.MinValue.UtcTicks);
        // Keep the last known charge on screen, greyed, instead of wiping it:
        // "80, an hour ago" is more useful than "--".
        RefreshBattery("buscando AirPods");
    }

    /// <summary>Repaints the icon and tooltip from the accumulated readings.</summary>
    private void RefreshBattery(string state)
    {
        var now = DateTimeOffset.UtcNow;
        var freshness = _battery.LowestPod is null ? BatteryFreshness.Unknown
            : _battery.IsPodReadingLive(now) ? BatteryFreshness.Live
            : BatteryFreshness.Remembered;
        _batteryIcon.Update(_battery.LowestPod, _battery.PodCharging, freshness);

        var pods = $"I {Format(_battery.Left)} D {Format(_battery.Right)}";
        if (freshness == BatteryFreshness.Remembered && _battery.LowestPodReading is { } lowest)
            pods += $" (hace {StatusForm.FormatAge(lowest.Age(now))})";
        var text = $"AirPodsLink · {state} · {pods}";
        _trayIcon.Text = text.Length <= 127 ? text : text[..127];
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

    private static string Format(BatteryReading? reading) =>
        reading is { } value ? $"{BatteryTracker.FormatPercent(value.Percent)} %" : "—";

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
        RunOnUi(() => _statusForm.UpdateConnectionAttempt(result));
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
        _endpointMonitor.StereoEndpointActivated -= OnStereoEndpointActivated;
        _endpointMonitor.Diagnostic -= OnWatcherDiagnostic;
        _endpointMonitor.Dispose();
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
