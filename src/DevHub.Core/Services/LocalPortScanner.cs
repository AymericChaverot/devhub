using System.Diagnostics;
using System.Globalization;

namespace DevHub.Core.Services;

/// <summary>One local TCP listening port and the process owning it.</summary>
public sealed record LocalPortInfo(
    int Port,
    string LocalAddress,
    int ProcessId,
    string ProcessName,
    bool IsCommonDevPort);

/// <summary>
/// Inspects local listening TCP ports via netstat and resolves owning process names.
/// </summary>
public static class LocalPortScanner
{
    public static readonly IReadOnlySet<int> CommonDevPorts = new HashSet<int>
    {
        1433, 3000, 3001, 3306, 4000, 4200, 4321, 5000, 5001, 5173, 5174,
        5432, 5500, 6379, 7071, 8000, 8008, 8080, 8081, 8443, 8888, 9000,
        9090, 9229, 11434, 27017,
    };

    /// <summary>Queries active TCP listening ports on the local machine.</summary>
    public static async Task<IReadOnlyList<LocalPortInfo>> GetListeningPortsAsync(
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo("netstat")
        {
            Arguments = "-ano -p tcp",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start netstat.");

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var nameCache = new Dictionary<int, string?>();
        string? ResolveProcessName(int pid)
        {
            if (pid <= 0)
            {
                return "System";
            }

            if (nameCache.TryGetValue(pid, out var cached))
            {
                return cached;
            }

            try
            {
                using var proc = Process.GetProcessById(pid);
                var name = proc.ProcessName;
                nameCache[pid] = name;
                return name;
            }
            catch
            {
                nameCache[pid] = null;
                return null;
            }
        }

        return ParseNetstatOutput(output, ResolveProcessName);
    }

    /// <summary>
    /// Parses `netstat -ano` output into deduplicated listening TCP ports, sorted by port.
    /// Accepts listening state tokens across localized Windows builds (LISTENING, ÉCOUTE, ABHÖREN, etc.)
    /// or any TCP line where the remote endpoint port is `:0`.
    /// </summary>
    public static IReadOnlyList<LocalPortInfo> ParseNetstatOutput(
        string output,
        Func<int, string?>? processNameResolver = null)
    {
        var byPortAndPid = new Dictionary<(int Port, int Pid), LocalPortInfo>();
        var lines = output.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("TCP", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5)
            {
                continue;
            }

            var localEndpoint = parts[1];
            var foreignEndpoint = parts[2];
            var state = parts[3];
            var pidText = parts[^1];

            // Match LISTENING or localized equivalents (where foreign endpoint ends with :0).
            var isListening = state.Equals("LISTENING", StringComparison.OrdinalIgnoreCase)
                || foreignEndpoint.EndsWith(":0", StringComparison.Ordinal);
            if (!isListening)
            {
                continue;
            }

            var lastColon = localEndpoint.LastIndexOf(':');
            if (lastColon <= 0)
            {
                continue;
            }

            var address = localEndpoint[..lastColon];
            var portSlice = localEndpoint[(lastColon + 1)..];
            if (!int.TryParse(portSlice, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
                || port <= 0
                || port > 65535)
            {
                continue;
            }

            if (!int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
            {
                continue;
            }

            var key = (port, pid);
            if (byPortAndPid.ContainsKey(key))
            {
                continue;
            }

            var procName = processNameResolver?.Invoke(pid);
            if (string.IsNullOrWhiteSpace(procName))
            {
                procName = pid == 0 ? "System" : $"PID {pid}";
            }

            byPortAndPid[key] = new LocalPortInfo(
                port,
                address,
                pid,
                procName,
                CommonDevPorts.Contains(port));
        }

        return [.. byPortAndPid.Values
            .OrderByDescending(p => p.IsCommonDevPort)
            .ThenBy(p => p.Port)];
    }

    /// <summary>Terminates a process by PID.</summary>
    public static void KillProcess(int pid)
    {
        using var process = Process.GetProcessById(pid);
        process.Kill(entireProcessTree: true);
    }
}
