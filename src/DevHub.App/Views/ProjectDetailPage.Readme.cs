using Markdig;
using Microsoft.UI.Xaml;
using Serilog;

namespace DevHub.App.Views;

public sealed partial class ProjectDetailPage
{
    private bool _readmeScrollHooked;

    private async Task LoadReadmeAsync(string projectPath)
    {
        var readmePath = Directory.EnumerateFiles(projectPath, "*.md")
            .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals("README", StringComparison.OrdinalIgnoreCase));
        if (readmePath is null)
        {
            return;
        }

        try
        {
            var markdown = await File.ReadAllTextAsync(readmePath);
            var html = WrapHtml(Markdown.ToHtml(markdown, new MarkdownPipelineBuilder().UseAdvancedExtensions().Build()));

            // Transparent default background: no white flash while the control spins up or tears down.
            ReadmeView.DefaultBackgroundColor = Microsoft.UI.Colors.Transparent;
            await ReadmeView.EnsureCoreWebView2Async();
            ReadmeView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

            // README links belong in the user's browser, not inside the app.
            ReadmeView.CoreWebView2.NavigationStarting += (_, navArgs) =>
            {
                if (navArgs.Uri?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
                {
                    navArgs.Cancel = true;
                    ShellLauncher.Open(navArgs.Uri);
                }
            };
            ReadmeView.CoreWebView2.NewWindowRequested += (_, windowArgs) =>
            {
                windowArgs.Handled = true;
                if (windowArgs.Uri?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
                {
                    ShellLauncher.Open(windowArgs.Uri);
                }
            };

            if (!_readmeScrollHooked)
            {
                _readmeScrollHooked = true;
                await HookWebViewScrollAsync(ReadmeView);
            }

            ReadmeView.NavigateToString(html);
            ReadmeView.Visibility = Visibility.Visible;
            NoReadmeText.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to render README {Path}", readmePath);
            NoReadmeText.Text = Loc.T("Detail_ReadmeError", "The README could not be displayed.");
        }
    }

    private string WrapHtml(string body)
    {
        var dark = ActualTheme == ElementTheme.Dark;
        var background = dark ? "#272727" : "#fbfbfb";
        var text = dark ? "#e8e8e8" : "#1b1b1b";
        var muted = dark ? "#9d9d9d" : "#616161";
        var codeBg = dark ? "#1f1f1f" : "#f0f0f0";
        var border = dark ? "#3f3f3f" : "#e0e0e0";
        var link = dark ? "#4cc2ff" : "#005fb8";

        return $$"""
            <!DOCTYPE html>
            <html><head><meta charset="utf-8"><style>
            body { font-family: 'Segoe UI', sans-serif; background: {{background}}; color: {{text}};
                   margin: 0; padding: 24px 28px; line-height: 1.55; font-size: 14px; }
            h1, h2 { border-bottom: 1px solid {{border}}; padding-bottom: 6px; }
            a { color: {{link}}; text-decoration: none; } a:hover { text-decoration: underline; }
            code { background: {{codeBg}}; padding: 2px 5px; border-radius: 4px;
                   font-family: 'Cascadia Code', Consolas, monospace; font-size: 12.5px; }
            pre { background: {{codeBg}}; padding: 12px 14px; border-radius: 8px; overflow-x: auto; }
            pre code { background: none; padding: 0; }
            blockquote { border-left: 3px solid {{border}}; margin-left: 0; padding-left: 14px; color: {{muted}}; }
            table { border-collapse: collapse; } th, td { border: 1px solid {{border}}; padding: 6px 10px; }
            img { max-width: 100%; }
            </style></head><body>{{body}}</body></html>
            """;
    }
}
