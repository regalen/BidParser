using System.Windows;

namespace BidParser.Desktop.Mvvm;

/// <summary>
/// Keeps per-keystroke validation separate from focus-loss formatting for numeric fields.
/// </summary>
public static class NumericInputBehavior
{
    public static readonly DependencyProperty NormalizeOnLostFocusProperty = DependencyProperty.RegisterAttached(
        "NormalizeOnLostFocus",
        typeof(bool),
        typeof(NumericInputBehavior),
        new PropertyMetadata(false, OnNormalizeOnLostFocusChanged));

    public static bool GetNormalizeOnLostFocus(DependencyObject element)
        => (bool)element.GetValue(NormalizeOnLostFocusProperty);

    public static void SetNormalizeOnLostFocus(DependencyObject element, bool value)
        => element.SetValue(NormalizeOnLostFocusProperty, value);

    private static void OnNormalizeOnLostFocusChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not FrameworkElement target)
        {
            return;
        }

        target.LostFocus -= OnLostFocus;
        if (e.NewValue is true)
        {
            target.LostFocus += OnLostFocus;
        }
    }

    private static void OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NumericFieldViewModel field })
        {
            field.Normalize();
        }
    }
}
