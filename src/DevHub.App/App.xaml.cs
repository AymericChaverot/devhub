using DevHub.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Serilog;

namespace DevHub.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        ConfigureLogging();
        Services = ConfigureServices();
        UnhandledException += (_, e) => Log.Error(e.Exception, "Unhandled exception");
    }

    /// <summary>Application-wide service provider, set once at startup.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>Native handle of the main window; needed to initialize file/folder pickers.</summary>
    public static IntPtr MainWindowHandle { get; private set; }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var settings = Services.GetRequiredService<ISettingsService>();
        await settings.LoadAsync();

        // Required before showing toast notifications (pomodoro completion).
        try
        {
            Microsoft.Windows.AppNotifications.AppNotificationManager.Default.Register();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Toast notification registration failed; timers will finish silently");
        }

        _window = new MainWindow();
        _mainWindow = _window;
        MainWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        ApplyTheme(settings.Settings.Theme);
        _window.Activate();
        Log.Information("DevHub started");

        // Silent update check; the dashboard banner appears if a release is newer.
        _ = Services.GetRequiredService<ViewModels.UpdateViewModel>().CheckSilentlyAsync();
    }

    private static Window? _mainWindow;

    /// <summary>Applies "System" / "Light" / "Dark" to the whole window, live.</summary>
    public static void ApplyTheme(string theme)
    {
        if (_mainWindow?.Content is FrameworkElement root)
        {
            root.RequestedTheme = theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<ICatalogService, EmbeddedCatalogService>();
        services.AddSingleton<IPackageService, WingetCliPackageService>();
        services.AddSingleton<IProjectScanner, FileSystemProjectScanner>();
        services.AddSingleton<IProjectStore, SqliteProjectStore>();
        services.AddSingleton<IGitService, LibGit2GitService>();
        services.AddSingleton<IFileOperations, ShellFileOperations>();
        services.AddSingleton<IIdeService, InstalledIdeService>();
        services.AddSingleton<ISystemInfoService, WmiSystemInfoService>();
        services.AddSingleton<IWeatherService, OpenMeteoWeatherService>();
        services.AddSingleton<ITokenStore, WindowsCredentialTokenStore>();
        services.AddSingleton<ITerminalService, WindowsTerminalService>();
        services.AddSingleton<IUpdateService, GitHubUpdateService>();
        services.AddSingleton<ViewModels.UpdateViewModel>();
        services.AddSingleton<ViewModels.PomodoroViewModel>();
        services.AddSingleton<IForgeService, GitHubForgeService>();
        services.AddSingleton<IForgeService, GitLabForgeService>();
        services.AddSingleton<ViewModels.ForgeAccountsViewModel>();
        services.AddSingleton<ViewModels.EnvironmentsViewModel>();
        services.AddSingleton<ViewModels.ProjectsViewModel>();
        services.AddSingleton<ViewModels.DashboardViewModel>();
        return services.BuildServiceProvider();
    }

    private static void ConfigureLogging()
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DevHub", "logs", "devhub-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();
    }
}
