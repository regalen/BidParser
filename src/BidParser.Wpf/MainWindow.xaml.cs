using System.IO;
using System.Windows;
using System.Windows.Input;
using BidParser.Wpf.ViewModels;
using Microsoft.Win32;

namespace BidParser.Wpf;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
    }

    private void Browse_Click(object sender, RoutedEventArgs e) => OpenFilePicker();

    private void OpenFilePicker()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a quote file",
            Filter = "Quote files|*.pdf;*.xlsx;*.xls|All files|*.*"
        };

        if (dialog.ShowDialog(this) == true)
            _vm.SetInputFile(dialog.FileName);
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        if (files is not { Length: > 0 }) return;

        var path = files[0];
        var ext = Path.GetExtension(path).ToLowerInvariant();

        if (ext is not (".pdf" or ".xlsx" or ".xls"))
        {
            MessageBox.Show(
                "Only PDF, XLSX, and XLS files are supported.",
                "Unsupported file type",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _vm.SetInputFile(path);
        e.Handled = true;
    }
}
