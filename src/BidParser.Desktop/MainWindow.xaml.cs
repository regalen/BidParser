using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using BidParser.Desktop.ViewModels;

namespace BidParser.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>
    /// Moves keyboard focus into the overwrite overlay when it opens, so Tab and Space act on the
    /// dialog rather than on the window behind the scrim. Binding cannot express this.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.HasDialog)
            || sender is not MainWindowViewModel { HasDialog: true })
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
            DialogHost.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)));
    }
}
