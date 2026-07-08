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

        _window = new MainWindow();
        _mainWindow = _window;
        MainWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        ApplyTheme(settings.Settings.Theme);
        _window.Activate();
        Log.Information("DevHub started");
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
        services.AddSingleton<ViewModels.EnvironmentsViewModel>();
        services.AddSingleton<ViewModels.ProjectsViewModel>();
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
