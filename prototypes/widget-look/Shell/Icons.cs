// PROTOTYPE: Priority and Effort icons, drawn as vectors in code. The set is picked from Shell.Prefs (a design switch).
// Priority keeps distinct shapes per level so it still reads in contrast themes, where colour is dropped.
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
        int level = 3 - (int)Priority; // High 3, Medium 2, Low 1
        switch (Shell.Prefs.PriStyle)
        {
            case 0: // signal bars
                for (int i = 0; i < 3; i++)
                {
                    double h = 5 + i * 4;
                    var r = new Rectangle { Width = 3.2, Height = h, RadiusX = 1, RadiusY = 1, Fill = brush, Opacity = i < level ? 1 : 0.22 };
                    Canvas.SetLeft(r, 1.5 + i * 4.8); Canvas.SetTop(r, 14 - h);
                    c.Children.Add(r);
                }
                break;
            case 1: // chevrons
                string d = Priority switch
                {
                    Pri.High => "M3,8.5 L8,3.5 L13,8.5 M3,13 L8,8 L13,13",
                    Pri.Medium => "M3.5,6 H12.5 M3.5,10.5 H12.5",
                    _ => "M3,6 L8,11 L13,6",
                };
                c.Children.Add(new Path { Data = Geo(d), Stroke = brush, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round });
                break;
            default: // alert badges: filled alert, half-filled ring, empty ring
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
                break;
        }
        Children.Add(c);
        ToolTipService.SetToolTip(this, $"{Priority} priority");
    }

    internal static Geometry Geo(string d) => (Geometry)XamlReader.Load(
        $"<Geometry xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">{d}</Geometry>");
}

public sealed class EffIcon : Grid
{
    public static readonly DependencyProperty EffortProperty = DependencyProperty.Register(
        nameof(Effort), typeof(Eff), typeof(EffIcon), new PropertyMetadata(Eff.Quick, (d, _) => ((EffIcon)d).Draw()));
    public Eff Effort { get => (Eff)GetValue(EffortProperty); set => SetValue(EffortProperty, value); }

    public EffIcon() { VerticalAlignment = VerticalAlignment.Center; Draw(); }

    void Draw()
    {
        Children.Clear();
        var brush = Shell.Res("MutedTextBrush");
        switch (Shell.Prefs.EffStyle)
        {
            case 0: // pie clock
            {
                var c = new Canvas { Width = 16, Height = 16 };
                c.Children.Add(new Path { Data = PriIcon.Geo("M8,1.8 A6.2,6.2 0 1 1 7.99,1.8 Z"), Stroke = brush, StrokeThickness = 1.3 });
                string pie = Effort switch
                {
                    Eff.Quick => "M8,8 L8,3.6 A4.4,4.4 0 0 1 12.4,8 Z",
                    Eff.Short => "M8,8 L8,3.6 A4.4,4.4 0 0 1 8,12.4 Z",
                    _ => "M8,3.6 A4.4,4.4 0 1 1 7.99,3.6 Z",
                };
                c.Children.Add(new Path { Data = PriIcon.Geo(pie), Fill = brush });
                Children.Add(c);
                break;
            }
            case 1: // duration
                Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 1, 7, 2),
                    Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
                    Child = new TextBlock { Text = F.EffSpan(Effort), FontSize = 11, Foreground = brush },
                });
                break;
            default: // symbols: bolt, clock, hourglass
            {
                var c = new Canvas { Width = 16, Height = 16 };
                if (Effort == Eff.Quick)
                    c.Children.Add(new Path { Data = PriIcon.Geo("M9.2,1.5 L3.6,9.2 H7.6 L6.8,14.5 L12.4,6.8 H8.4 Z"), Fill = brush });
                else if (Effort == Eff.Short)
                {
                    c.Children.Add(new Path { Data = PriIcon.Geo("M8,1.8 A6.2,6.2 0 1 1 7.99,1.8 Z"), Stroke = brush, StrokeThickness = 1.4 });
                    c.Children.Add(new Path { Data = PriIcon.Geo("M8,4.6 V8 L10.4,9.6"), Stroke = brush, StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round });
                }
                else
                {
                    c.Children.Add(new Path { Data = PriIcon.Geo("M4,1.8 H12 M4,14.2 H12 M5.2,1.8 C5.2,5.6 8,6.6 8,8 C8,9.4 5.2,10.4 5.2,14.2 M10.8,1.8 C10.8,5.6 8,6.6 8,8 C8,9.4 10.8,10.4 10.8,14.2"),
                        Stroke = brush, StrokeThickness = 1.4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                    c.Children.Add(new Path { Data = PriIcon.Geo("M6.4,13.2 L8,10.6 L9.6,13.2 Z"), Fill = brush });
                }
                Children.Add(c);
                break;
            }
        }
        ToolTipService.SetToolTip(this, Effort switch { Eff.Quick => "Quick · 15 min or less", Eff.Short => "Short · up to an hour", _ => "Long · more than an hour" });
    }
}
