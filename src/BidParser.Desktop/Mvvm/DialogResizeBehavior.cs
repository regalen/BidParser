using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace BidParser.Desktop.Mvvm;

/// <summary>
/// Lets an in-window overlay dialog be resized from a corner grip. Mark the dialog root with
/// <c>IsResizable</c> and a <see cref="Thumb"/> inside it with <c>IsGrip</c>. The dialog keeps
/// <see cref="double.NaN"/> Width/Height (fill the host) until the grip is dragged; double-clicking
/// the grip returns it to filling the host. Sizes are written with <c>SetCurrentValue</c> so a
/// two-way binding can keep them on the view model.
/// </summary>
public static class DialogResizeBehavior
{
    public static readonly DependencyProperty IsResizableProperty = DependencyProperty.RegisterAttached(
        "IsResizable",
        typeof(bool),
        typeof(DialogResizeBehavior),
        new PropertyMetadata(false, OnIsResizableChanged));

    public static readonly DependencyProperty IsGripProperty = DependencyProperty.RegisterAttached(
        "IsGrip",
        typeof(bool),
        typeof(DialogResizeBehavior),
        new PropertyMetadata(false, OnIsGripChanged));

    public static bool GetIsResizable(DependencyObject element) => (bool)element.GetValue(IsResizableProperty);

    public static void SetIsResizable(DependencyObject element, bool value) => element.SetValue(IsResizableProperty, value);

    public static bool GetIsGrip(DependencyObject element) => (bool)element.GetValue(IsGripProperty);

    public static void SetIsGrip(DependencyObject element, bool value) => element.SetValue(IsGripProperty, value);

    private static void OnIsResizableChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not FrameworkElement dialog)
        {
            return;
        }

        dialog.Loaded -= OnDialogLoaded;
        dialog.Unloaded -= OnDialogUnloaded;
        if (e.NewValue is true)
        {
            dialog.Loaded += OnDialogLoaded;
            dialog.Unloaded += OnDialogUnloaded;
        }
    }

    private static void OnIsGripChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not Thumb grip)
        {
            return;
        }

        grip.DragDelta -= OnGripDragDelta;
        grip.MouseDoubleClick -= OnGripDoubleClick;
        if (e.NewValue is true)
        {
            grip.DragDelta += OnGripDragDelta;
            grip.MouseDoubleClick += OnGripDoubleClick;
        }
    }

    private static void OnGripDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (FindDialog((DependencyObject)sender) is not { } dialog)
        {
            return;
        }

        var (maxWidth, maxHeight) = Available(dialog);

        // The dialog is centred in its host, so a size change of 2·Δ moves the corner by Δ and the
        // grip stays under the pointer.
        var width = Clamp(dialog.ActualWidth + (2 * e.HorizontalChange), dialog.MinWidth, maxWidth);
        var height = Clamp(dialog.ActualHeight + (2 * e.VerticalChange), dialog.MinHeight, maxHeight);
        dialog.SetCurrentValue(FrameworkElement.WidthProperty, width);
        dialog.SetCurrentValue(FrameworkElement.HeightProperty, height);
        e.Handled = true;
    }

    private static void OnGripDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindDialog((DependencyObject)sender) is not { } dialog)
        {
            return;
        }

        dialog.SetCurrentValue(FrameworkElement.WidthProperty, double.NaN);
        dialog.SetCurrentValue(FrameworkElement.HeightProperty, double.NaN);
        e.Handled = true;
    }

    private static void OnDialogLoaded(object sender, RoutedEventArgs e)
    {
        var dialog = (FrameworkElement)sender;
        if (VisualTreeHelper.GetParent(dialog) is FrameworkElement host)
        {
            host.SizeChanged -= OnHostSizeChanged;
            host.SizeChanged += OnHostSizeChanged;
        }

        FitToHost(dialog);
    }

    private static void OnDialogUnloaded(object sender, RoutedEventArgs e)
    {
        if (VisualTreeHelper.GetParent((FrameworkElement)sender) is FrameworkElement host)
        {
            host.SizeChanged -= OnHostSizeChanged;
        }
    }

    private static void OnHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var host = (FrameworkElement)sender;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(host); i++)
        {
            if (VisualTreeHelper.GetChild(host, i) is FrameworkElement child && GetIsResizable(child))
            {
                FitToHost(child);
            }
        }
    }

    /// <summary>A dragged size never outgrows a window that has since been made smaller.</summary>
    private static void FitToHost(FrameworkElement dialog)
    {
        var (maxWidth, maxHeight) = Available(dialog);
        if (!double.IsNaN(dialog.Width) && dialog.Width > maxWidth)
        {
            dialog.SetCurrentValue(FrameworkElement.WidthProperty, Math.Max(dialog.MinWidth, maxWidth));
        }

        if (!double.IsNaN(dialog.Height) && dialog.Height > maxHeight)
        {
            dialog.SetCurrentValue(FrameworkElement.HeightProperty, Math.Max(dialog.MinHeight, maxHeight));
        }
    }

    private static (double Width, double Height) Available(FrameworkElement dialog)
    {
        if (VisualTreeHelper.GetParent(dialog) is not FrameworkElement host || host.ActualWidth <= 0)
        {
            return (double.PositiveInfinity, double.PositiveInfinity);
        }

        var margin = dialog.Margin;
        return (
            Math.Max(0, host.ActualWidth - margin.Left - margin.Right),
            Math.Max(0, host.ActualHeight - margin.Top - margin.Bottom));
    }

    private static double Clamp(double value, double min, double max)
        => Math.Max(min, Math.Min(value, Math.Max(min, max)));

    private static FrameworkElement? FindDialog(DependencyObject start)
    {
        for (var current = VisualTreeHelper.GetParent(start); current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement element && GetIsResizable(element))
            {
                return element;
            }
        }

        return null;
    }
}
