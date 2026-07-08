using System.Diagnostics;
using System.Text;

namespace DevHub.Core.Services;

public sealed class WingetCliPackageService : IPackageService
{
    public async Task<IReadOnlyDictionary<string, string>> GetInstalledVersionsAsync(
        CancellationToken cancellationToken = default)
    {
        var (_, output) = await RunWingetAsync(
            ["list", "--accept-source-agreements", "--disable-interactivity"],
            cancellationToken);
        var entries = WingetListParser.ParseEntries(output);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries.Where(e => !WingetListParser.IsSyntheticId(e.Id)))
        {
            result[entry.Id] = entry.Version;
        }

        // Packages installed outside winget (e.g. the official Node.js installer) get a
        // synthetic ARP/MSIX id in bulk-listing mode even though `--id --exact` resolves
        // them fine; fall back to matching by display name for those.
        foreach (var entry in entries.Where(e => WingetListParser.IsSyntheticId(e.Id)))
        {
            result.TryAdd(IPackageService.NameKey(entry.Name), entry.Version);
        }

        return result;
    }

    public async Task<string?> GetInstalledVersionAsync(string wingetId, CancellationToken cancellationToken = default)
    {
        var (exitCode, output) = await RunWingetAsync(
            ["list", "--id", wingetId, "--exact", "--accept-source-agreements", "--disable-interactivity"],
            cancellationToken);

        // winget exits non-zero when no package matches.
        if (exitCode != 0)
        {
            return null;
        }

        return WingetListParser.Parse(output).TryGetValue(wingetId, out var version) ? version : null;
    }

    public async Task<PackageOperationResult> InstallAsync(string wingetId, CancellationToken cancellationToken = default)
    {
        var (exitCode, output) = await RunWingetAsync(
            [
                "install", "--id", wingetId, "--exact", "--silent",
                "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity",
            ],
            cancellationToken);
        return ToResult(exitCode, output);
    }

    public async Task<PackageOperationResult> UninstallAsync(string wingetId, CancellationToken cancellationToken = default)
    {
        var (exitCode, output) = await RunWingetAsync(
            ["uninstall", "--id", wingetId, "--exact", "--silent", "--disable-interactivity"],
            cancellationToken);
        return ToResult(exitCode, output);
    }

    private static PackageOperationResult ToResult(int exitCode, string output)
    {
        if (exitCode == 0)
        {
            return new PackageOperationResult(true);
        }

        // The last non-empty line of winget output is the human-readable error.
        var message = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(l => l.Length > 0 && !l.All(c => c is '-' or '\\' or '|' or '/'));
        return new PackageOperationResult(false, message ?? $"winget exited with code 0x{exitCode:X}");
    }

    private static async Task<(int ExitCode, string Output)> RunWingetAsync(
        IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "winget",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, await stdout + "\n" + await stderr);
    }
}
