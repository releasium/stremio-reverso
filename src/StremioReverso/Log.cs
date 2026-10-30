using System.IO;

namespace StremioReverso;

internal static class Log
{
    static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StremioReverso", "log.txt");

    public static void Error(string where, Exception e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTime.Now:s} {where}: {e}\n");
        }
        catch { /* logging must never crash the app */ }
    }
}
