using DevHub.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DevHub.App.Views;

public sealed partial class EnvironmentsPage : Page
{
    public EnvironmentsPage()
    {
        ViewModel = App.Services.GetRequiredService<EnvironmentsViewModel>();
        InitializeComponent();

        // The view model is a singleton but the page is recreated on every navigation,
        // so the handler is subscribed on Loaded and removed on Unloaded (no leaks).
        Loaded += OnLoaded;
        Unloaded += (_, _) => ViewModel.GroupsChanged -= OnGroupsChanged;
    }

    public EnvironmentsViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.GroupsChanged += OnGroupsChanged;

        // Re-attach the already-loaded groups when navigating back to this page;
        // LoadAsync is a no-op in that case and would never raise GroupsChanged.
        if (ViewModel.Groups.Count > 0)
        {
            OnGroupsChanged(this, EventArgs.Empty);
        }

        await ViewModel.LoadAsync();
    }

    private void OnGroupsChanged(object? sender, EventArgs e)
    {
        // Grouped CollectionViewSource does not track rebuilt group lists; re-point it.
        GroupedTools.Source = null;
        GroupedTools.Source = ViewModel.Groups;

        if (ViewModel.Groups.Count > 0)
        {
            ToolsGrid.ScrollIntoView(ViewModel.Groups[0][0]);
        }
    }

    private void ErrorInfoBar_CloseButtonClick(InfoBar sender, object args) => ViewModel.ClearError();
}
