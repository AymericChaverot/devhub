using DevHub.App.Views;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace DevHub.App;

public sealed partial class MainWindow : Window
{
    public static new MainWindow? Current { get; private set; }

    public MainWindow()
    {
        Current = this;
        InitializeComponent();

        // Open at 85% of the screen's work area, centered. Sizes are physical pixels,
        // so a fixed value would shrink with display scaling — this stays proportional.
        var workArea = Microsoft.UI.Windowing.DisplayArea
            .GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest)
            .WorkArea;
        var width = (int)(workArea.Width * 0.85);
        var height = (int)(workArea.Height * 0.85);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            workArea.X + (workArea.Width - width) / 2,
            workArea.Y + (workArea.Height - height) / 2,
            width,
            height));

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // The system caption buttons (min/max/close) don't follow the app theme on
        // their own — set their glyph color explicitly, and again on every theme flip.
        ApplyTitleBarButtonColors(RootGrid.ActualTheme);
        RootGrid.ActualThemeChanged += (sender, _) => ApplyTitleBarButtonColors(sender.ActualTheme);

        RootFrame.Navigate(typeof(ShellPage));
    }

    public void SetTitleBarDimmed(bool dimmed)
    {
        TitleBarDimOverlay.Visibility = dimmed ? Visibility.Visible : Visibility.Collapsed;
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
