// PROTOTYPE: throwaway.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace Look;

public sealed partial class UndoPill : UserControl
{
    public Store S => Shell.Store;

    public UndoPill()
    {
        InitializeComponent();
        var c = ElementCompositionPreview.GetElementVisual(this).Compositor;
        var show = c.CreateAnimationGroup();
        var fade = c.CreateScalarKeyFrameAnimation(); fade.Target = "Opacity";
        fade.InsertKeyFrame(0, 0); fade.InsertKeyFrame(1, 1); fade.Duration = System.TimeSpan.FromMilliseconds(180);
        var rise = c.CreateScalarKeyFrameAnimation(); rise.Target = "Translation.Y";
        rise.InsertKeyFrame(0, 12); rise.InsertKeyFrame(1, 0, c.CreateCubicBezierEasingFunction(new(0.1f, 0.9f), new(0.2f, 1f)));
        rise.Duration = System.TimeSpan.FromMilliseconds(260);
        show.Add(fade); show.Add(rise);
        var hide = c.CreateScalarKeyFrameAnimation(); hide.Target = "Opacity";
        hide.InsertKeyFrame(1, 0); hide.Duration = System.TimeSpan.FromMilliseconds(150);
        ElementCompositionPreview.SetIsTranslationEnabled(Pill, true);
        ElementCompositionPreview.SetImplicitShowAnimation(Pill, show);
        ElementCompositionPreview.SetImplicitHideAnimation(Pill, hide);
    }

    void Undo_Click(object sender, RoutedEventArgs e) => S.Undo();
}
