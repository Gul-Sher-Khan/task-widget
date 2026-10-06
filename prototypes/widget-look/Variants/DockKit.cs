// PROTOTYPE: the Dock's "a Capture is processing" animations. Composition-only, stopped when idle.
using System;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Look;

public static class DockKit
{
    // Runs `start` while Store.IsProcessing is true and resets the visual afterwards.
    public static void WhileProcessing(FrameworkElement owner, UIElement target, Action<Visual, bool> anim)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(target, true);
        void Sync() => anim(ElementCompositionPreview.GetElementVisual(target), Shell.Store.IsProcessing);
        System.ComponentModel.PropertyChangedEventHandler h = (_, e) => { if (e.PropertyName == nameof(Store.IsProcessing)) Sync(); };
        owner.Loaded += (_, _) => { Shell.Store.PropertyChanged += h; Sync(); };
        owner.Unloaded += (_, _) => Shell.Store.PropertyChanged -= h;
    }

    public static void Breathe(Visual v, bool on)
    {
        if (!on) { v.StopAnimation("Opacity"); v.Opacity = 1; return; }
        var c = v.Compositor;
        var a = c.CreateScalarKeyFrameAnimation();
        var ease = c.CreateCubicBezierEasingFunction(new(0.45f, 0f), new(0.55f, 1f));
        a.InsertKeyFrame(0, 1); a.InsertKeyFrame(0.5f, 0.35f, ease); a.InsertKeyFrame(1, 1, ease);
        a.Duration = TimeSpan.FromMilliseconds(1400);
        a.IterationBehavior = AnimationIterationBehavior.Forever;
        v.StartAnimation("Opacity", a);
    }

    // A short accent bar sweeping left to right under the icons.
    public static void Sweep(Visual v, bool on, float width)
    {
        if (!on) { v.StopAnimation("Translation.X"); v.StopAnimation("Opacity"); v.Opacity = 0; return; }
        var c = v.Compositor;
        v.Opacity = 1;
        var a = c.CreateScalarKeyFrameAnimation();
        var ease = c.CreateCubicBezierEasingFunction(new(0.45f, 0f), new(0.55f, 1f));
        a.InsertKeyFrame(0, -24); a.InsertKeyFrame(1, width, ease);
        a.Duration = TimeSpan.FromMilliseconds(1100);
        a.IterationBehavior = AnimationIterationBehavior.Forever;
        v.StartAnimation("Translation.X", a);
    }

    // Each glyph bobs in turn.
    public static void Wave(Visual v, bool on, int index)
    {
        if (!on) { v.StopAnimation("Translation.Y"); v.Properties.InsertVector3("Translation", Vector3.Zero); return; }
        var c = v.Compositor;
        var a = c.CreateScalarKeyFrameAnimation();
        var ease = c.CreateCubicBezierEasingFunction(new(0.45f, 0f), new(0.55f, 1f));
        a.InsertKeyFrame(0, 0); a.InsertKeyFrame(0.18f, -3, ease); a.InsertKeyFrame(0.36f, 0, ease); a.InsertKeyFrame(1, 0);
        a.Duration = TimeSpan.FromMilliseconds(1200);
        a.DelayTime = TimeSpan.FromMilliseconds(index * 140);
        a.IterationBehavior = AnimationIterationBehavior.Forever;
        v.StartAnimation("Translation.Y", a);
    }
}
