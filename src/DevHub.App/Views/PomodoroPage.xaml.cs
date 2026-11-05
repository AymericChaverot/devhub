using DevHub.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DevHub.App.Views;

public sealed partial class PomodoroPage : Page
{
    public PomodoroPage()
    {
        // Singleton view model: the countdown keeps running when the user navigates away.
        ViewModel = App.Services.GetRequiredService<PomodoroViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadProjectsAsync();
    }

    public PomodoroViewModel ViewModel { get; }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        // Tags carry "minutes|kind" where kind is a semantic id, never a display string.
        if ((sender as FrameworkElement)?.Tag is string tag)
        {
            var parts = tag.Split('|');
            var isBreak = parts[1] == "break";
            var label = isBreak
                ? Loc.T("Pomo_BreakLabel", "Break")
                : Loc.T("Pomo_FocusLabel", "Focus");
            ViewModel.Start(int.Parse(parts[0]), label, isBreak);
        }
    }

    private void CustomStart_Click(object sender, RoutedEventArgs e)
    {
        if (double.IsNaN(CustomMinutes.Value))
        {
            return;
        }

        var fallback = Loc.T("Pomo_FocusLabel", "Focus");
        var label = CustomLabel.Text.Trim().Length > 0 ? CustomLabel.Text.Trim() : fallback;
        ViewModel.Start((int)CustomMinutes.Value, label);
    }
}
