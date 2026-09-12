using System.Windows;
using Microsoft.Win32;

namespace BidParser.Desktop.Services;

public sealed class WindowsThemeService(System.Windows.Application application) : IThemeService, IDisposable
{
    private const string PersonalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private bool started;

    public void Start()
    {
        if (started)
        {
            return;
        }

        started = true;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        ApplyTheme();
    }

    public void Dispose()
    {
        if (!started)
        {
            return;
        }

        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
        started = false;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        => application.Dispatcher.BeginInvoke(ApplyTheme);

    private void OnSystemParameterChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => application.Dispatcher.BeginInvoke(ApplyTheme);

    private void ApplyTheme()
    {
        var source = SystemParameters.HighContrast
            ? "Themes/Colors.HighContrast.xaml"
            : IsLightMode()
                ? "Themes/Colors.Light.xaml"
                : "Themes/Colors.Dark.xaml";

        var dictionaries = application.Resources.MergedDictionaries;
        var current = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.Contains("Colors.", StringComparison.Ordinal) == true);
        var replacement = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };
        if (current is null)
        {
            dictionaries.Insert(0, replacement);
        }
        else
        {
            dictionaries[dictionaries.IndexOf(current)] = replacement;
        }
    }

    private static bool IsLightMode()
        => Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", 1) is not int value || value != 0;
}
