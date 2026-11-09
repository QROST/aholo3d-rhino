using System.Threading.Channels;
using Aholo.Core;
namespace Aholo.Rhino;

/// <summary>Best-effort bounded diagnostics. Never performs disk IO on Rhino's UI thread.</summary>
internal static class GaussianLog
{
    private static readonly Channel<string> Queue = Channel.CreateBounded<string>(new BoundedChannelOptions(256)
    { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });
    public static string FilePath => Path.Combine(Environment.GetEnvironmentVariable("AHOLO_RHINO_STATE_DIR") ?? TaskStore.DefaultRoot, "logs", "gs-viewport.log");
    static GaussianLog() { _ = Task.Run(DrainAsync); }

    public static void Write(string eventName, string message)
    {
        // Callers supply timings, counts and sanitized state only, never prompts or credentials.
        Queue.Writer.TryWrite($"{DateTime.UtcNow:O} {eventName} {message.Replace('\r', ' ').Replace('\n', ' ')}");
    }

    private static async Task DrainAsync()
    {
        await foreach (string line in Queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                string path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length >= 1024 * 1024)
                    File.Move(path, path + ".previous", true);
                await File.AppendAllTextAsync(path, line + Environment.NewLine).ConfigureAwait(false);
            }
            catch (Exception) { /* Diagnostics must never prevent a redraw, import or shutdown. */ }
        }
    }
}
