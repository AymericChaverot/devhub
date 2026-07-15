using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevHub.Core.Models;

namespace DevHub.App.ViewModels;

public enum PackageState
{
    Unknown,
    NotInstalled,
    Queued,
    Working,
    Installed,
}

/// <summary>One installable version of a tool (a single winget package).</summary>
public sealed partial class ToolVariantViewModel : ObservableObject
{
    private readonly Func<ToolVariantViewModel, Task> _install;
    private readonly Func<ToolVariantViewModel, Task> _uninstall;
    private readonly Func<ToolVariantViewModel, Task> _update;

    public ToolVariantViewModel(
        string toolName,
        ToolVariant variant,
        Func<ToolVariantViewModel, Task> install,
        Func<ToolVariantViewModel, Task> uninstall,
        Func<ToolVariantViewModel, Task> update)
    {
        ToolName = toolName;
        Variant = variant;
        _install = install;
        _uninstall = uninstall;
        _update = update;
    }

    public ToolVariant Variant { get; }

    public string ToolName { get; }

    public string Label => Variant.Label;

    public string WingetId => Variant.WingetId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(DisplayLabel))]
    [NotifyPropertyChangedFor(nameof(IsWorking))]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    [NotifyPropertyChangedFor(nameof(CanUninstall))]
    [NotifyPropertyChangedFor(nameof(CanUpdate))]
    public partial PackageState State { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial string? InstalledVersion { get; set; }

    /// <summary>Upgrade version winget advertises, or null when the package is up to date.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpdate))]
    [NotifyPropertyChangedFor(nameof(UpdateHintText))]
    public partial string? AvailableVersion { get; set; }

    /// <summary>Version-picker entry text; a check mark flags installed versions.</summary>
    public string DisplayLabel => State == PackageState.Installed ? $"{Label}  ✓" : Label;

    public bool IsWorking => State is PackageState.Working or PackageState.Queued;

    public bool CanInstall => State == PackageState.NotInstalled;

    public bool CanUninstall => State == PackageState.Installed;

    public bool CanUpdate => State == PackageState.Installed && AvailableVersion is not null;

    /// <summary>Caption shown under the status line when an update is available.</summary>
    public string UpdateHintText => AvailableVersion is null
        ? ""
        : Loc.F("Env_UpdateHint", "Update available: {0}", AvailableVersion);

    public string StatusText => State switch
    {
        PackageState.Installed when InstalledVersion is not null =>
            $"{Loc.T("Env_StatusInstalled", "Installed")} · {InstalledVersion}",
        PackageState.Installed => Loc.T("Env_StatusInstalled", "Installed"),
        PackageState.NotInstalled => Loc.T("Env_StatusNotInstalled", "Not installed"),
        PackageState.Queued => Loc.T("Env_StatusQueued", "Queued…"),
        PackageState.Working => Loc.T("Env_StatusWorking", "Working…"),
        _ => "",
    };

    [RelayCommand]
    private Task InstallAsync() => _install(this);

    [RelayCommand]
    private Task UninstallAsync() => _uninstall(this);

    [RelayCommand]
    private Task UpdateAsync() => _update(this);
}
