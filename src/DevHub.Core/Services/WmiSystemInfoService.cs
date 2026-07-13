using System.Management;
using System.Runtime.Versioning;

namespace DevHub.Core.Services;

/// <summary>Reads hardware facts from WMI; usage numbers come from WMI + DriveInfo.</summary>
[SupportedOSPlatform("windows")]
public sealed class WmiSystemInfoService : ISystemInfoService
{
    private const double BytesPerGb = 1024d * 1024 * 1024;

    private MachineInfo? _cached;

    public async Task<MachineInfo> GetMachineInfoAsync(CancellationToken cancellationToken = default)
    {
        return _cached ??= await Task.Run(ReadMachineInfo, cancellationToken);
    }

    public Task<ResourceUsage> GetResourceUsageAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(ReadResourceUsage, cancellationToken);
    }

    private static MachineInfo ReadMachineInfo()
    {
        var cpuName = "Unknown CPU";
        var cores = 0;
        var threads = Environment.ProcessorCount;
        foreach (var cpu in Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"))
        {
            cpuName = (cpu["Name"] as string)?.Trim() ?? cpuName;
            cores += Convert.ToInt32(cpu["NumberOfCores"] ?? 0);
        }

        var gpus = Query("SELECT Name FROM Win32_VideoController")
            .Select(gpu => (gpu["Name"] as string)?.Trim())
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .ToList();

        var (osName, totalMemoryGb) = ReadOsRow();

        return new MachineInfo(
            Environment.MachineName,
            ReadUserFirstName(),
            osName,
            $"Build {Environment.OSVersion.Version.Build}",
            cpuName,
            cores,
            threads,
            gpus,
            totalMemoryGb);
    }

    /// <summary>
    /// The user's given name from their Windows account display name ("Aymeric CHAVEROT"
    /// → "Aymeric"); falls back to the login name when Windows has no display name.
    /// </summary>
    private static string ReadUserFirstName()
    {
        var displayName = Query("SELECT FullName FROM Win32_UserAccount WHERE Name = '"
                + Environment.UserName.Replace("'", "''") + "' AND LocalAccount = TRUE")
            .Select(account => (account["FullName"] as string)?.Trim())
            .FirstOrDefault(name => !string.IsNullOrEmpty(name));

        var source = string.IsNullOrWhiteSpace(displayName) ? Environment.UserName : displayName;
        var first = source.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return char.ToUpper(first[0]) + first[1..];
    }

    private static ResourceUsage ReadResourceUsage()
    {
        var cpuLoads = Query("SELECT LoadPercentage FROM Win32_Processor")
            .Select(cpu => Convert.ToDouble(cpu["LoadPercentage"] ?? 0))
            .ToList();
        var cpuUsage = cpuLoads.Count > 0 ? cpuLoads.Average() : 0;

        double totalGb = 0, freeGb = 0;
        foreach (var os in Query("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem"))
        {
            // Both values are reported in kilobytes.
            totalGb = Convert.ToDouble(os["TotalVisibleMemorySize"] ?? 0) / (1024 * 1024);
            freeGb = Convert.ToDouble(os["FreePhysicalMemory"] ?? 0) / (1024 * 1024);
        }

        var drives = DriveInfo.GetDrives()
            .Where(d => d is { DriveType: DriveType.Fixed, IsReady: true })
            .Select(d => new DriveUsage(
                d.Name.TrimEnd('\\'),
                string.IsNullOrWhiteSpace(d.VolumeLabel) ? null : d.VolumeLabel,
                d.TotalSize / BytesPerGb,
                (d.TotalSize - d.TotalFreeSpace) / BytesPerGb))
            .ToList();

        return new ResourceUsage(
            cpuUsage,
            totalGb - freeGb,
            totalGb,
            TimeSpan.FromMilliseconds(Environment.TickCount64),
            drives);
    }

    private static (string OsName, double TotalMemoryGb) ReadOsRow()
    {
        foreach (var os in Query("SELECT Caption, TotalVisibleMemorySize FROM Win32_OperatingSystem"))
        {
            var caption = (os["Caption"] as string)?.Trim().Replace("Microsoft ", "") ?? "Windows";
            var totalGb = Convert.ToDouble(os["TotalVisibleMemorySize"] ?? 0) / (1024 * 1024);
            return (caption, totalGb);
        }

        return ("Windows", 0);
    }

    private static IEnumerable<ManagementBaseObject> Query(string wql)
    {
        using var searcher = new ManagementObjectSearcher(wql);
        using var results = searcher.Get();
        foreach (var item in results)
        {
            yield return item;
        }
    }
}
