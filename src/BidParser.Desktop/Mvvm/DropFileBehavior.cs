using System.Windows;
using System.Windows.Input;

namespace BidParser.Desktop.Mvvm;

/// <summary>
/// Turns any element into a single-file drop target. The element reports drag state and the
/// dropped paths through commands, so file policy stays in the view model.
/// </summary>
public static class DropFileBehavior
{
    public static readonly DependencyProperty DropCommandProperty = DependencyProperty.RegisterAttached(
        "DropCommand",
        typeof(ICommand),
        typeof(DropFileBehavior),
        new PropertyMetadata(null, OnCommandChanged));

    public static readonly DependencyProperty DragStateCommandProperty = DependencyProperty.RegisterAttached(
        "DragStateCommand",
        typeof(ICommand),
        typeof(DropFileBehavior),
        new PropertyMetadata(null, OnCommandChanged));

    public static ICommand? GetDropCommand(DependencyObject element)
        => (ICommand?)element.GetValue(DropCommandProperty);

    public static void SetDropCommand(DependencyObject element, ICommand? value)
        => element.SetValue(DropCommandProperty, value);

    public static ICommand? GetDragStateCommand(DependencyObject element)
        => (ICommand?)element.GetValue(DragStateCommandProperty);

    public static void SetDragStateCommand(DependencyObject element, ICommand? value)
        => element.SetValue(DragStateCommandProperty, value);

    private static void OnCommandChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not UIElement target)
        {
            return;
        }

        target.DragEnter -= OnDragOver;
        target.DragOver -= OnDragOver;
        target.DragLeave -= OnDragLeave;
        target.Drop -= OnDrop;

        if (e.NewValue is null)
        {
            return;
        }

        target.AllowDrop = true;
        target.DragEnter += OnDragOver;
        target.DragOver += OnDragOver;
        target.DragLeave += OnDragLeave;
        target.Drop += OnDrop;
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        var element = (DependencyObject)sender;
        var carriesFiles = PathsFrom(e) is not null;

        e.Effects = carriesFiles ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        Invoke(GetDragStateCommand(element), carriesFiles);
    }

    private static void OnDragLeave(object sender, DragEventArgs e)
    {
        Invoke(GetDragStateCommand((DependencyObject)sender), false);
        e.Handled = true;
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        var element = (DependencyObject)sender;
        Invoke(GetDragStateCommand(element), false);
        e.Handled = true;

        if (PathsFrom(e) is { } paths)
        {
            Invoke(GetDropCommand(element), paths);
        }
    }

    private static string[]? PathsFrom(DragEventArgs e)
        => e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths
            ? paths
            : null;

    private static void Invoke(ICommand? command, object parameter)
    {
        if (command?.CanExecute(parameter) == true)
        {
            command.Execute(parameter);
        }
    }
}
