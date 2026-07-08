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
        InitializeComponent();
        Loaded += (_, _) => Populate();
    }

    private void Populate()
    {
        FolderPathText.Text = _settings.Settings.ProjectsFolderPath ?? "Not set";
        DataFolderText.Text = DataFolder;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Version {version?.ToString(3) ?? "?"} (preview)";

        _initializingTheme = true;
        ThemePicker.SelectedIndex = _settings.Settings.Theme switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0,
        };
        _initializingTheme = false;
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

    private void OpenLogs_Click(object sender, RoutedEventArgs e) =>
        OpenInExplorer(Path.Combine(DataFolder, "logs"));

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e) => OpenInExplorer(DataFolder);

    private static void OpenInExplorer(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
