using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace StartDX.Dock.Theming;

/// <summary>
/// Theme-driven motion. Storyboards inside templates cannot use DynamicResource for durations/targets, so hover/press
/// animation is an attached behaviour that reads the *current* theme's tokens each time it runs:
/// switch themes and the very next hover already uses the new scale and timing.
/// </summary>
public static class Motion
{
    public static readonly DependencyProperty HoverProperty = DependencyProperty.RegisterAttached(
        "Hover", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnHoverChanged));

    public static bool GetHover(DependencyObject d) => (bool)d.GetValue(HoverProperty);
    public static void SetHover(DependencyObject d, bool v) => d.SetValue(HoverProperty, v);

    private static void OnHoverChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;

        if ((bool)e.NewValue)
        {
            fe.RenderTransformOrigin = new Point(0.5, 0.5);
            fe.RenderTransform = new ScaleTransform(1, 1);
            fe.MouseEnter += OnEnter;
            fe.MouseLeave += OnLeave;
            fe.PreviewMouseLeftButtonDown += OnDown;
            fe.PreviewMouseLeftButtonUp += OnUp;
        }
        else
        {
            fe.MouseEnter -= OnEnter;
            fe.MouseLeave -= OnLeave;
            fe.PreviewMouseLeftButtonDown -= OnDown;
            fe.PreviewMouseLeftButtonUp -= OnUp;
        }
    }

    private static void OnEnter(object s, MouseEventArgs e) => Animate((FrameworkElement)s, HoverTarget(), 1.0);
    private static void OnLeave(object s, MouseEventArgs e) => Animate((FrameworkElement)s, 1.0, 1.0);
    private static void OnDown(object s, MouseButtonEventArgs e) => Animate((FrameworkElement)s, Math.Min(0.97, HoverTarget() - 0.06), 0.6);
    private static void OnUp(object s, MouseButtonEventArgs e) =>
        Animate((FrameworkElement)s, ((FrameworkElement)s).IsMouseOver ? HoverTarget() : 1.0, 0.6);

    private static double HoverTarget() => ThemeManager.Instance.Get(ThemeKeys.HoverScale, 1.04);

    private static void Animate(FrameworkElement fe, double to, double speedFactor)
    {
        if (fe.RenderTransform is not ScaleTransform st) return;
        var ms = ThemeManager.Instance.Get(ThemeKeys.HoverMs, 140.0) * speedFactor;
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(Math.Max(1, ms)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }
}
