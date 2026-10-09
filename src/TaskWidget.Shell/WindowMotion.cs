using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace TaskWidget;

// Window-level motion from the prototype's Shell/Windows.cs, driven by Motion.xaml.
static class WindowMotion
{
    public static double Ms(string key) => (double)Application.Current.Resources[key];

    public static CompositionEasingFunction Ease(Compositor compositor, bool arrive)
    {
        string prefix = arrive ? "EaseOut" : "EaseIn";
        return compositor.CreateCubicBezierEasingFunction(
            new Vector2((float)Ms(prefix + "X1"), (float)Ms(prefix + "Y1")),
            new Vector2((float)Ms(prefix + "X2"), (float)Ms(prefix + "Y2")));
    }

    // Fade and slide the content; the backdrop itself can't be faded on a WinUI 3 window.
    public static void AnimateContent(UIElement element, bool show, double ms, Action? done = null)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var compositor = visual.Compositor;
        var ease = Ease(compositor, show);
        float slide = -(float)Ms("WidgetSlidePx");
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        var opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(0, show ? 0 : 1);
        opacity.InsertKeyFrame(1, show ? 1 : 0, ease);
        opacity.Duration = TimeSpan.FromMilliseconds(ms);
        var translation = compositor.CreateScalarKeyFrameAnimation();
        translation.InsertKeyFrame(0, show ? slide : 0);
        translation.InsertKeyFrame(1, show ? 0 : slide, ease);
        translation.Duration = TimeSpan.FromMilliseconds(ms);
        visual.StartAnimation("Opacity", opacity);
        visual.StartAnimation("Translation.Y", translation);
        batch.End();
        if (done is not null)
            batch.Completed += (_, _) => done();
    }

    // The Dock arrives scaling up from DockSpringFrom on a spring.
    public static void SpringIn(UIElement element, double widthDip)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.CenterPoint = new Vector3((float)(widthDip / 2), 0, 0);
        var compositor = visual.Compositor;
        var spring = compositor.CreateSpringVector3Animation();
        float from = (float)Ms("DockSpringFrom");
        spring.InitialValue = new Vector3(from, from, 1);
        spring.FinalValue = Vector3.One;
        spring.DampingRatio = (float)Ms("DockSpringDamping");
        spring.Period = TimeSpan.FromMilliseconds(Ms("DockSpringPeriodMs"));
        visual.StartAnimation("Scale", spring);
    }
}
