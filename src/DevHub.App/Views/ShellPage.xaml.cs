using DevHub.App.ViewModels;
using DevHub.Core.Models;
using DevHub.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace DevHub.App.Views;

public sealed record CommandPaletteItem(
    string Glyph,
    string Title,
    string Subtitle,
    string Category,
    Func<Task> ExecuteAsync,
    double Boost = 0);

public sealed partial class ShellPage : Page
{
    private List<CommandPaletteItem> _allPaletteItems = [];

    public ShellPage()
    {
        Pomodoro = App.Services.GetRequiredService<PomodoroViewModel>();
        InitializeComponent();
        Current = this;
    }

    public PomodoroViewModel Pomodoro { get; }

    /// <summary>The single shell instance; lets pages switch the selected nav section.</summary>
    public static ShellPage? Current { get; private set; }

    /// <summary>Selects a nav item by tag, which navigates its page (sidebar stays in sync).</summary>
    public void NavigateTo(string tag)
    {
        if (tag == "settings")
        {
            Nav.SelectedItem = Nav.SettingsItem;
            return;
        }

        var item = Nav.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(i => (string?)i.Tag == tag);
        if (item is not null)
        {
            Nav.SelectedItem = item;
        }
    }

    /// <summary>Navigates the shell frame directly to a specific project's detail page.</summary>
    public void OpenProjectDetail(Project project)
    {
        NavigateTo("projects");
        ContentFrame.Navigate(typeof(ProjectDetailPage), project);
    }

    /// <summary>Opens the Toolbox page and selects a specific tool by index.</summary>
    public void OpenToolboxTool(int toolIndex)
    {
        DevToolsPage.PendingToolIndex = toolIndex;
        if (ContentFrame.Content is DevToolsPage existing)
        {
            existing.SelectTool(toolIndex);
        }
        else
        {
            NavigateTo("toolbox");
        }
    }

    /// <summary>A command DevHub was launched with, run once the shell has loaded.</summary>
    public static LaunchCommand? PendingLaunchCommand { get; set; }

    /// <summary>Carries out a launch command (jump list entry, or a second launch redirected here).</summary>
    public async Task RunLaunchCommandAsync(LaunchCommand command)
    {
        switch (command)
        {
            case LaunchCommand.ShowPalette when CommandPaletteOverlay.Visibility != Visibility.Visible:
                await ToggleCommandPaletteAsync();
                break;

            case LaunchCommand.OpenProject open:
                var projects = await App.Services.GetRequiredService<IProjectStore>().GetAllAsync();
                var project = projects.FirstOrDefault(
                    p => string.Equals(p.Path, open.Path, StringComparison.OrdinalIgnoreCase));
                if (project is not null)
                {
                    OpenProjectDetail(project);
                }
                else
                {
                    // Moved or deleted since the jump list was built.
                    NavigateTo("projects");
                }

                break;
        }
    }

    private async void Nav_Loaded(object sender, RoutedEventArgs e)
    {
        NavigateTo("home");

        if (PendingLaunchCommand is { } command)
        {
            PendingLaunchCommand = null;
            await RunLaunchCommandAsync(command);
        }
    }

    private async void Nav_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer == QuickSearchItem)
        {
            await ToggleCommandPaletteAsync();
        }
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(SettingsPage));
            return;
        }

        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        var pageType = tag switch
        {
            "home" => typeof(DashboardPage),
            "environments" => typeof(EnvironmentsPage),
            "envvars" => typeof(EnvVariablesPage),
            "projects" => typeof(ProjectsPage),
            "toolbox" => typeof(DevToolsPage),
            "pomodoro" => typeof(PomodoroPage),
            _ => null,
        };

        if (pageType is not null && ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    // ============ Command Palette (Ctrl+K / Ctrl+P) ============

    private async void CommandPaletteAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ToggleCommandPaletteAsync();
    }

    public async Task ToggleCommandPaletteAsync()
    {
        if (CommandPaletteOverlay.Visibility == Visibility.Visible)
        {
            HideCommandPalette();
            return;
        }

        _allPaletteItems = await BuildPaletteItemsAsync();
        CommandPaletteSearchBox.Text = "";
        FilterPaletteItems("");
        CommandPaletteOverlay.Visibility = Visibility.Visible;
        MainWindow.Current?.SetTitleBarDimmed(true);
        CommandPaletteSearchBox.Focus(FocusState.Programmatic);
    }

    private void HideCommandPalette()
    {
        CommandPaletteOverlay.Visibility = Visibility.Collapsed;
        MainWindow.Current?.SetTitleBarDimmed(false);
    }

    private void CommandPaletteBackdrop_Tapped(object sender, TappedRoutedEventArgs e) =>
        HideCommandPalette();

    private void CommandPaletteCard_Tapped(object sender, TappedRoutedEventArgs e) =>
        e.Handled = true;

    private void CommandPaletteSearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        FilterPaletteItems(CommandPaletteSearchBox.Text);

    private async void CommandPaletteSearchBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            HideCommandPalette();
        }
        else if (e.Key == VirtualKey.Down && CommandPaletteList.Items.Count > 0)
        {
            e.Handled = true;
            CommandPaletteList.SelectedIndex = Math.Min(
                CommandPaletteList.Items.Count - 1,
                CommandPaletteList.SelectedIndex + 1);
            CommandPaletteList.ScrollIntoView(CommandPaletteList.SelectedItem);
        }
        else if (e.Key == VirtualKey.Up && CommandPaletteList.Items.Count > 0)
        {
            e.Handled = true;
            CommandPaletteList.SelectedIndex = Math.Max(0, CommandPaletteList.SelectedIndex - 1);
            CommandPaletteList.ScrollIntoView(CommandPaletteList.SelectedItem);
        }
        else if (e.Key == VirtualKey.Enter && CommandPaletteList.SelectedItem is CommandPaletteItem item)
        {
            e.Handled = true;
            HideCommandPalette();
            await item.ExecuteAsync();
        }
    }

    private async void CommandPaletteList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CommandPaletteItem item)
        {
            HideCommandPalette();
            await item.ExecuteAsync();
        }
    }

    /// <summary>
    /// Fuzzy-matches every item and ranks the best first. The title counts most; the subtitle
    /// (a project's path) and the category only help an item surface, never outrank a title match.
    /// </summary>
    private void FilterPaletteItems(string query)
    {
        const int SubtitlePenalty = 300;
        const int CategoryPenalty = 400;

        var matches = string.IsNullOrWhiteSpace(query)
            ? _allPaletteItems
            : _allPaletteItems
                .Select(item => (Item: item, Score: new[]
                {
                    FuzzyMatcher.Score(query, item.Title),
                    FuzzyMatcher.Score(query, item.Subtitle) - SubtitlePenalty,
                    FuzzyMatcher.Score(query, item.Category) - CategoryPenalty,
                }.Max()))
                .Where(m => m.Score is not null)
                .OrderByDescending(m => m.Score + m.Item.Boost)
                .Select(m => m.Item)
                .ToList();

        CommandPaletteList.ItemsSource = matches;
        if (matches.Count > 0)
        {
            CommandPaletteList.SelectedIndex = 0;
        }
    }

    /// <summary>Favours pinned projects, then recently opened ones, fading over a few days.</summary>
    private static double ProjectBoost(bool pinned, DateTime? lastOpenedUtc)
    {
        const double PinnedBoost = 30;
        const double RecentBoost = 60;

        var recency = lastOpenedUtc is { } opened
            ? RecentBoost / (1 + Math.Max(0, (DateTime.UtcNow - opened).TotalDays))
            : 0;
        return (pinned ? PinnedBoost : 0) + recency;
    }

    private async Task<List<CommandPaletteItem>> BuildPaletteItemsAsync()
    {
        var items = new List<CommandPaletteItem>();
        var navCategory = Loc.T("Palette_CatNav", "Navigation");
        var projectCategory = Loc.T("Palette_CatProject", "Project");
        var toolCategory = Loc.T("Palette_CatTool", "Toolbox");
        var pomoCategory = "Pomodoro";

        // 1. Projects
        var store = App.Services.GetRequiredService<IProjectStore>();
        var settings = App.Services.GetRequiredService<ISettingsService>();
        var ides = App.Services.GetRequiredService<IIdeService>();
        var terminal = App.Services.GetRequiredService<ITerminalService>();
        var allProjects = await store.GetAllAsync();
        var ignored = settings.Settings.IgnoredProjectPaths;
        var pinned = settings.Settings.PinnedProjectPaths;
        var lastOpened = settings.Settings.ProjectLastOpened;

        // Before anything is typed: pinned first, then most recently opened, then by name.
        foreach (var entry in allProjects
            .Where(p => !ignored.Contains(p.Path, StringComparer.OrdinalIgnoreCase))
            .Select(p => (
                Project: p,
                Pinned: pinned.Contains(p.Path, StringComparer.OrdinalIgnoreCase),
                Opened: lastOpened.TryGetValue(p.Path, out var at) ? at : (DateTime?)null))
            .OrderByDescending(e => e.Pinned)
            .ThenByDescending(e => e.Opened ?? DateTime.MinValue)
            .ThenBy(e => e.Project.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var p = entry.Project;
            var boost = ProjectBoost(entry.Pinned, entry.Opened);
            items.Add(new CommandPaletteItem(
                "\uE8B7",
                p.Name,
                p.Path,
                projectCategory,
                () =>
                {
                    OpenProjectDetail(p);
                    return Task.CompletedTask;
                },
                boost));

            items.Add(new CommandPaletteItem(
                "\uE8A7",
                Loc.F("Palette_OpenInIde", "{0} — Open in IDE", p.Name),
                p.Path,
                projectCategory,
                async () =>
                {
                    var installed = await ides.GetInstalledIdesAsync();
                    if (installed.Count == 0)
                    {
                        return;
                    }

                    var preferredId = settings.Settings.ProjectIdePreferences.GetValueOrDefault(p.Path)
                        ?? settings.Settings.PreferredIdeId;
                    var ide = installed.FirstOrDefault(i => i.Id == preferredId) ?? installed[0];
                    await ides.OpenAsync(ide, p.Path);
                },
                boost));

            items.Add(new CommandPaletteItem(
                "\uE756",
                Loc.F("Palette_OpenTerminal", "{0} — Open in Terminal", p.Name),
                p.Path,
                projectCategory,
                () =>
                {
                    terminal.OpenTerminal(p.Path);
                    return Task.CompletedTask;
                },
                boost));
        }

        // 2. Navigation
        (string Tag, string Glyph, string Label)[] navEntries =
        [
            ("home", "\uE80F", Loc.T("Shell_NavHome.Content", "Home")),
            ("projects", "\uE8B7", Loc.T("Shell_NavProjects.Content", "Projects")),
            ("environments", "\uE7B8", Loc.T("Shell_NavEnvironments.Content", "Environments")),
            ("envvars", "\uE756", Loc.T("Shell_NavEnvVars.Content", "Environment Variables")),
            ("toolbox", "\uEC7A", Loc.T("Shell_NavToolbox.Content", "Toolbox")),
            ("pomodoro", "\uE823", Loc.T("Shell_NavPomodoro.Content", "Pomodoro")),
            ("settings", "\uE713", Loc.T("Settings_Title.Text", "Settings")),
        ];
        foreach (var (tag, glyph, label) in navEntries)
        {
            var t = tag;
            items.Add(new CommandPaletteItem(
                glyph,
                label,
                Loc.T("Palette_GoToSection", "Go to section"),
                navCategory,
                () =>
                {
                    NavigateTo(t);
                    return Task.CompletedTask;
                }));
        }

        // 3. Toolbox tools
        (int Index, string Glyph, string Name)[] tools =
        [
            (0, "\uE8AB", Loc.T("Tools_NavConverter.Text", "Converter")),
            (1, "\uE72E", Loc.T("Tools_NavHashes.Text", "Hashes")),
            (2, "\uE77B", Loc.T("Tools_NavJwt.Text", "JWT decoder")),
            (3, "\uE943", Loc.T("Tools_NavJson.Text", "JSON formatter")),
            (4, "\uE823", Loc.T("Tools_NavUuid.Text", "UUID & time")),
            (5, "\uE721", Loc.T("Tools_NavRegex.Text", "Regex tester")),
            (6, "\uE7C3", Loc.T("Tools_NavDiff.Text", "Text diff")),
            (7, "\uE790", Loc.T("Tools_NavColor.Text", "Color converter")),
            (8, "\uE8D7", Loc.T("Tools_NavPassword.Text", "Password generator")),
            (9, "\uE774", Loc.T("Tools_NavApi.Text", "API client")),
            (10, "\uE968", Loc.T("Tools_NavPorts.Text", "Local ports")),
        ];
        foreach (var (idx, glyph, name) in tools)
        {
            var toolIdx = idx;
            items.Add(new CommandPaletteItem(
                glyph,
                name,
                Loc.T("Shell_NavToolbox.Content", "Toolbox"),
                toolCategory,
                () =>
                {
                    OpenToolboxTool(toolIdx);
                    return Task.CompletedTask;
                }));
        }

        // 4. Pomodoro quick actions
        items.Add(new CommandPaletteItem(
            "\uE823",
            Loc.T("Pomo_Focus25.Content", "Focus · 25 min"),
            "Pomodoro",
            pomoCategory,
            () =>
            {
                Pomodoro.Start(25, Loc.T("Pomo_FocusLabel", "Focus"));
                return Task.CompletedTask;
            }));
        items.Add(new CommandPaletteItem(
            "\uE823",
            Loc.T("Pomo_Focus50.Content", "Focus · 50 min"),
            "Pomodoro",
            pomoCategory,
            () =>
            {
                Pomodoro.Start(50, Loc.T("Pomo_FocusLabel", "Focus"));
                return Task.CompletedTask;
            }));
        items.Add(new CommandPaletteItem(
            "\uE823",
            Loc.T("Pomo_Break5.Content", "Break · 5 min"),
            "Pomodoro",
            pomoCategory,
            () =>
            {
                Pomodoro.Start(5, Loc.T("Pomo_BreakLabel", "Break"), isBreak: true);
                return Task.CompletedTask;
            }));

        return items;
    }
}
