using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BidParser.Desktop.Converters;

/// <summary>
/// Collapses an element whose bound value carries nothing to show — null, an empty string, or an
/// empty collection. Optional message bars and lists bind their Visibility straight to the value.
/// </summary>
public sealed class EmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => HasContent(value) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static bool HasContent(object? value) => value switch
    {
        null => false,
        string text => !string.IsNullOrWhiteSpace(text),
        ICollection collection => collection.Count > 0,
        IEnumerable sequence => sequence.GetEnumerator().MoveNext(),
        _ => true
    };
}
