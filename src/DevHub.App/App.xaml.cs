using DevHub.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
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

        // Language must be set before the first ResourceLoader/XAML resource lookup.
        if (settings.Settings.Language is { Length: > 0 } language && language != "System")
        {
            try
            {
                Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not apply the language override {Language}", language);
            }
        }

        // DevHub.Core has no UI dependency; hand it a resource lookup for its user-facing strings.
        DevHub.Core.Services.CoreStrings.Lookup = Loc.GetOrNull;

        // Required before showing toast notifications (pomodoro completion).
        try
        {
            Microsoft.Windows.AppNotifications.AppNotificationManager.Default.Register();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Toast notification registration failed; timers will finish silently");
        }

        var appInstance = AppInstance.GetCurrent();
        Views.ShellPage.PendingLaunchCommand = ReadLaunchCommand(appInstance.GetActivatedEventArgs());
        appInstance.Activated += OnRedirectedActivation;

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

    /// <summary>A second launch was redirected here (see <see cref="Program"/>): come forward and act on it.</summary>
    private void OnRedirectedActivation(object? sender, AppActivationArguments args)
    {
        var command = ReadLaunchCommand(args);
        _window?.DispatcherQueue.TryEnqueue(async () =>
        {
            MainWindow.Current?.BringToFront();
            if (command is not null && Views.ShellPage.Current is { } shell)
            {
                await shell.RunLaunchCommandAsync(command);
            }
        });
    }

    private static LaunchCommand? ReadLaunchCommand(AppActivationArguments args) =>
        args.Kind == ExtendedActivationKind.Launch
        && args.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch
            ? LaunchCommand.Parse(launch.Arguments)
            : null;

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
        services.AddSingleton<IEnvironmentVariableService, EnvironmentVariableService>();
        services.AddSingleton<ViewModels.EnvVariablesViewModel>();
        services.AddSingleton<ViewModels.UpdateViewModel>();
        services.AddSingleton<ViewModels.PomodoroViewModel>();
        services.AddSingleton<IForgeService, GitHubForgeService>();
        services.AddSingleton<IForgeService, GitLabForgeService>();
        services.AddSingleton<IProjectTemplateCatalog, ProjectTemplateCatalog>();
        services.AddSingleton<IProjectScaffolderService, ProjectScaffolderService>();
        services.AddSingleton<ViewModels.ForgeAccountsViewModel>();
        services.AddSingleton<ViewModels.EnvironmentsViewModel>();
        services.AddSingleton<ViewModels.ProjectsViewModel>();
        services.AddSingleton<ViewModels.DashboardViewModel>();
        services.AddTransient<ViewModels.NewProjectWizardViewModel>();
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
