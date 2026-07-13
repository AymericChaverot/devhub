using DevHub.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DevHub.App.Views;

public sealed partial class ShellPage : Page
{
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

    private void Nav_Loaded(object sender, RoutedEventArgs e)
    {
        Nav.SelectedItem = Nav.MenuItems[0];
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
}
