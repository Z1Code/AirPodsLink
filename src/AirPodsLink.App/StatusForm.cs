using AirPodsLink.Core;

namespace AirPodsLink.App;

internal sealed class StatusForm : Form
{
    private readonly Label _model = NewLabel(16, FontStyle.Bold);
    private readonly Label _connection = NewLabel(10, FontStyle.Regular);
    private readonly Label _left = NewLabel(12, FontStyle.Regular);
    private readonly Label _right = NewLabel(12, FontStyle.Regular);
    private readonly Label _case = NewLabel(12, FontStyle.Regular);
    private readonly Label _detail = NewLabel(9, FontStyle.Regular);
    private readonly Label _accelerator = NewLabel(9, FontStyle.Regular);

    public StatusForm()
    {
        Text = "AirPodsLink";
        ClientSize = new Size(380, 250);
        MinimumSize = new Size(396, 289);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(248, 248, 248);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(24, 20, 24, 16),
            AutoScroll = true
        };
        panel.Controls.AddRange([_model, _connection, _left, _right, _case, _detail, _accelerator]);
        Controls.Add(panel);
        ShowWaiting();

        FormClosing += (_, args) =>
        {
            if (args.CloseReason == CloseReason.UserClosing)
            {
                args.Cancel = true;
                Hide();
            }
        };
    }

    public void ShowWaiting()
    {
        _model.Text = "Buscando AirPods cercanos…";
        _connection.Text = "Abre el estuche o ponte los auriculares.";
        _left.Text = "Izquierdo: —";
        _right.Text = "Derecho: —";
        _case.Text = "Estuche: —";
        _detail.Text = "El escaneo es local y no instala ningún controlador.";
        _accelerator.Text = "Aceleración por canal normal: activa";
    }

    public void UpdateConnectionAttempt(ConnectionAttemptResult result)
    {
        _accelerator.Text = result.Error is null ? result.Summary : $"{result.Summary} · {result.Error}";
        _connection.Text = result.AudioReady
            ? "Endpoint estéreo preparado por Windows"
            : result.BluetoothLinkReady
                ? "Bluetooth conectado · esperando el endpoint estéreo"
                : "Detectados cerca · Windows aún está conectando";
    }

    public void UpdateStatus(AirPodsSeenEventArgs seen, BatteryTracker battery, bool windowsConnected)
    {
        var item = seen.Advertisement;
        var now = DateTimeOffset.UtcNow;
        _model.Text = item.ModelName;
        _connection.Text = windowsConnected ? "Conectados a Windows" : "Detectados cerca · preparando conexión";
        _left.Text = $"Izquierdo: {FormatBattery(battery.Left, now)}";
        _right.Text = $"Derecho: {FormatBattery(battery.Right, now)}";
        _case.Text = $"Estuche: {FormatBattery(battery.Case, now)}";
        _detail.Text = $"Señal {seen.Rssi} dBm · oído: {FormatEar(item)} · recibido {DateTime.Now:T}";
    }

    /// <summary>Formats a reading, saying how old it is once it stops being live.</summary>
    internal static string FormatBattery(BatteryReading? reading, DateTimeOffset now)
    {
        if (reading is not { } value) return "—";
        var age = value.Age(now);
        if (age < BatteryTracker.LiveFor)
            return $"{BatteryTracker.FormatPercent(value.Percent)} %{(value.Charging ? " ⚡" : string.Empty)}";
        return $"{BatteryTracker.FormatPercent(value.Percent)} % (hace {FormatAge(age)})";
    }

    internal static string FormatAge(TimeSpan age) => age.TotalHours >= 1
        ? $"{(int)age.TotalHours} h {age.Minutes} min"
        : $"{Math.Max(1, (int)age.TotalMinutes)} min";

    private static string FormatEar(AirPodsAdvertisement item) => (item.LeftInEar, item.RightInEar) switch
    {
        (true, true) => "ambos",
        (true, false) => "izquierdo",
        (false, true) => "derecho",
        _ => "ninguno"
    };

    private static Label NewLabel(float size, FontStyle style) => new()
    {
        AutoSize = false,
        Width = 325,
        Height = size >= 16 ? 38 : 28,
        Font = new Font("Segoe UI", size, style),
        ForeColor = Color.FromArgb(30, 30, 30)
    };
}
