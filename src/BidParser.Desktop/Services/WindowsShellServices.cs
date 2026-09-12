using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace BidParser.Desktop.Services;

public sealed class WindowsFileDialogService : IFileDialogService
{
    public string? ChooseSourceFile(string filter)
    {
        var dialog = new OpenFileDialog
        {
            Filter = filter,
            Multiselect = false,
            CheckFileExists = true,
            Title = "Choose a quote file"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ChooseOutputFile(string suggestedName, string filter, string? initialDirectory)
    {
        var dialog = new SaveFileDialog
        {
            Filter = filter,
            FileName = suggestedName,
            DefaultExt = Path.GetExtension(suggestedName),
            AddExtension = true,
            // The overwrite decision is ours: it uses the destructive dialog pattern, and the
            // workbook is staged and moved rather than written over the existing file.
            OverwritePrompt = false,
            Title = "Save CRM workbook"
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}

public sealed class WindowsClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception)
        {
            // Another process can hold the clipboard open; a failed copy is not worth an error state.
        }
    }
}

public sealed class WindowsShellLauncher : IShellLauncher
{
    public void Open(string target) => Start(new ProcessStartInfo(target) { UseShellExecute = true });

    public void RevealInFolder(string path)
    {
        if (File.Exists(path))
        {
            Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
    }

    private static void Start(ProcessStartInfo startInfo)
    {
        try
        {
            using var process = Process.Start(startInfo);
        }
        catch (Exception)
        {
            // No shell handler, or the folder disappeared — nothing worth interrupting the user for.
        }
    }
}
