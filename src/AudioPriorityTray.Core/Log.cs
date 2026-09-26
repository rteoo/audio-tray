using System.Diagnostics;

namespace AudioPriorityTray.Core;

/// <summary>Append-only file log; a tray app has no console to report to.</summary>
public static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly Lock Gate = new();

    public static string? FilePath { get; set; }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:O} {level} {message}";
        Debug.WriteLine(line);
        if (FilePath is null) return;

        lock (Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var file = new FileInfo(FilePath);
            if (file.Exists && file.Length > MaxBytes)
                File.Move(FilePath, FilePath + ".old", overwrite: true);
            File.AppendAllText(FilePath, line + Environment.NewLine);
        }
    }
}
