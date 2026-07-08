using DevHub.App.Views;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace DevHub.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1200, 800));

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // The system caption buttons (min/max/close) don't follow the app theme on
        // their own — set their glyph color explicitly, and again on every theme flip.
        ApplyTitleBarButtonColors(RootGrid.ActualTheme);
        RootGrid.ActualThemeChanged += (sender, _) => ApplyTitleBarButtonColors(sender.ActualTheme);

        RootFrame.Navigate(typeof(ShellPage));
    }

    private void ApplyTitleBarButtonColors(ElementTheme theme)
    {
        var dark = theme == ElementTheme.Dark;
        var foreground = dark ? Colors.White : Colors.Black;
        var hoverBackground = dark ? Color.FromArgb(25, 255, 255, 255) : Color.FromArgb(15, 0, 0, 0);

        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = dark
            ? Color.FromArgb(150, 255, 255, 255)
            : Color.FromArgb(150, 0, 0, 0);
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonHoverBackgroundColor = hoverBackground;
        titleBar.ButtonPressedBackgroundColor = hoverBackground;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
    }
}
