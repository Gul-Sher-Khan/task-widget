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
        fade.InsertKeyFrame(0, 0); fade.InsertKeyFrame(1, 1); fade.Duration = System.TimeSpan.FromMilliseconds(Shell.Ms("UndoFadeInMs"));
        var rise = c.CreateScalarKeyFrameAnimation(); rise.Target = "Translation.Y";
        rise.InsertKeyFrame(0, (float)Shell.Num("UndoRisePx")); rise.InsertKeyFrame(1, 0, Shell.EaseOut(c));
        rise.Duration = System.TimeSpan.FromMilliseconds(Shell.Ms("UndoInMs"));
        show.Add(fade); show.Add(rise);
        var hide = c.CreateScalarKeyFrameAnimation(); hide.Target = "Opacity";
        hide.InsertKeyFrame(1, 0); hide.Duration = System.TimeSpan.FromMilliseconds(Shell.Ms("UndoOutMs"));
        ElementCompositionPreview.SetIsTranslationEnabled(Pill, true);
        ElementCompositionPreview.SetImplicitShowAnimation(Pill, show);
        ElementCompositionPreview.SetImplicitHideAnimation(Pill, hide);
    }

    void Undo_Click(object sender, RoutedEventArgs e) => S.Undo();
}
