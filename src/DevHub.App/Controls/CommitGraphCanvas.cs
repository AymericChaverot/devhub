using DevHub.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace DevHub.App.Controls;

/// <summary>Draws one commit row of the graph: colored rails plus the commit dot.</summary>
public sealed partial class CommitGraphCanvas : Canvas
{
    public const double LaneWidth = 14;
    public const double RowHeight = 36;

    private static readonly Color[] Palette =
    [
        Color.FromArgb(255, 0x4C, 0xC2, 0xFF), // blue
        Color.FromArgb(255, 0x6C, 0xCB, 0x5F), // green
        Color.FromArgb(255, 0xFF, 0xB9, 0x00), // amber
        Color.FromArgb(255, 0xE1, 0x71, 0xE8), // violet
        Color.FromArgb(255, 0xFF, 0x99, 0x66), // coral
        Color.FromArgb(255, 0x00, 0xB7, 0xC3), // teal
        Color.FromArgb(255, 0xF7, 0x63, 0x0C), // orange
        Color.FromArgb(255, 0x9A, 0xA0, 0xF5), // periwinkle
    ];

    public static readonly DependencyProperty RowProperty = DependencyProperty.Register(
        nameof(Row), typeof(object), typeof(CommitGraphCanvas), new PropertyMetadata(null, OnRowChanged));

    public object? Row
    {
        get => GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    private static void OnRowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CommitGraphCanvas)d).Rebuild();

    private void Rebuild()
    {
        Children.Clear();
        if (Row is not GitGraphRow row)
        {
            return;
        }

        Height = RowHeight;

        static double X(int lane) => (lane * LaneWidth) + (LaneWidth / 2);
        static SolidColorBrush BrushFor(int lane) => new(Palette[lane % Palette.Length]);

        foreach (var segment in row.TopSegments)
        {
            Children.Add(new Line
            {
                X1 = X(segment.From),
                Y1 = 0,
                X2 = X(segment.To),
                Y2 = RowHeight / 2,
                Stroke = BrushFor(segment.From),
                StrokeThickness = 2,
            });
        }

        foreach (var segment in row.BottomSegments)
        {
            Children.Add(new Line
            {
                X1 = X(segment.From),
                Y1 = RowHeight / 2,
                X2 = X(segment.To),
                Y2 = RowHeight,
                Stroke = BrushFor(segment.To),
                StrokeThickness = 2,
            });
        }

        var dot = new Ellipse
        {
            Width = 9,
            Height = 9,
            Fill = BrushFor(row.Lane),
        };
        SetLeft(dot, X(row.Lane) - 4.5);
        SetTop(dot, (RowHeight / 2) - 4.5);
        Children.Add(dot);
    }
}
