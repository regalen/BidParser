using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace BidParser.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // A portable WinExe has no console, so an unhandled startup exception would
        // otherwise vanish ("double-click, nothing happens"). Surface it: write a
        // crash log next to the exe and show the message before the process dies.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ReportCrash(e.Exception);
        e.Handled = true;
        Shutdown(1);
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        => ReportCrash(e.ExceptionObject as Exception);

    private static void ReportCrash(Exception? ex)
    {
        var text = ex?.ToString() ?? "Unknown error (no exception object).";

        string? logPath = null;
        try
        {
            var dir = AppContext.BaseDirectory;
            logPath = Path.Combine(dir, "BidParserLite-crash.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:u}]\n{text}\n\n", Encoding.UTF8);
        }
        catch
        {
            // Best-effort logging; never let the crash reporter itself throw.
        }

        var message = new StringBuilder()
            .AppendLine("BidParser Lite failed to start.")
            .AppendLine()
            .AppendLine(text);

        if (logPath is not null)
            message.AppendLine().Append("A copy was written to:\n").Append(logPath);

        MessageBox.Show(
            message.ToString(),
            "BidParser Lite — Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
