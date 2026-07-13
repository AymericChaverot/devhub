using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace DevHub.Core.Services;

/// <summary>Opens an interactive terminal in a directory and runs a command in it.</summary>
public interface ITerminalService
{
    void OpenTerminal(string workingDirectory, string command);
}

/// <summary>
/// Prefers Windows Terminal (keeps the user's profile, tabs, theme); falls back to a
/// plain PowerShell window when wt.exe is not installed. The shell stays open after
/// the command exits so its output remains readable.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsTerminalService : ITerminalService
{
    public void OpenTerminal(string workingDirectory, string command)
    {
        // Bypass applies to this session only (never written to the machine policy);
        // without it, restricted machines can't run script shims like npm.ps1.
        string[] shellArguments = ["-ExecutionPolicy", "Bypass", "-NoExit", "-Command", command];

        try
        {
            var wt = new ProcessStartInfo { FileName = "wt.exe", UseShellExecute = false };
            wt.ArgumentList.Add("-d");
            wt.ArgumentList.Add(workingDirectory);
            wt.ArgumentList.Add("powershell");
            foreach (var argument in shellArguments)
            {
                wt.ArgumentList.Add(argument);
            }

            Process.Start(wt);
        }
        catch (Win32Exception)
        {
            // A console-subsystem child of a GUI app gets its own console window.
            var fallback = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
            };
            foreach (var argument in shellArguments)
            {
                fallback.ArgumentList.Add(argument);
            }

            Process.Start(fallback);
        }
    }
}
