using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace TaskWidget;

public static class Fx
{
    public static readonly DependencyProperty EnterProperty = DependencyProperty.RegisterAttached(
        "Enter", typeof(bool), typeof(Fx), new PropertyMetadata(false, (element, args) =>
        {
            if ((bool)args.NewValue)
                ApplyEnter((UIElement)element);
        }));

    public static bool GetEnter(UIElement element) => (bool)element.GetValue(EnterProperty);

    public static void SetEnter(UIElement element, bool value) => element.SetValue(EnterProperty, value);

    static void ApplyEnter(UIElement element)
    {
        var compositor = ElementCompositionPreview.GetElementVisual(element).Compositor;
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var showFor = TimeSpan.FromMilliseconds(Resource("EnterMs"));
        var show = compositor.CreateAnimationGroup();
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Target = "Opacity";
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, Ease(compositor, "EaseOut"));
        fade.Duration = showFor;
        var slide = compositor.CreateScalarKeyFrameAnimation();
        slide.Target = "Translation.Y";
        slide.InsertKeyFrame(0, -(float)Resource("EnterSlidePx"));
        slide.InsertKeyFrame(1, 0, Ease(compositor, "EaseOut"));
        slide.Duration = showFor;
        show.Add(fade);
        show.Add(slide);
        var hide = compositor.CreateScalarKeyFrameAnimation();
        hide.Target = "Opacity";
        hide.InsertKeyFrame(1, 0, Ease(compositor, "EaseIn"));
        hide.Duration = TimeSpan.FromMilliseconds(Resource("LeaveMs"));
        ElementCompositionPreview.SetImplicitShowAnimation(element, show);
        ElementCompositionPreview.SetImplicitHideAnimation(element, hide);
    }

    static CompositionEasingFunction Ease(Compositor compositor, string name) =>
        compositor.CreateCubicBezierEasingFunction(
            new Vector2((float)Resource(name + "X1"), (float)Resource(name + "Y1")),
            new Vector2((float)Resource(name + "X2"), (float)Resource(name + "Y2")));

    static double Resource(string key) => (double)Application.Current.Resources[key];
}
