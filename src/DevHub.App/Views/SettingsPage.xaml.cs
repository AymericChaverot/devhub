using System.Diagnostics;
using System.Reflection;
using DevHub.App.ViewModels;
using DevHub.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace DevHub.App.Views;

public sealed partial class SettingsPage : Page
{
    private static readonly string DataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevHub");

    private readonly ISettingsService _settings;
    private readonly ProjectsViewModel _projects;
    private bool _initializingTheme;

    public SettingsPage()
    {
        _settings = App.Services.GetRequiredService<ISettingsService>();
        _projects = App.Services.GetRequiredService<ProjectsViewModel>();
        Accounts = App.Services.GetRequiredService<ForgeAccountsViewModel>();
        Updates = App.Services.GetRequiredService<UpdateViewModel>();
        InitializeComponent();
        Loaded += (_, _) => Populate();
    }

    public ForgeAccountsViewModel Accounts { get; }

    public UpdateViewModel Updates { get; }

    private void Populate()
    {
        FolderPathText.Text = _settings.Settings.ProjectsFolderPath ?? Loc.T("Settings_NotSet", "Not set");
        WeatherLocationBox.Text = _settings.Settings.WeatherLocation ?? "";
        DataFolderText.Text = DataFolder;

        // Package identity version — what the updater compares against releases.
        VersionText.Text = $"{Updates.CurrentVersionText} (preview)";

        _initializingTheme = true;
        ThemePicker.SelectedIndex = _settings.Settings.Theme switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0,
        };
        LanguagePicker.SelectedIndex = _settings.Settings.Language switch
        {
            "en-US" => 1,
            "fr-FR" => 2,
            _ => 0,
        };
        TerminalShellPicker.SelectedIndex = (_settings.Settings.DefaultTerminalShell ?? "powershell").ToLowerInvariant() switch
        {
            "pwsh" => 1,
            "cmd" => 2,
            "git-bash" => 3,
            "wsl" => 4,
            _ => 0,
        };
        _initializingTheme = false;
    }

    private async void TerminalShellPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingTheme || (TerminalShellPicker.SelectedItem as ComboBoxItem)?.Tag is not string shellId)
        {
            return;
        }

        _settings.Settings.DefaultTerminalShell = shellId;
        await _settings.SaveAsync();
    }

    private async void LanguagePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingTheme || (LanguagePicker.SelectedItem as ComboBoxItem)?.Tag is not string language)
        {
            return;
        }

        // Applied on the next launch; MRT can't reload live XAML resources.
        _settings.Settings.Language = language;
        await _settings.SaveAsync();
    }

    private async void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            // Persists the setting and refreshes the Projects page in one go.
            await _projects.SetProjectsFolderAsync(folder.Path);
            FolderPathText.Text = folder.Path;
        }
    }

    private async void ThemePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingTheme || (ThemePicker.SelectedItem as ComboBoxItem)?.Tag is not string theme)
        {
            return;
        }

        _settings.Settings.Theme = theme;
        await _settings.SaveAsync();
        App.ApplyTheme(theme);
    }

    private async void WeatherLocation_LostFocus(object sender, RoutedEventArgs e) =>
        await SaveWeatherLocationAsync();

    private async void WeatherLocation_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            await SaveWeatherLocationAsync();
        }
    }

    private async Task SaveWeatherLocationAsync()
    {
        var value = WeatherLocationBox.Text.Trim();
        var normalized = value.Length == 0 ? null : value;
        if (_settings.Settings.WeatherLocation == normalized)
        {
            return;
        }

        // The weather cache is keyed by this value; the dashboard refetches on its next load.
        _settings.Settings.WeatherLocation = normalized;
        await _settings.SaveAsync();
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e) =>
        OpenInExplorer(Path.Combine(DataFolder, "logs"));

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e) => OpenInExplorer(DataFolder);

    private static void OpenInExplorer(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
