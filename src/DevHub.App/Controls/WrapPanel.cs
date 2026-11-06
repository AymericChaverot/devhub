using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace DevHub.App.Controls;

/// <summary>Lays children out left to right, starting a new line whenever the next one doesn't fit.</summary>
public sealed partial class WrapPanel : Panel
{
    public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(
        nameof(HorizontalSpacing), typeof(double), typeof(WrapPanel), new PropertyMetadata(0d, OnSpacingChanged));

    public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(
        nameof(VerticalSpacing), typeof(double), typeof(WrapPanel), new PropertyMetadata(0d, OnSpacingChanged));

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        }

        return Layout(availableSize.Width, arrange: false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Layout(finalSize.Width, arrange: true);
        return finalSize;
    }

    /// <summary>Walks the children line by line; arranges them when asked, and returns the extent used.</summary>
    private Size Layout(double maxWidth, bool arrange)
    {
        var line = new List<UIElement>();
        double x = 0, y = 0, lineHeight = 0, widest = 0;

        void FlushLine()
        {
            if (arrange)
            {
                var left = 0d;
                foreach (var element in line)
                {
                    var size = element.DesiredSize;
                    element.Arrange(new Rect(left, y + ((lineHeight - size.Height) / 2), size.Width, size.Height));
                    left += size.Width + HorizontalSpacing;
                }
            }

            widest = Math.Max(widest, x);
            line.Clear();
        }

        foreach (var child in Children.Where(c => c.Visibility == Visibility.Visible))
        {
            var size = child.DesiredSize;
            if (line.Count > 0 && x + HorizontalSpacing + size.Width > maxWidth)
            {
                FlushLine();
                y += lineHeight + VerticalSpacing;
                x = 0;
                lineHeight = 0;
            }

            x += (line.Count > 0 ? HorizontalSpacing : 0) + size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
            line.Add(child);
        }

        if (line.Count == 0)
        {
            return new Size(0, 0);
        }

        FlushLine();
        return new Size(widest, y + lineHeight);
    }

    private static void OnSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((WrapPanel)d).InvalidateMeasure();
}
