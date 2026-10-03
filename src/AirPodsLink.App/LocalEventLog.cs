using System.Text.Json;

namespace AirPodsLink.App;

internal sealed class LocalEventLog
{
    /// <summary>
    /// Size at which the log is rotated. One previous file is kept, so the
    /// diagnostics never take more than about twice this on disk; before
    /// rotation existed the log had reached 14 MB in three weeks.
    /// </summary>
    internal const long MaxBytes = 4 * 1024 * 1024;

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
                RotateIfFull();
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

    public string PreviousFilePath => Path.ChangeExtension(FilePath, ".previous.jsonl");

    private void RotateIfFull()
    {
        var current = new FileInfo(FilePath);
        if (!current.Exists || current.Length < MaxBytes) return;
        File.Move(FilePath, PreviousFilePath, overwrite: true);
    }
}
