using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace DevHub.Core.Services;

/// <summary>Metadata for a terminal shell profile supported by DevHub.</summary>
public sealed record TerminalShellOption(string Id, string DisplayName);

/// <summary>Opens an interactive terminal in a directory and optionally runs a command in it.</summary>
public interface ITerminalService
{
    void OpenTerminal(string workingDirectory, string command = "", string? shell = null);
}

/// <summary>
/// Prefers Windows Terminal (keeps the user's profile, tabs, theme); falls back to a
/// plain shell window when wt.exe is not installed. The shell stays open after
/// the command exits so its output remains readable.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsTerminalService : ITerminalService
{
    public static IReadOnlyList<TerminalShellOption> SupportedShells { get; } =
    [
        new("powershell", "Windows PowerShell"),
        new("pwsh", "PowerShell 7 (pwsh)"),
        new("cmd", "Command Prompt (cmd)"),
        new("git-bash", "Git Bash"),
        new("wsl", "WSL"),
    ];

    public void OpenTerminal(string workingDirectory, string command = "", string? shell = null)
    {
        var (shellExecutable, shellArguments) = BuildShellInvocation(workingDirectory, command, shell);

        try
        {
            var wt = new ProcessStartInfo { FileName = "wt.exe", UseShellExecute = false };
            wt.ArgumentList.Add("-d");
            wt.ArgumentList.Add(workingDirectory);
            wt.ArgumentList.Add(shellExecutable);
            foreach (var argument in shellArguments)
            {
                wt.ArgumentList.Add(argument);
            }

            Process.Start(wt);
        }
        catch (Win32Exception)
        {
            LaunchStandaloneShell(workingDirectory, shellExecutable, shellArguments, command);
        }
    }

    internal static (string Executable, string[] Arguments) BuildShellInvocation(
        string workingDirectory,
        string command,
        string? shell)
    {
        var normalized = (shell ?? "powershell").Trim().ToLowerInvariant();
        var hasCommand = !string.IsNullOrWhiteSpace(command);

        return normalized switch
        {
            "pwsh" => (
                ResolvePwshExecutable(),
                hasCommand
                    ? ["-ExecutionPolicy", "Bypass", "-NoExit", "-Command", command]
                    : ["-ExecutionPolicy", "Bypass", "-NoExit"]),
            "cmd" => (
                "cmd.exe",
                hasCommand ? ["/K", command] : ["/K"]),
            "git-bash" => (
                ResolveGitBashExecutable(),
                hasCommand
                    ? ["--login", "-i", "-c", $"{command}; exec bash"]
                    : ["--login", "-i"]),
            "wsl" => (
                "wsl.exe",
                hasCommand
                    ? ["--cd", workingDirectory, "--", "sh", "-c", $"{command}; exec $SHELL"]
                    : ["--cd", workingDirectory]),
            _ => (
                "powershell.exe",
                hasCommand
                    ? ["-ExecutionPolicy", "Bypass", "-NoExit", "-Command", command]
                    : ["-ExecutionPolicy", "Bypass", "-NoExit"]),
        };
    }

    private static string ResolvePwshExecutable()
    {
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "pwsh.exe"),
        ];

        return candidates.FirstOrDefault(File.Exists) ?? "pwsh.exe";
    }

    private static string ResolveGitBashExecutable()
    {
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git", "bin", "bash.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "bin", "bash.exe"),
        ];

        return candidates.FirstOrDefault(File.Exists) ?? "bash.exe";
    }

    private static void LaunchStandaloneShell(
        string workingDirectory,
        string shellExecutable,
        string[] shellArguments,
        string command)
    {
        try
        {
            var fallback = new ProcessStartInfo
            {
                FileName = shellExecutable,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
            };
            foreach (var argument in shellArguments)
            {
                fallback.ArgumentList.Add(argument);
            }

            Process.Start(fallback);
        }
        catch (Win32Exception) when (!string.Equals(shellExecutable, "powershell.exe", StringComparison.OrdinalIgnoreCase))
        {
            // Ultimate fallback if the selected shell binary isn't installed on this machine.
            var ps = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
            };
            ps.ArgumentList.Add("-ExecutionPolicy");
            ps.ArgumentList.Add("Bypass");
            ps.ArgumentList.Add("-NoExit");
            if (!string.IsNullOrWhiteSpace(command))
            {
                ps.ArgumentList.Add("-Command");
                ps.ArgumentList.Add(command);
            }

            Process.Start(ps);
        }
    }
}
