using DevHub.App.ViewModels;
using DevHub.Core.Models;
using DevHub.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DevHub.App.Views;

/// <summary>
/// Project detail: hero header + README / Git / Files / Forge tabs. The code-behind is split
/// by feature into ProjectDetailPage.*.cs partials; this file holds shared state and navigation.
/// </summary>
public sealed partial class ProjectDetailPage : Page
{
    private readonly ISettingsService _settings;
    private readonly IIdeService _ides;
    private readonly IGitService _git;
    private readonly ForgeAccountsViewModel _forgeAccounts;
    private readonly ITerminalService _terminal;
    private Project? _project;

    public ProjectDetailPage()
    {
        _settings = App.Services.GetRequiredService<ISettingsService>();
        _ides = App.Services.GetRequiredService<IIdeService>();
        _git = App.Services.GetRequiredService<IGitService>();
        _forgeAccounts = App.Services.GetRequiredService<ForgeAccountsViewModel>();
        _terminal = App.Services.GetRequiredService<ITerminalService>();
        InitializeComponent();

        Unloaded += (_, _) =>
        {
            // A running fold would keep the page alive through the per-frame callback.
            _heroAnimator?.Stop();

            // Win2D canvases must be torn down explicitly or they leak their device.
            LangDecor.RemoveFromVisualTree();
            IdeDecor.RemoveFromVisualTree();
        };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var (project, relativeDisplay) = e.Parameter switch
        {
            ProjectDetailArgs args => (args.Project, args.RelativePathDisplay),
            Project p => (p, null),
            _ => (null, null),
        };
        if (project is null)
        {
            return;
        }

        _project = project;
        ShowProjectIdentity(project, relativeDisplay ?? project.Path);
        ShowTechStack(project);
        ShowRemoteButton(project.GitRemoteUrl);
        SetupForgeTab(project);
        RefreshPinState();
        RefreshSubprojectButtons();
        RebuildRunMenu();
        RebuildTerminalShellMenu();
        ResetHero();

        await PopulateIdeMenuAsync(project);
        await Task.WhenAll(
            RecordProjectOpenedAsync(project),
            LoadGitAsync(project),
            LoadReadmeAsync(project.Path));
    }

    /// <summary>Stamps the project as "last opened now"; the dashboard's recent list reads this.</summary>
    private async Task RecordProjectOpenedAsync(Project project)
    {
        _settings.Settings.ProjectLastOpened[project.Path] = DateTime.UtcNow;
        await _settings.SaveAsync();
    }

    private void SectionBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var selected = sender.SelectedItem;
        ReadmeContainer.Visibility = selected == ReadmeTab ? Visibility.Visible : Visibility.Collapsed;
        GitContainer.Visibility = selected == GitTab ? Visibility.Visible : Visibility.Collapsed;
        FilesContainer.Visibility = selected == FilesTab ? Visibility.Visible : Visibility.Collapsed;
        ForgeContainer.Visibility = selected == ForgeTab ? Visibility.Visible : Visibility.Collapsed;

        if (selected == FilesTab && !_filesLoaded)
        {
            PopulateFileTreeRoot();
        }

        if (selected == ForgeTab && !_forgeLoaded)
        {
            _forgeLoaded = true;
            _ = LoadForgeAsync();
        }

        // The graph's ScrollViewer only exists once the Git tab has been realized.
        if (selected == GitTab)
        {
            HookGraphScroll();
        }

        if (selected == ForgeTab)
        {
            HookForgeScroll();
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }
}
