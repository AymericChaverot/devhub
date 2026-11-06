using System.Diagnostics;

namespace DevHub.App;

/// <summary>Hands URLs, files, and folders to the Windows shell (default browser, Explorer…).</summary>
public static class ShellLauncher
{
    /// <summary>Opens a URL or file with its default handler; blank targets are ignored.</summary>
    public static void Open(string? target)
    {
        if (!string.IsNullOrWhiteSpace(target))
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
    }

    /// <summary>Opens a folder in File Explorer.</summary>
    public static void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
}
