namespace DevHub.Core.Services;

/// <summary>Hardware/OS facts that never change while the app runs.</summary>
public sealed record MachineInfo(
    string MachineName,
    string UserFirstName,
    string OsName,
    string OsBuild,
    string CpuName,
    int CpuCores,
    int CpuThreads,
    IReadOnlyList<string> GpuNames,
    double TotalMemoryGb);

/// <summary>Point-in-time usage numbers; poll to refresh.</summary>
public sealed record ResourceUsage(
    double CpuUsagePercent,
    double UsedMemoryGb,
    double TotalMemoryGb,
    TimeSpan Uptime,
    IReadOnlyList<DriveUsage> Drives);

/// <summary>One fixed drive, e.g. ("C:", "Windows", 512, 380).</summary>
public sealed record DriveUsage(string Name, string? Label, double TotalGb, double UsedGb)
{
    public double UsedRatio => TotalGb > 0 ? UsedGb / TotalGb : 0;
}

/// <summary>Reads machine hardware info and resource usage (for the dashboard).</summary>
public interface ISystemInfoService
{
    /// <summary>Static hardware/OS facts. Queried once, then cached.</summary>
    Task<MachineInfo> GetMachineInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>Current memory/disk usage and uptime.</summary>
    Task<ResourceUsage> GetResourceUsageAsync(CancellationToken cancellationToken = default);
}
