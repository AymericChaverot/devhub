using DevHub.App.ViewModels;
using DevHub.Core.Models;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Svg;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Serilog;
using Windows.Foundation;

namespace DevHub.App.Views;

/// <summary>The two blurred brand logos behind the hero: the project's stack and its chosen IDE.</summary>
public sealed partial class ProjectDetailPage
{
    private const double DecorOpacity = 0.20;

    private string? _langDecorPath;
    private string? _ideDecorPath;
    private CanvasSvgDocument? _langSvg;
    private CanvasSvgDocument? _ideSvg;

    private void ShowTechDecoration(Project project)
    {
        // Framework logo preferred over language; among those, prefer brands whose
        // color actually shows on the card (near-black logos vanish on dark Mica).
        var candidates = project.Frameworks
            .Select(f => BadgeVisuals.ForFramework(f.Name).Slug)
            .Concat(project.Languages.Select(l => BadgeVisuals.ForLanguage(l.Language).Slug))
            .Where(s => s is not null)
            .ToList();

        var slug = candidates.FirstOrDefault(s => !HasLightVariant(s!)) ?? candidates.FirstOrDefault();
        _langDecorPath = BadgeVisuals.DecorSvgPath(slug);
        _langSvg = null;
        LangDecor.Invalidate();
    }

    private void ShowIdeDecoration(IdeInfo? ide)
    {
        _ideDecorPath = ide is null ? null : BadgeVisuals.DecorSvgPath(BadgeVisuals.IdeSlug(ide.Id));
        _ideSvg = null;
        IdeDecor.Invalidate();
    }

    /// <summary>Smooth swap: fade the IDE logo out, repoint it, fade it back in.</summary>
    private async Task CrossfadeIdeDecorationAsync(IdeInfo ide)
    {
        await AnimateOpacityAsync(IdeDecor, 0, TimeSpan.FromMilliseconds(140));
        ShowIdeDecoration(ide);
        await AnimateOpacityAsync(IdeDecor, DecorOpacity, TimeSpan.FromMilliseconds(280));
    }

    /// <summary>
    /// Anchors the logos to the card: the stack logo half-sunk below the bottom-left corner,
    /// the IDE logo half-raised above the top edge, left of the primary actions.
    /// </summary>
    private void PlaceDecorations(Size cardSize)
    {
        const double langLeft = 8;
        const double ideRightInset = 220;

        Canvas.SetLeft(LangDecor, langLeft);
        Canvas.SetTop(LangDecor, cardSize.Height - (LangDecor.Height / 2));
        Canvas.SetLeft(IdeDecor, cardSize.Width - ideRightInset - IdeDecor.Width);
        Canvas.SetTop(IdeDecor, -IdeDecor.Height / 2);
    }

    /// <summary>The logos drift, grow and dim slightly as the hero collapses (0 = expanded, 1 = collapsed).</summary>
    private void ApplyDecorProgress(double progress)
    {
        LangDecorTransform.ScaleX = LangDecorTransform.ScaleY = 1 + (0.45 * progress);
        LangDecorTransform.TranslateX = 70 * progress;
        LangDecorTransform.TranslateY = -45 * progress;
        LangDecorTransform.Rotation = -10 + (16 * progress);
        IdeDecorTransform.ScaleX = IdeDecorTransform.ScaleY = 1 + (0.4 * progress);
        IdeDecorTransform.TranslateX = -60 * progress;
        IdeDecorTransform.TranslateY = 35 * progress;
        IdeDecorTransform.Rotation = 10 - (16 * progress);
        LangDecor.Opacity = DecorOpacity * (1 - (0.35 * progress));
    }


    /// <summary>A "-light" variant exists exactly for brands that are too dark to see.</summary>
    private static bool HasLightVariant(string slug) =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "ToolIcons", $"{slug}-light.svg"));

    private void LangDecor_Draw(CanvasControl sender, CanvasDrawEventArgs args) =>
        DrawBlurredSvg(sender, args, _langDecorPath, ref _langSvg);

    private void IdeDecor_Draw(CanvasControl sender, CanvasDrawEventArgs args) =>
        DrawBlurredSvg(sender, args, _ideDecorPath, ref _ideSvg);

    /// <summary>Draws the SVG into a command list and runs it through a real gaussian blur.</summary>
    private static void DrawBlurredSvg(
        CanvasControl sender, CanvasDrawEventArgs args, string? svgPath, ref CanvasSvgDocument? cache)
    {
        if (svgPath is null)
        {
            return;
        }

        try
        {
            cache ??= CanvasSvgDocument.LoadFromXml(sender, File.ReadAllText(svgPath));

            // Padding around the glyph so the blur can bleed without hard clipping.
            var side = (float)Math.Min(sender.ActualWidth, sender.ActualHeight);
            var padding = side * 0.15f;
            var glyphSide = side - (2 * padding);

            var commands = new CanvasCommandList(sender);
            using (var session = commands.CreateDrawingSession())
            {
                session.DrawSvg(cache, new Windows.Foundation.Size(glyphSide, glyphSide), padding, padding);
            }

            args.DrawingSession.DrawImage(new GaussianBlurEffect
            {
                Source = commands,
                BlurAmount = side / 28f,
                BorderMode = EffectBorderMode.Soft,
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not draw hero decoration {Path}", svgPath);
        }
    }

    private static Task AnimateOpacityAsync(UIElement element, double to, TimeSpan duration)
    {
        var completion = new TaskCompletionSource();
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = duration,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");

        var storyboard = new Storyboard { Children = { animation } };
        storyboard.Completed += (_, _) => completion.TrySetResult();
        storyboard.Begin();
        return completion.Task;
    }
}
