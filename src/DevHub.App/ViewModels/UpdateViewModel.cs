using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevHub.Core.Services;
using Serilog;

namespace DevHub.App.ViewModels;

/// <summary>
/// Update state shared by the dashboard banner and the Settings "About" card.
/// Checked silently at startup; "Install" downloads the MSIX and hands it to
/// Windows App Installer (which prompts to close the running app).
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    private readonly IUpdateService _updates;

    public UpdateViewModel(IUpdateService updates)
    {
        _updates = updates;
        StatusText = "";
    }

    public Version CurrentVersion { get; } = GetCurrentVersion();

    public string CurrentVersionText => $"Version {CurrentVersion.ToString(3)}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    [NotifyPropertyChangedFor(nameof(BannerTitle))]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    [NotifyPropertyChangedFor(nameof(CanInstallNow))]
    public partial UpdateInfo? Available { get; set; }

    public bool HasUpdate => Available is not null;

    /// <summary>The release ships an installer asset we can download directly.</summary>
    public bool CanInstall => Available is { AssetUrl: not null };

    public string BannerTitle =>
        Available is null ? "" : $"DevHub {Available.Version.ToString(3)} is available";

    [ObservableProperty]
    public partial bool IsChecking { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallNow))]
    public partial bool IsDownloading { get; set; }

    /// <summary>Install button enabled: an installer asset exists and no download is running.</summary>
    public bool CanInstallNow => CanInstall && !IsDownloading;

    [ObservableProperty]
    public partial double DownloadPercent { get; set; }

    /// <summary>Feedback line under the Settings button ("You're up to date", errors...).</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; }

    /// <summary>Silent startup check: only surfaces something when an update exists.</summary>
    public async Task CheckSilentlyAsync()
    {
        Available = await _updates.CheckForUpdateAsync(CurrentVersion);
        if (Available is not null)
        {
            Log.Information("Update available: {Version}", Available.Version);
            StatusText = $"{Available.Version.ToString(3)} is available.";
        }
    }

    [RelayCommand]
    private async Task CheckAsync()
    {
        IsChecking = true;
        StatusText = "Checking…";
        try
        {
            Available = await _updates.CheckForUpdateAsync(CurrentVersion);
            StatusText = Available is null
                ? "You're up to date."
                : $"{Available.Version.ToString(3)} is available.";
        }
        finally
        {
            IsChecking = false;
        }
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (Available is not { AssetUrl: not null } update)
        {
            return;
        }

        IsDownloading = true;
        DownloadPercent = 0;
        StatusText = "Downloading…";
        try
        {
            var progress = new Progress<double>(p =>
            {
                DownloadPercent = p * 100;
                StatusText = $"Downloading… {p * 100:0}%";
            });
            var installerPath = await _updates.DownloadUpdateAsync(update, progress);

            StatusText = "Launching the installer…";
            Log.Information("Launching update installer {Path}", installerPath);

            // App Installer takes over: it shows the update UI and asks to close DevHub.
            Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Downloading the update failed");
            StatusText = $"Update failed: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        if (Available is { ReleaseUrl.Length: > 0 })
        {
            Process.Start(new ProcessStartInfo(Available.ReleaseUrl) { UseShellExecute = true });
        }
    }

    [RelayCommand]
    private void Dismiss() => Available = null;

    private static Version GetCurrentVersion()
    {
        try
        {
            var v = Windows.ApplicationModel.Package.Current.Id.Version;
            return new Version(v.Major, v.Minor, v.Build, v.Revision);
        }
        catch (Exception)
        {
            // Unpackaged (rare dev scenario): the assembly version is close enough.
            return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
        }
    }
}
