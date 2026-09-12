using System.Net.Http;
using System.Windows;
using BidParser.Application.Output;
using BidParser.Application.Parsing;
using BidParser.Desktop.Configuration;
using BidParser.Desktop.Services;
using BidParser.Desktop.ViewModels;
using BidParser.Parsing.Registry;

namespace BidParser.Desktop;

public partial class App : System.Windows.Application
{
    private readonly CancellationTokenSource lifetime = new();
    private WindowsThemeService? themeService;
    private HttpClient? httpClient;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Composition is a handful of `new`s: no host, no container, no database, no listener.
        var registry = new ParserRegistry();
        var sourceInspector = new SourceFormatInspector();
        var knownSlugs = registry.Parsers.Select(parser => parser.Slug).ToHashSet(StringComparer.Ordinal);
        var knownVendors = registry.Parsers.Select(parser => parser.Vendor).ToHashSet(StringComparer.Ordinal);

        // Step 1: the bundled documents are read and validated synchronously, so the window below
        // opens with working guidance and defaults whether or not this machine has a network.
        var configuration = DesktopConfiguration.FromBundled(knownSlugs, knownVendors);

        themeService = new WindowsThemeService(this);
        themeService.Start();

        var viewModel = new MainWindowViewModel(
            new ParserCatalog(registry),
            new QuoteParseService(registry, sourceInspector),
            new WorkbookWriteService(),
            new WindowsFileDialogService(),
            new WindowsClipboardService(),
            new WindowsShellLauncher(),
            configuration,
            configuration);

        var window = new MainWindow(viewModel);
        MainWindow = window;

        // Step 2: show the usable window before any network work starts.
        window.Show();

        // Step 3: three independent background operations. None blocks the window, none waits on
        // another, and each keeps its fallback silently when it fails.
        StartBackgroundRefresh(viewModel, configuration, knownSlugs, knownVendors);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        lifetime.Cancel();
        httpClient?.Dispose();
        lifetime.Dispose();
        themeService?.Dispose();
        base.OnExit(e);
    }

    private void StartBackgroundRefresh(
        MainWindowViewModel viewModel,
        DesktopConfiguration configuration,
        IReadOnlySet<string> knownSlugs,
        IReadOnlySet<string> knownVendors)
    {
        httpClient = PublicDocumentFetcher.CreateClient(DesktopVersion.UserAgent);
        var fetcher = new PublicDocumentFetcher(httpClient);
        var remote = new RemoteConfigurationService(fetcher);
        var updates = new UpdateCheckService(fetcher);
        var ct = lifetime.Token;

        _ = ApplyAsync(
            () => remote.TryFetchGuidanceAsync(knownSlugs, ct),
            configuration.ReplaceGuidance);

        _ = ApplyAsync(
            () => remote.TryFetchVendorDefaultsAsync(knownVendors, ct),
            catalog =>
            {
                configuration.ReplaceVendorDefaults(catalog);
                // Defaults that arrive after the window opened update only the fields the user has
                // not already edited.
                viewModel.RefreshVendorDefaults();
            });

        _ = ApplyAsync(
            () => updates.TryCheckAsync(DesktopVersion.Installed, ct),
            viewModel.ShowUpdate);
    }

    /// <summary>Runs one background operation and applies its result on the UI dispatcher, or does nothing.</summary>
    private async Task ApplyAsync<T>(Func<Task<T?>> operation, Action<T> apply) where T : class
    {
        try
        {
            if (await operation() is { } result)
            {
                await Dispatcher.InvokeAsync(() => apply(result));
            }
        }
        catch (Exception)
        {
            // A background refresh never disturbs the user: the bundled document or the
            // no-update state simply stands.
        }
    }
}
