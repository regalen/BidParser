using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using BidParser.Desktop.Configuration;

namespace BidParser.Desktop.Mvvm;

/// <summary>
/// Renders validated guidance runs into a TextBlock. The inlines are constructed here as WPF
/// objects from an already-safe model — no markup is ever handed to a XAML or HTML parser, so a
/// remote document cannot introduce an element this code did not create.
/// </summary>
public static class GuidanceInlines
{
    public static readonly DependencyProperty RunsProperty = DependencyProperty.RegisterAttached(
        "Runs",
        typeof(IReadOnlyList<GuidanceRun>),
        typeof(GuidanceInlines),
        new PropertyMetadata(null, OnRunsChanged));

    public static IReadOnlyList<GuidanceRun>? GetRuns(DependencyObject element)
        => (IReadOnlyList<GuidanceRun>?)element.GetValue(RunsProperty);

    public static void SetRuns(DependencyObject element, IReadOnlyList<GuidanceRun>? value)
        => element.SetValue(RunsProperty, value);

    private static void OnRunsChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBlock target)
        {
            return;
        }

        target.Inlines.Clear();
        if (e.NewValue is not IReadOnlyList<GuidanceRun> runs)
        {
            return;
        }

        foreach (var run in runs)
        {
            target.Inlines.Add(new Run(run.Text)
            {
                FontWeight = run.IsStrong ? FontWeights.SemiBold : FontWeights.Normal
            });
        }
    }
}
