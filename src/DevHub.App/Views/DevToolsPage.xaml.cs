using System.Diagnostics;
using System.Text;
using DevHub.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace DevHub.App.Views;

public sealed partial class DevToolsPage : Page
{
    private static readonly HttpClient ApiClient = CreateApiClient();

    public DevToolsPage()
    {
        InitializeComponent();
        ToolsList.SelectedIndex = 0;
    }

    private static HttpClient CreateApiClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "DevHub");
        return client;
    }

    private void ToolsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var index = ToolsList.SelectedIndex;
        ConverterPanel.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        HashesPanel.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        JwtPanel.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        JsonPanel.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        UuidPanel.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed;
        RegexPanel.Visibility = index == 5 ? Visibility.Visible : Visibility.Collapsed;
        DiffPanel.Visibility = index == 6 ? Visibility.Visible : Visibility.Collapsed;
        ColorPanel.Visibility = index == 7 ? Visibility.Visible : Visibility.Collapsed;
        PasswordPanel.Visibility = index == 8 ? Visibility.Visible : Visibility.Collapsed;
        ApiPanel.Visibility = index == 9 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void CopyToClipboard(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    // ============ Converter ============

    private void Converter_Changed(object sender, object e)
    {
        // Fires for both combo changes and typing; the panels may not be realized yet.
        if (ConvertInput is null || ConvertOutput is null)
        {
            return;
        }

        ConvertError.Visibility = Visibility.Collapsed;
        if (ConvertInput.Text.Length == 0)
        {
            ConvertOutput.Text = "";
            return;
        }

        try
        {
            ConvertOutput.Text = DevToolbox.Convert(
                ConvertInput.Text,
                (ToolTextFormat)ConvertFrom.SelectedIndex,
                (ToolTextFormat)ConvertTo.SelectedIndex);
        }
        catch (FormatException ex)
        {
            ConvertOutput.Text = "";
            ConvertError.Text = ex.Message;
            ConvertError.Visibility = Visibility.Visible;
        }
    }

    private void ConverterSwap_Click(object sender, RoutedEventArgs e)
    {
        (ConvertFrom.SelectedIndex, ConvertTo.SelectedIndex) = (ConvertTo.SelectedIndex, ConvertFrom.SelectedIndex);
        if (ConvertOutput.Text.Length > 0)
        {
            ConvertInput.Text = ConvertOutput.Text;
        }
    }

    private void CopyConvertOutput_Click(object sender, RoutedEventArgs e) => CopyToClipboard(ConvertOutput.Text);

    // ============ Hashes ============

    private void HashInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        HashList.Items.Clear();
        if (HashInput.Text.Length == 0)
        {
            return;
        }

        foreach (var (algorithm, digest) in DevToolbox.ComputeHashes(HashInput.Text))
        {
            var digestBox = new TextBox
            {
                Text = digest,
                IsReadOnly = true,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code, Consolas"),
                FontSize = 12.5,
            };
            Grid.SetColumn(digestBox, 1);

            var copy = new Button
            {
                Content = new FontIcon { FontSize = 14, Glyph = "\uE8C8" },
                Tag = digest,
            };
            ToolTipService.SetToolTip(copy, Loc.F("Tools_CopyAlgoTip", "Copy {0}", algorithm));
            copy.Click += (s, _) => CopyToClipboard((string)((Button)s).Tag);
            Grid.SetColumn(copy, 2);

            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = algorithm, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(digestBox);
            row.Children.Add(copy);
            HashList.Items.Add(row);
        }
    }

    // ============ JWT ============

    private void JwtInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        JwtError.Visibility = Visibility.Collapsed;
        JwtHeader.Text = "";
        JwtPayload.Text = "";
        if (JwtInput.Text.Trim().Length == 0)
        {
            return;
        }

        try
        {
            (JwtHeader.Text, JwtPayload.Text) = DevToolbox.DecodeJwt(JwtInput.Text);
        }
        catch (FormatException ex)
        {
            JwtError.Text = ex.Message;
            JwtError.Visibility = Visibility.Visible;
        }
    }

    // ============ JSON ============

    private void JsonFormat_Click(object sender, RoutedEventArgs e) => RunJson(indent: true);

    private void JsonMinify_Click(object sender, RoutedEventArgs e) => RunJson(indent: false);

    private void RunJson(bool indent)
    {
        JsonError.Visibility = Visibility.Collapsed;
        try
        {
            JsonOutput.Text = DevToolbox.FormatJson(JsonInput.Text, indent);
        }
        catch (FormatException ex)
        {
            JsonOutput.Text = "";
            JsonError.Text = ex.Message;
            JsonError.Visibility = Visibility.Visible;
        }
    }

    private void CopyJsonOutput_Click(object sender, RoutedEventArgs e) => CopyToClipboard(JsonOutput.Text);

    // ============ UUID & time ============

    private void GenerateUuids_Click(object sender, RoutedEventArgs e)
    {
        var count = UuidCount.SelectedIndex switch { 0 => 1, 1 => 5, _ => 10 };
        var uuids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid().ToString());
        var text = string.Join("\n", uuids);
        UuidOutput.Text = UuidUppercase.IsChecked == true ? text.ToUpperInvariant() : text;
    }

    private void CopyUuids_Click(object sender, RoutedEventArgs e) => CopyToClipboard(UuidOutput.Text);

    private void TimestampNow_Click(object sender, RoutedEventArgs e) =>
        TimestampInput.Text = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

    private void TimestampInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        TimestampError.Visibility = Visibility.Collapsed;
        TimestampOutput.Text = "";
        var text = TimestampInput.Text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        if (!long.TryParse(text, out var value))
        {
            TimestampError.Text = Loc.T("Tools_TimestampNotANumber", "Enter a whole number of seconds (or milliseconds).");
            TimestampError.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            TimestampOutput.Text = DevToolbox.DescribeUnixTimestamp(value);
        }
        catch (ArgumentOutOfRangeException)
        {
            TimestampError.Text = Loc.T("Tools_TimestampOutOfRange", "This timestamp is out of the representable range.");
            TimestampError.Visibility = Visibility.Visible;
        }
    }

    // ============ Regex tester ============

    private void Regex_Changed(object sender, object e)
    {
        if (RegexPattern is null || RegexOutput is null)
        {
            return;
        }

        RegexError.Visibility = Visibility.Collapsed;
        RegexSummary.Text = "";
        RegexOutput.Text = "";
        if (RegexPattern.Text.Length == 0 || RegexInput.Text.Length == 0)
        {
            return;
        }

        try
        {
            var matches = DevToolbox.TestRegex(
                RegexPattern.Text,
                RegexInput.Text,
                RegexIgnoreCase.IsChecked == true,
                RegexMultiline.IsChecked == true);

            RegexSummary.Text = matches.Count switch
            {
                0 => Loc.T("Tools_RegexNoMatch", "No match."),
                1 => Loc.T("Tools_RegexOneMatch", "1 match."),
                _ => Loc.F("Tools_RegexMatches", "{0} matches.", matches.Count),
            };

            var lines = matches.Select(m =>
                m.Groups.Count > 0
                    ? $"[{m.Index}] {m.Value}\n      groups: {string.Join("  ", m.Groups)}"
                    : $"[{m.Index}] {m.Value}");
            RegexOutput.Text = string.Join("\n", lines);
        }
        catch (FormatException ex)
        {
            RegexError.Text = ex.Message;
            RegexError.Visibility = Visibility.Visible;
        }
    }

    // ============ Text diff ============

    private void DiffCompare_Click(object sender, RoutedEventArgs e)
    {
        DiffError.Visibility = Visibility.Collapsed;
        DiffSummary.Text = "";
        DiffOutput.Blocks.Clear();

        try
        {
            var lines = DevToolbox.DiffLines(DiffLeft.Text, DiffRight.Text);
            var added = lines.Count(l => l.Marker == '+');
            var removed = lines.Count(l => l.Marker == '-');
            DiffSummary.Text = $"+{added}  −{removed}";

            var removedBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 0xCF, 0x22, 0x2E));
            var addedBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 0x2D, 0xA4, 0x4E));

            foreach (var line in lines.Take(2000))
            {
                var paragraph = new Microsoft.UI.Xaml.Documents.Paragraph();
                var run = new Microsoft.UI.Xaml.Documents.Run { Text = $"{line.Marker} {line.Text}" };
                if (line.Marker == '+')
                {
                    run.Foreground = addedBrush;
                }
                else if (line.Marker == '-')
                {
                    run.Foreground = removedBrush;
                }

                paragraph.Inlines.Add(run);
                DiffOutput.Blocks.Add(paragraph);
            }
        }
        catch (FormatException ex)
        {
            DiffError.Text = ex.Message;
            DiffError.Visibility = Visibility.Visible;
        }
    }

    // ============ Color converter ============

    private void ColorInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        ColorError.Visibility = Visibility.Collapsed;
        if (ColorInput.Text.Trim().Length == 0)
        {
            ColorSwatch.Background = null;
            ColorHex.Text = ColorRgb.Text = ColorHsl.Text = "";
            return;
        }

        try
        {
            var color = DevToolbox.ParseColor(ColorInput.Text);
            ColorSwatch.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, color.R, color.G, color.B));
            ColorHex.Text = color.Hex;
            ColorRgb.Text = color.Rgb;
            ColorHsl.Text = color.Hsl;
        }
        catch (FormatException ex)
        {
            ColorSwatch.Background = null;
            ColorHex.Text = ColorRgb.Text = ColorHsl.Text = "";
            ColorError.Text = ex.Message;
            ColorError.Visibility = Visibility.Visible;
        }
    }

    // ============ Password generator ============

    private void GeneratePassword_Click(object sender, RoutedEventArgs e)
    {
        PasswordError.Visibility = Visibility.Collapsed;
        try
        {
            PasswordOutput.Text = DevToolbox.GeneratePassword(
                (int)PasswordLength.Value,
                PasswordUpper.IsChecked == true,
                PasswordLower.IsChecked == true,
                PasswordDigits.IsChecked == true,
                PasswordSymbols.IsChecked == true);
        }
        catch (FormatException ex)
        {
            PasswordError.Text = ex.Message;
            PasswordError.Visibility = Visibility.Visible;
        }
    }

    private void CopyPassword_Click(object sender, RoutedEventArgs e) => CopyToClipboard(PasswordOutput.Text);

    // ============ API client ============

    private void ApiUrl_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            ApiSend_Click(sender, e);
        }
    }

    private async void ApiSend_Click(object sender, RoutedEventArgs e)
    {
        var url = ApiUrl.Text.Trim();
        if (url.Length == 0)
        {
            return;
        }

        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
            ApiUrl.Text = url;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            ApiStatus.Text = Loc.T("Tools_ApiInvalidUrl", "Invalid URL");
            return;
        }

        var methodName = (ApiMethod.SelectedItem as ComboBoxItem)?.Content as string ?? "GET";
        using var request = new HttpRequestMessage(new HttpMethod(methodName), uri);

        var hasBody = methodName is not ("GET" or "HEAD") && ApiBody.Text.Length > 0;
        string? contentType = null;
        foreach (var line in ApiHeaders.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                contentType = value;
            }
            else
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        if (hasBody)
        {
            request.Content = new StringContent(ApiBody.Text, Encoding.UTF8, contentType ?? "application/json");
        }

        ApiSendButton.IsEnabled = false;
        ApiProgress.IsActive = true;
        ApiStatus.Text = Loc.T("Tools_ApiSending", "Sending…");
        ApiResponseBody.Text = "";
        ApiResponseHeadersExpander.Visibility = Visibility.Collapsed;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await ApiClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var body = await response.Content.ReadAsStringAsync();
            stopwatch.Stop();

            ApiStatus.Text = $"{(int)response.StatusCode} {response.ReasonPhrase} · {stopwatch.ElapsedMilliseconds} ms · {body.Length:N0} chars";

            var headerLines = response.Headers
                .Concat(response.Content.Headers)
                .Select(h => $"{h.Key}: {string.Join(", ", h.Value)}");
            ApiResponseHeaders.Text = string.Join("\n", headerLines);
            ApiResponseHeadersExpander.Visibility = Visibility.Visible;

            // Pretty-print JSON responses; anything else is shown raw.
            try
            {
                ApiResponseBody.Text = DevToolbox.FormatJson(body, indent: true);
            }
            catch (FormatException)
            {
                ApiResponseBody.Text = body;
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            ApiStatus.Text = Loc.F("Tools_ApiFailed", "Request failed · {0} ms", stopwatch.ElapsedMilliseconds);
            ApiResponseBody.Text = ex.Message;
        }
        finally
        {
            ApiSendButton.IsEnabled = true;
            ApiProgress.IsActive = false;
        }
    }
}
