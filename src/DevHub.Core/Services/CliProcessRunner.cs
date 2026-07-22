using System.Diagnostics;

namespace DevHub.Core.Services;

/// <summary>
/// Runs a command line and captures its output. Same shellout shape as
/// <see cref="LibGit2GitService"/>'s private git runner, but wired through
/// OutputDataReceived/ErrorDataReceived so callers can optionally stream lines live
/// (used for the scaffold CLI command) instead of only getting the buffered result.
/// </summary>
internal static class CliProcessRunner
{
    /// <summary>
    /// Runs a command line and captures its output. The whole body — including
    /// <see cref="Process.Start(ProcessStartInfo)"/> — is offloaded via <see cref="Task.Run(Action)"/>
    /// so a caller invoking this synchronously up to its first await (e.g. a XAML property setter
    /// reacting to a selection change) never blocks the UI thread on process/PATH resolution, which
    /// can take seconds for things like Windows' "app execution alias" stubs (e.g. a missing python.exe).
    /// </summary>
    public static Task<string> RunAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        Action<string>? onOutputLine,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => RunCoreAsync(fileName, arguments, workingDirectory, onOutputLine, cancellationToken), cancellationToken);
    }

    private static async Task<string> RunCoreAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        Action<string>? onOutputLine,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {fileName}.");

        var output = new System.Text.StringBuilder();
        void OnLine(object? sender, DataReceivedEventArgs e)
        {
            if (e.Data is null)
            {
                return;
            }

            lock (output)
            {
                output.AppendLine(e.Data);
            }

            onOutputLine?.Invoke(e.Data);
        }

        process.OutputDataReceived += OnLine;
        process.ErrorDataReceived += OnLine;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        var combined = output.ToString().Trim();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(combined.Length > 0 ? combined : $"{fileName} exited with code {process.ExitCode}");
        }

        return combined;
    }

    /// <summary>Whether <paramref name="fileName"/> runs at all (used for runtime-availability checks).</summary>
    public static async Task<bool> IsAvailableAsync(string fileName, CancellationToken cancellationToken = default)
    {
        try
        {
            await RunAsync(fileName, "--version", Environment.CurrentDirectory, onOutputLine: null, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }
}
