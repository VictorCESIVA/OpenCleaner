using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OpenCleaner.UI;

public static class UiMotion
{
    public static Duration InteractionDuration => new(TimeSpan.FromMilliseconds(
        SystemParameters.ClientAreaAnimation ? 140 : 0));

    internal static void Enter(FrameworkElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.RenderTransform = new TranslateTransform();
        if (!element.IsLoaded || !SystemParameters.ClientAreaAnimation) return;

        var duration = new Duration(TimeSpan.FromMilliseconds(240));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration)
        {
            EasingFunction = easing, FillBehavior = FillBehavior.Stop
        });
        element.RenderTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, duration)
        {
            EasingFunction = easing, FillBehavior = FillBehavior.Stop
        });
    }
}
