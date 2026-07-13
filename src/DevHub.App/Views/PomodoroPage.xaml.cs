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
    }

    public PomodoroViewModel ViewModel { get; }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string tag)
        {
            var parts = tag.Split('|');
            ViewModel.Start(int.Parse(parts[0]), parts[1]);
        }
    }

    private void CustomStart_Click(object sender, RoutedEventArgs e)
    {
        if (double.IsNaN(CustomMinutes.Value))
        {
            return;
        }

        var label = CustomLabel.Text.Trim().Length > 0 ? CustomLabel.Text.Trim() : "Focus";
        ViewModel.Start((int)CustomMinutes.Value, label);
    }
}
