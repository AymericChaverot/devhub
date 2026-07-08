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

    public ToolVariantViewModel(
        string toolName,
        ToolVariant variant,
        Func<ToolVariantViewModel, Task> install,
        Func<ToolVariantViewModel, Task> uninstall)
    {
        ToolName = toolName;
        Variant = variant;
        _install = install;
        _uninstall = uninstall;
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
    public partial PackageState State { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial string? InstalledVersion { get; set; }

    /// <summary>Version-picker entry text; a check mark flags installed versions.</summary>
    public string DisplayLabel => State == PackageState.Installed ? $"{Label}  ✓" : Label;

    public bool IsWorking => State is PackageState.Working or PackageState.Queued;

    public bool CanInstall => State == PackageState.NotInstalled;

    public bool CanUninstall => State == PackageState.Installed;

    public string StatusText => State switch
    {
        PackageState.Installed when InstalledVersion is not null => $"Installed · {InstalledVersion}",
        PackageState.Installed => "Installed",
        PackageState.NotInstalled => "Not installed",
        PackageState.Queued => "Queued…",
        PackageState.Working => "Working…",
        _ => "",
    };

    [RelayCommand]
    private Task InstallAsync() => _install(this);

    [RelayCommand]
    private Task UninstallAsync() => _uninstall(this);
}
