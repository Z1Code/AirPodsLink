using System.Text.Json;

namespace AirPodsLink.App;

internal sealed class LocalEventLog
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AirPodsLink",
        "connection-events.jsonl");

    public async Task WriteAsync(string eventName, object details)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);
            var line = JsonSerializer.Serialize(new
            {
                timestamp = DateTimeOffset.Now,
                eventName,
                details
            });
            await _gate.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(FilePath, line + Environment.NewLine);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch
        {
            // Diagnostics must never prevent Bluetooth operation.
        }
    }
}
