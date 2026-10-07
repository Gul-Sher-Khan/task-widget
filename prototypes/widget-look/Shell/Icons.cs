// PROTOTYPE: the Priority badge and Effort duration chip (picked in round 2), drawn in code.
// Priority keeps a distinct shape per level (filled "!" / half ring / empty ring) so it reads in contrast themes too,
// where every level shares the system text colour.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Look;

public sealed class PriIcon : Grid
{
    public static readonly DependencyProperty PriorityProperty = DependencyProperty.Register(
        nameof(Priority), typeof(Pri), typeof(PriIcon), new PropertyMetadata(Pri.Low, (d, _) => ((PriIcon)d).Draw()));
    public Pri Priority { get => (Pri)GetValue(PriorityProperty); set => SetValue(PriorityProperty, value); }

    public PriIcon() { Width = 16; Height = 16; Draw(); }

    void Draw()
    {
        Children.Clear();
        var c = new Canvas { Width = 16, Height = 16 };
        var brush = F.Pri(Priority);
        if (Priority == Pri.High)
        {
            c.Children.Add(new Path { Data = Geo("M8,1.5 A6.5,6.5 0 1 1 7.99,1.5 Z"), Fill = brush });
            c.Children.Add(new Path { Data = Geo("M8,4.6 V8.6"), Stroke = Shell.Res("OnPriBrush"), StrokeThickness = 1.9, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            var dot = new Ellipse { Width = 2.2, Height = 2.2, Fill = Shell.Res("OnPriBrush") };
            Canvas.SetLeft(dot, 6.9); Canvas.SetTop(dot, 10.2);
            c.Children.Add(dot);
        }
        else
        {
            c.Children.Add(new Path { Data = Geo("M8,2.2 A5.8,5.8 0 1 1 7.99,2.2 Z"), Stroke = brush, StrokeThickness = 1.6 });
            if (Priority == Pri.Medium) c.Children.Add(new Path { Data = Geo("M8,4.6 A3.4,3.4 0 0 1 8,11.4 Z"), Fill = brush });
        }
        Children.Add(c);
        ToolTipService.SetToolTip(this, $"{Priority} priority");
    }

    static Geometry Geo(string d) => (Geometry)XamlReader.Load(
        $"<Geometry xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">{d}</Geometry>");
}

public sealed class EffIcon : Grid
{
    public static readonly DependencyProperty EffortProperty = DependencyProperty.Register(
        nameof(Effort), typeof(Eff), typeof(EffIcon), new PropertyMetadata(Eff.Quick, (d, _) => ((EffIcon)d).Draw()));
    public Eff Effort { get => (Eff)GetValue(EffortProperty); set => SetValue(EffortProperty, value); }

    readonly TextBlock label = new() { FontSize = 11 };

    public EffIcon()
    {
        VerticalAlignment = VerticalAlignment.Center;
        var chip = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 1, 7, 2), Background = Shell.Res("ChipBrush"), Child = label };
        label.Foreground = Shell.Res("MutedTextBrush");
        Children.Add(chip);
        Draw();
    }

    void Draw()
    {
        label.Text = F.EffSpan(Effort);
        ToolTipService.SetToolTip(this, Effort switch { Eff.Quick => "Quick · 15 min or less", Eff.Short => "Short · up to an hour", _ => "Long · more than an hour" });
    }
}
