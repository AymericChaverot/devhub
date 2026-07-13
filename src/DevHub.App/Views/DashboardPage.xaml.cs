using System.ComponentModel;
using System.Numerics;
using DevHub.App.ViewModels;
using DevHub.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace DevHub.App.Views;

public sealed partial class DashboardPage : Page
{
    private readonly DispatcherTimer _usageTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _skyAnimationTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly PropertyChangedEventHandler _weatherChangedHandler;

    public DashboardPage()
    {
        ViewModel = App.Services.GetRequiredService<DashboardViewModel>();
        Updates = App.Services.GetRequiredService<UpdateViewModel>();
        InitializeComponent();

        _usageTimer.Tick += (_, _) => _ = ViewModel.RefreshUsageAsync();
        _skyAnimationTimer.Tick += (_, _) => SkyDecor.Invalidate();

        // The view model is a singleton but the page is recreated per navigation:
        // the handler must come off again, and the Win2D canvas must be torn down.
        _weatherChangedHandler = (_, e) =>
        {
            if (e.PropertyName == nameof(DashboardViewModel.Weather))
            {
                SkyDecor.Invalidate();
                UpdateSkyAnimation();
            }
        };
        ActualThemeChanged += (_, _) => SkyDecor.Invalidate();

        Loaded += async (_, _) =>
        {
            ViewModel.PropertyChanged += _weatherChangedHandler;
            SkyDecor.Invalidate();
            UpdateSkyAnimation();
            _usageTimer.Start();
            await ViewModel.LoadAsync();
        };
        Unloaded += (_, _) =>
        {
            _usageTimer.Stop();
            _skyAnimationTimer.Stop();
            ViewModel.PropertyChanged -= _weatherChangedHandler;
            SkyDecor.RemoveFromVisualTree();
        };
    }

    public DashboardViewModel ViewModel { get; }

    public UpdateViewModel Updates { get; }

    private void UpdateBanner_CloseButtonClick(InfoBar sender, object args) =>
        Updates.DismissCommand.Execute(null);

    private void RecentProject_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ProjectViewModel project)
        {
            Frame.Navigate(typeof(ProjectDetailPage), project.Project);
        }
    }

    private void OpenEnvironments_Click(object sender, RoutedEventArgs e) =>
        ShellPage.Current?.NavigateTo("environments");

    private void OpenSettings_Click(object sender, RoutedEventArgs e) =>
        ShellPage.Current?.NavigateTo("settings");

    private void WorkFeedBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        ViewModel.SelectedFeedIndex = sender.Items.IndexOf(sender.SelectedItem);
    }

    private void OpenReview_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string url && url.Length > 0)
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    // ============ Sky decoration ============

    private void SkyDecor_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (ViewModel.Weather is not { } weather)
        {
            return;
        }

        var dark = ActualTheme == ElementTheme.Dark;
        var width = (float)sender.Size.Width;
        var height = (float)sender.Size.Height;

        DrawSkyGradient(args.DrawingSession, width, height, weather, dark);
        if (!weather.IsDay)
        {
            DrawStars(args.DrawingSession, width, height, weather.WmoCode >= 2);
        }

        // The celestial shapes are drawn crisp into a command list, then blurred and faded.
        using var layer = new CanvasCommandList(sender);
        using (var session = layer.CreateDrawingSession())
        {
            DrawSky(session, weather, dark);
        }

        // Mostly opaque so the shapes clearly sit in front of the gradient, not under it.
        using var blurred = new GaussianBlurEffect { Source = layer, BlurAmount = 9 };
        using var faded = new OpacityEffect { Source = blurred, Opacity = 0.75f };
        args.DrawingSession.DrawImage(faded);

        // Animated precipitation, lightly blurred so it blends with the clouds.
        if (IsRainy(weather.WmoCode) || IsSnowy(weather.WmoCode))
        {
            var seconds = (Environment.TickCount64 % 3_600_000) / 1000f;
            using var precipitation = new CanvasCommandList(sender);
            using (var session = precipitation.CreateDrawingSession())
            {
                DrawPrecipitation(session, weather, dark, seconds);
            }

            using var softened = new GaussianBlurEffect { Source = precipitation, BlurAmount = 2.5f };
            using var precipFaded = new OpacityEffect { Source = softened, Opacity = 0.8f };
            args.DrawingSession.DrawImage(precipFaded);
        }
    }

    private static bool IsRainy(int code) =>
        code is (>= 51 and <= 67) or (>= 80 and <= 82) or >= 95;

    private static bool IsSnowy(int code) => code is (>= 71 and <= 77) or 85 or 86;

    /// <summary>Starts/stops the ~30 fps redraw loop — only spins while precipitation shows.</summary>
    private void UpdateSkyAnimation()
    {
        var weather = ViewModel.Weather;
        var animate = IsLoaded
            && weather is not null
            && (IsRainy(weather.WmoCode) || IsSnowy(weather.WmoCode));
        if (animate)
        {
            _skyAnimationTimer.Start();
        }
        else
        {
            _skyAnimationTimer.Stop();
        }
    }

    private static void DrawPrecipitation(
        CanvasDrawingSession session, WeatherInfo weather, bool dark, float seconds)
    {
        var center = new Vector2(180, 140);
        var code = weather.WmoCode;
        var cloudBottom = code <= 2 ? 96f : 68f;

        if (IsRainy(code))
        {
            // Drops fall along their slant and loop; alpha fades as they near the ground.
            var drop = dark ? Color.FromArgb(255, 118, 168, 228) : Color.FromArgb(255, 88, 148, 218);
            var direction = Vector2.Normalize(new Vector2(-9, 24));
            const float loop = 90f;
            for (var i = 0; i < 8; i++)
            {
                var distance = (seconds * 110 + i * 41.3f) % loop;
                var start = center + new Vector2(-48 + i * 17, cloudBottom) + direction * distance;
                var fade = 1 - distance / loop;
                var color = Color.FromArgb((byte)(220 * (0.35 + 0.65 * fade)), drop.R, drop.G, drop.B);
                session.DrawLine(start, start + direction * 20, color, 5);
            }
        }

        if (IsSnowy(code))
        {
            // Flakes drift down slowly and sway sideways.
            var flake = dark ? Color.FromArgb(255, 225, 232, 245) : Color.FromArgb(255, 198, 208, 228);
            const float loop = 100f;
            for (var i = 0; i < 10; i++)
            {
                var fall = (seconds * 34 + i * 29.7f) % loop;
                var sway = MathF.Sin(seconds * 1.6f + i * 1.3f) * 9;
                var position = center + new Vector2(-52 + i * 13 + sway, cloudBottom + 4 + fall);
                var fade = 1 - fall / loop;
                var radius = 3.5f + i % 3 * 1.4f;
                session.FillCircle(
                    position, radius,
                    Color.FromArgb((byte)(230 * (0.3 + 0.7 * fade)), flake.R, flake.G, flake.B));
            }
        }

        // Thunderstorm: a bolt flashes for ~180 ms every few seconds.
        if (code >= 95 && seconds % 3.7f < 0.18f)
        {
            var bolt = new[]
            {
                center + new Vector2(18, cloudBottom - 10),
                center + new Vector2(2, cloudBottom + 26),
                center + new Vector2(14, cloudBottom + 24),
                center + new Vector2(-6, cloudBottom + 62),
                center + new Vector2(10, cloudBottom + 30),
                center + new Vector2(-2, cloudBottom + 32),
                center + new Vector2(12, cloudBottom - 10),
            };
            using var geometry = CanvasGeometry.CreatePolygon(session, bolt);
            session.DrawGeometry(geometry, Color.FromArgb(90, 255, 236, 130), 8);
            session.FillGeometry(geometry, Color.FromArgb(235, 255, 224, 102));
        }
    }

    /// <summary>Condition-tinted sky, fading to transparent toward the page content.</summary>
    private static void DrawSkyGradient(
        CanvasDrawingSession session, float width, float height, WeatherInfo weather, bool dark)
    {
        var top = SkyTopColor(weather, dark);
        var stops = new CanvasGradientStop[]
        {
            new() { Position = 0f, Color = top },
            new() { Position = 1f, Color = Color.FromArgb(0, top.R, top.G, top.B) },
        };
        using var brush = new CanvasLinearGradientBrush(session, stops)
        {
            StartPoint = new Vector2(0, 0),
            EndPoint = new Vector2(0, height),
        };
        session.FillRectangle(0, 0, width, height, brush);
    }

    private static Color SkyTopColor(WeatherInfo weather, bool dark)
    {
        var code = weather.WmoCode;
        var rainy = code is (>= 51 and <= 67) or (>= 80 and <= 82) or >= 95;
        var snowy = code is (>= 71 and <= 77) or 85 or 86;

        var color = weather.IsDay
            ? (rainy, snowy, code) switch
            {
                (true, _, _) => Color.FromArgb(110, 92, 113, 133),   // rain: slate
                (_, true, _) => Color.FromArgb(100, 174, 185, 196),  // snow: pale gray
                (_, _, 3) or (_, _, 45) or (_, _, 48) => Color.FromArgb(100, 140, 153, 166), // overcast/fog
                (_, _, 2) => Color.FromArgb(100, 111, 163, 204),     // partly cloudy
                _ => Color.FromArgb(100, 61, 139, 212),              // blue sky
            }
            : code >= 2
                ? Color.FromArgb(130, 30, 38, 51)                    // cloudy night
                : Color.FromArgb(130, 22, 36, 74);                   // clear night

        // Dark backgrounds swallow translucent tints; push a little harder.
        return dark ? Color.FromArgb((byte)Math.Min(color.A + 30, 255), color.R, color.G, color.B) : color;
    }

    /// <summary>Deterministic star field, denser and brighter on clear nights.</summary>
    private static void DrawStars(CanvasDrawingSession session, float width, float height, bool cloudyNight)
    {
        var random = new Random(1234);
        var count = cloudyNight ? 28 : 70;
        for (var i = 0; i < count; i++)
        {
            var x = (float)(random.NextDouble() * width);
            var y = (float)(random.NextDouble() * height * 0.72);
            var radius = (float)(random.NextDouble() * 1.6 + 0.7);

            // Stars fade out before the gradient does, so none float on a clear background.
            var fade = Math.Clamp(1 - y / (height * 0.8f), 0, 1);
            var alpha = (byte)(random.Next(90, 200) * fade * (cloudyNight ? 0.55 : 1));
            session.FillCircle(new Vector2(x, y), radius, Color.FromArgb(alpha, 255, 255, 255));
        }
    }

    private static void DrawSky(CanvasDrawingSession session, WeatherInfo weather, bool dark)
    {
        var center = new Vector2(180, 140);
        const float radius = 70;

        var code = weather.WmoCode;
        var cloudy = code >= 2;

        // The sun/moon only shows through clear-ish skies (clear, mainly clear, partly
        // cloudy); overcast, fog and any precipitation are clouds-only.
        var showCelestialBody = code <= 2;

        if (showCelestialBody && weather.IsDay)
        {
            var sun = dark ? Color.FromArgb(255, 255, 186, 60) : Color.FromArgb(255, 255, 200, 64);
            session.FillCircle(center, radius + 28, Color.FromArgb(50, sun.R, sun.G, sun.B));
            session.FillCircle(center, radius, sun);
        }
        else if (showCelestialBody)
        {
            var moon = dark ? Color.FromArgb(255, 205, 215, 235) : Color.FromArgb(255, 165, 180, 212);
            using var full = CanvasGeometry.CreateCircle(session, center, radius);
            using var bite = CanvasGeometry.CreateCircle(session, center + new Vector2(34, -24), radius - 8);
            using var crescent = full.CombineWith(bite, Matrix3x2.Identity, CanvasGeometryCombine.Exclude);
            session.FillGeometry(crescent, moon);
        }

        if (cloudy)
        {
            var cloud = dark ? Color.FromArgb(255, 148, 158, 174) : Color.FromArgb(255, 226, 231, 240);
            if (showCelestialBody)
            {
                // Partly cloudy: one cloud drifting in front of the sun/moon.
                DrawCloud(session, center + new Vector2(30, 38), 1.0f, cloud);
            }
            else
            {
                // Clouds-only skies: a bigger main cloud, centered, with a companion.
                DrawCloud(session, center + new Vector2(6, 10), 1.15f, cloud);
                DrawCloud(session, center + new Vector2(-78, -18), 0.65f, cloud);
            }
        }

        // Rain, snow and lightning live in the animated layer (DrawPrecipitation).
    }

    private static void DrawCloud(CanvasDrawingSession session, Vector2 at, float scale, Color color)
    {
        session.FillCircle(at + new Vector2(-36, 8) * scale, 27 * scale, color);
        session.FillCircle(at + new Vector2(0, -10) * scale, 35 * scale, color);
        session.FillCircle(at + new Vector2(38, 6) * scale, 29 * scale, color);
        session.FillEllipse(at + new Vector2(0, 16) * scale, 64 * scale, 21 * scale, color);
    }
}
