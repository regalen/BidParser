using System.Globalization;
using System.IO;
using BidParser.Application.Output;
using BidParser.Application.Parsing;
using BidParser.Desktop.Configuration;
using BidParser.Desktop.Mvvm;
using BidParser.Desktop.Services;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;

namespace BidParser.Desktop.ViewModels;

/// <summary>
/// The whole desktop workflow: choose a vendor/format/template, hold one quote file, parse it off
/// the UI thread, and save the CRM workbook explicitly. Nothing is persisted between launches and
/// no copy of the quote is retained — the parsed result lives only in this object.
/// </summary>
public sealed class MainWindowViewModel : ObservableObject
{
    private readonly QuoteParseService parseService;
    private readonly WorkbookWriteService writeService;
    private readonly IFileDialogService fileDialogs;
    private readonly IClipboardService clipboard;
    private readonly IShellLauncher shell;
    private readonly IVendorDefaultsSource vendorDefaults;
    private readonly IGuidanceProvider guidance;

    private VendorViewModel? selectedVendor;
    private ParserOptionViewModel? selectedParser;
    private TemplateOptionViewModel? selectedTemplate;
    private EffectiveCapabilities capabilities = EffectiveCapabilities.None;
    private SelectedFileViewModel? selectedFile;
    private InvalidDropZoneViewModel? invalidFile;
    private ResultViewModel result = new NoResultViewModel();
    private OverwriteDialogViewModel? dialog;
    private ParsedQuote? parsedQuote;
    private string? completedStatus;
    private string? lastSaveDirectory;
    private bool splitBySolutionId;
    private bool isDragOver;
    private bool isParsing;
    private bool isSaving;
    private AvailableUpdate? update;
    private bool updateDismissed;

    public MainWindowViewModel(
        ParserCatalog catalog,
        QuoteParseService parseService,
        WorkbookWriteService writeService,
        IFileDialogService fileDialogs,
        IClipboardService clipboard,
        IShellLauncher shell,
        IVendorDefaultsSource vendorDefaults,
        IGuidanceProvider guidance)
    {
        this.parseService = parseService;
        this.writeService = writeService;
        this.fileDialogs = fileDialogs;
        this.clipboard = clipboard;
        this.shell = shell;
        this.vendorDefaults = vendorDefaults;
        this.guidance = guidance;

        // Dell quotes are acquired from the Dell Quote API, which the desktop does not host —
        // manual Dell JSON upload is not a supported contract, so the vendor is absent here.
        Vendors = catalog.GetAll()
            .Where(parser => parser.Vendor != BidParser.Domain.Constants.Vendors.Dell)
            .GroupBy(parser => parser.Vendor)
            .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new VendorViewModel(
                group.Key,
                group.Select(parser => new ParserOptionViewModel(parser)).ToList()))
            .ToList();

        foreach (var input in AllPricingFields)
        {
            input.PropertyChanged += (_, _) => Refresh();
        }

        BrowseCommand = new RelayCommand(Browse, () => !IsBusy);
        ClearCommand = new RelayCommand(ClearFile, () => !IsBusy);
        DropCommand = new RelayCommand<string[]>(OnFilesDropped);
        DragStateCommand = new RelayCommand<object>(state => IsDragOver = state is true);
        PrimaryCommand = new AsyncRelayCommand(RunPrimaryAsync, () => CanRunPrimary);
        SecondaryCommand = new RelayCommand(RunSecondary, () => !isSaving && !HasDialog);
        OpenFolderCommand = new RelayCommand(OpenSavedFolder, () => SavedPath is not null);
        CopyDetailsCommand = new RelayCommand(CopyDetails, () => Result is FailureResultViewModel);
        EscapeCommand = new RelayCommand(Escape);
        ViewUpdateCommand = new RelayCommand(ViewUpdate, () => update is not null);
        DismissUpdateCommand = new RelayCommand(DismissUpdate);

        Refresh();
    }

    public IReadOnlyList<VendorViewModel> Vendors { get; }
    public IReadOnlyList<ParserOptionViewModel> ParserOptions => SelectedVendor?.Parsers ?? [];
    public IReadOnlyList<TemplateOptionViewModel> TemplateOptions { get; private set; } = [];

    public NumericFieldViewModel FxRate { get; } = new("FX rate", "Direct", "4 d.p.", decimals: 4, mustBePositive: true);
    public NumericFieldViewModel Uplift { get; } = new("Uplift", "%", "%, 2 d.p.", decimals: 2);
    public NumericFieldViewModel DiscountOffMsrp { get; } = new("Discount Off MSRP", "%", "%, 2 d.p.", decimals: 2);
    public NumericFieldViewModel OnCost { get; } = new("On cost", "%", "optional · %, 2 d.p.", decimals: 2);

    public RelayCommand BrowseCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand<string[]> DropCommand { get; }
    public RelayCommand<object> DragStateCommand { get; }
    public AsyncRelayCommand PrimaryCommand { get; }
    public RelayCommand SecondaryCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand CopyDetailsCommand { get; }
    public RelayCommand EscapeCommand { get; }
    public RelayCommand ViewUpdateCommand { get; }
    public RelayCommand DismissUpdateCommand { get; }

    public VendorViewModel? SelectedVendor
    {
        get => selectedVendor;
        set
        {
            if (!SetProperty(ref selectedVendor, value))
            {
                return;
            }

            // Switching vendor discards that vendor's in-session pricing edits, matching the web.
            foreach (var input in AllPricingFields)
            {
                input.Clear();
            }

            OnPropertyChanged(nameof(ParserOptions));
            OnPropertyChanged(nameof(PricingHeading));
            OnPropertyChanged(nameof(HasSelectedVendor));
            SelectedParser = value?.Parsers.FirstOrDefault(parser => parser.Descriptor.IsAuto)
                ?? (value?.Parsers.Count == 1 ? value.Parsers[0] : null);
            ApplyVendorDefaults();
        }
    }

    public ParserOptionViewModel? SelectedParser
    {
        get => selectedParser;
        set
        {
            if (!SetProperty(ref selectedParser, value))
            {
                return;
            }

            TemplateOptions = value?.Descriptor.AvailableTemplates
                .Select(template => new TemplateOptionViewModel(template))
                .ToList() ?? [];
            OnPropertyChanged(nameof(TemplateOptions));
            OnPropertyChanged(nameof(HasMultipleTemplates));
            OnPropertyChanged(nameof(AcceptedFileCaption));
            OnPropertyChanged(nameof(SplitLabel));
            SelectedTemplate = TemplateOptions.FirstOrDefault();

            // The result belongs to the parser that produced it, and the held file may not be
            // accepted by the new one.
            DiscardParse();
            RevalidateSelectedFile();
            Refresh();
        }
    }

    public TemplateOptionViewModel? SelectedTemplate
    {
        get => selectedTemplate;
        set
        {
            if (!SetProperty(ref selectedTemplate, value))
            {
                return;
            }

            Capabilities = selectedParser is null || value is null
                ? EffectiveCapabilities.None
                : EffectiveCapabilities.From(selectedParser.Descriptor, value.Name);

            FxRate.IsRequired = Capabilities.SupportsFx && Capabilities.RequiresFx;
            Uplift.IsRequired = Capabilities.SupportsUplift && Capabilities.RequiresUplift;
            DiscountOffMsrp.IsRequired = Capabilities.SupportsDiscountOffMsrp && Capabilities.RequiresDiscountOffMsrp;
            OnCost.IsRequired = false;

            if (!Capabilities.SupportsSplit)
            {
                SplitBySolutionId = false;
            }

            Refresh();
        }
    }

    public EffectiveCapabilities Capabilities
    {
        get => capabilities;
        private set => SetProperty(ref capabilities, value);
    }

    public bool SplitBySolutionId
    {
        get => splitBySolutionId;
        set => SetProperty(ref splitBySolutionId, value);
    }

    public DropZoneViewModel DropZone
    {
        get
        {
            if (isDragOver)
            {
                return new DragOverDropZoneViewModel();
            }
            if (invalidFile is not null)
            {
                return invalidFile;
            }
            if (selectedFile is null)
            {
                return new EmptyDropZoneViewModel(AcceptedFileCaption);
            }
            if (isParsing)
            {
                return new FileDropZoneViewModel(selectedFile, "Reading line items…", FileStatusSeverity.Working, CanReplace: false);
            }
            if (completedStatus is not null)
            {
                return new FileDropZoneViewModel(selectedFile, completedStatus, FileStatusSeverity.Done, CanReplace: true);
            }

            return new FileDropZoneViewModel(
                selectedFile,
                "Drop another file here to replace it · Ctrl+O opens the file picker",
                FileStatusSeverity.Neutral,
                CanReplace: true);
        }
    }

    public ResultViewModel Result
    {
        get => result;
        private set
        {
            if (SetProperty(ref result, value))
            {
                Refresh();
            }
        }
    }

    /// <summary>The overwrite decision, or null when no overlay is open.</summary>
    public OverwriteDialogViewModel? Dialog
    {
        get => dialog;
        private set
        {
            if (SetProperty(ref dialog, value))
            {
                OnPropertyChanged(nameof(HasDialog));
                Refresh();
            }
        }
    }

    public bool HasDialog => dialog is not null;

    public bool IsDragOver
    {
        get => isDragOver;
        private set
        {
            if (SetProperty(ref isDragOver, value))
            {
                OnPropertyChanged(nameof(DropZone));
            }
        }
    }

    public bool IsParsing
    {
        get => isParsing;
        private set
        {
            if (SetProperty(ref isParsing, value))
            {
                Refresh();
            }
        }
    }

    public bool IsBusy => isParsing || isSaving;

    /// <summary>Settings are frozen for the duration of a parse.</summary>
    public bool IsSettingsEnabled => !IsBusy;

    public bool HasMultipleTemplates => TemplateOptions.Count > 1;

    public bool HasSelectedVendor => SelectedVendor is not null;

    /// <summary>
    /// True only while an update has been found and the user has not dismissed it. Dismissal is
    /// session-scoped and never written to disk, so the bar returns on the next launch.
    /// </summary>
    public bool HasUpdate => update is not null && !updateDismissed;

    public string UpdateMessage => update is null
        ? string.Empty
        : $"Installed {update.Installed} · Latest {update.Latest}";

    public bool CanParse =>
        SelectedVendor is not null
        && SelectedParser is not null
        && SelectedTemplate is not null
        && selectedFile is not null
        && !VisiblePricingFields.Any(input => input.HasError)
        && !IsBusy;

    public bool CanSave => parsedQuote is not null && !IsBusy;

    public string PrimaryActionText => isParsing
        ? "Parsing…"
        : Result switch
        {
            WarningResultViewModel => "Save anyway",
            SuccessResultViewModel => "Save",
            _ => "Parse quote"
        };

    public string SecondaryActionText => isParsing ? "Cancel" : "Reset";

    public string PricingHeading => $"{SelectedVendor?.Name ?? "Vendor"} pricing".ToUpperInvariant();

    public string SplitLabel => $"Split output by {SelectedParser?.Descriptor.SolutionSplitLabel ?? "Solution ID"}";

    public string AcceptedFileCaption => SelectedParser is null
        ? "Select a file type first"
        : $"{string.Join(", ", AcceptedExtensions.Select(extension => extension[1..].ToUpperInvariant()))} · max {SourceFormatInspector.MaxSourceBytes / (1024 * 1024)} MB";

    public string VersionText => $"BidParser Desktop {DesktopVersion.Display}";

    public string? SavedPath => (Result as ParsedResultViewModel)?.SavedPath;

    /// <summary>
    /// Reapplies the active vendor defaults to fields the user has not edited. Called when a
    /// validated remote defaults document arrives after the window is already open.
    /// </summary>
    public void RefreshVendorDefaults() => ApplyVendorDefaults();

    /// <summary>
    /// Shows the update bar. Called from the UI dispatcher when the background check finds a newer
    /// public release; a failed or empty check simply never calls it.
    /// </summary>
    public void ShowUpdate(AvailableUpdate available)
    {
        update = available;
        updateDismissed = false;
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(UpdateMessage));
        ViewUpdateCommand.NotifyCanExecuteChanged();
    }

    private IEnumerable<NumericFieldViewModel> AllPricingFields
    {
        get
        {
            yield return FxRate;
            yield return Uplift;
            yield return DiscountOffMsrp;
            yield return OnCost;
        }
    }

    private IEnumerable<NumericFieldViewModel> VisiblePricingFields
    {
        get
        {
            if (Capabilities.SupportsFx) yield return FxRate;
            if (Capabilities.SupportsUplift) yield return Uplift;
            if (Capabilities.SupportsDiscountOffMsrp) yield return DiscountOffMsrp;
            if (Capabilities.SupportsOnCost) yield return OnCost;
        }
    }

    private IReadOnlyList<string> AcceptedExtensions =>
        SelectedParser?.Descriptor.AcceptedExtensions ?? [];

    private bool CanRunPrimary => !HasDialog && (parsedQuote is not null ? CanSave : CanParse);

    private void ApplyVendorDefaults()
    {
        var defaults = SelectedVendor is null ? null : vendorDefaults.ForVendor(SelectedVendor.Name);
        FxRate.ApplyDefault(defaults?.FxRate);
        Uplift.ApplyDefault(defaults?.Margin);
        DiscountOffMsrp.ApplyDefault(defaults?.ImPercent);
        OnCost.ApplyDefault(defaults?.OnCostPercent);
    }

    private void Browse()
    {
        if (SelectedParser is null)
        {
            return;
        }

        var extensions = string.Join(";", AcceptedExtensions.Select(extension => $"*{extension}"));
        var chosen = fileDialogs.ChooseSourceFile($"Quote files ({extensions})|{extensions}");
        if (chosen is not null)
        {
            SelectFile(chosen);
        }
    }

    private void OnFilesDropped(string[]? paths)
    {
        if (paths is null || paths.Length == 0 || IsBusy)
        {
            return;
        }

        if (paths.Length > 1)
        {
            SetInvalidFile("One file at a time", "BidParser parses a single quote per run. Drop just the quote you want to parse.");
            return;
        }

        SelectFile(paths[0]);
    }

    private void SelectFile(string path)
    {
        DiscardParse();

        if (!File.Exists(path))
        {
            SetInvalidFile($"{Path.GetFileName(path)} could not be opened", "The file no longer exists at that location. Choose a different file.");
            return;
        }

        var file = SelectedFileViewModel.FromPath(path);
        if (Reject(file) is { } rejection)
        {
            SetInvalidFile(rejection.Headline, rejection.Detail);
            return;
        }

        invalidFile = null;
        selectedFile = file;
        Refresh();
    }

    private InvalidDropZoneViewModel? Reject(SelectedFileViewModel file)
    {
        if (file.SizeBytes > SourceFormatInspector.MaxSourceBytes)
        {
            var limit = SourceFormatInspector.MaxSourceBytes / (1024 * 1024);
            return new InvalidDropZoneViewModel(
                $"{file.DisplayName} is too large",
                $"Quotes must be {limit} MB or smaller. Export the quote again, or split it before parsing.");
        }

        var extension = Path.GetExtension(file.DisplayName);
        if (SelectedParser is { } parser
            && !parser.Descriptor.AcceptedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            var accepted = string.Join(", ", parser.Descriptor.AcceptedExtensions);
            return new InvalidDropZoneViewModel(
                $"{file.DisplayName} can't be parsed",
                $"{SelectedVendor?.Name} · {parser.DisplayName} accepts {accepted} files. Choose a different file or change the file type.");
        }

        return null;
    }

    private void SetInvalidFile(string headline, string detail)
    {
        selectedFile = null;
        invalidFile = new InvalidDropZoneViewModel(headline, detail);
        Refresh();
    }

    private void RevalidateSelectedFile()
    {
        if (selectedFile is null)
        {
            return;
        }

        if (Reject(selectedFile) is { } rejection)
        {
            SetInvalidFile(rejection.Headline, rejection.Detail);
        }
    }

    private void ClearFile()
    {
        selectedFile = null;
        invalidFile = null;
        DiscardParse();
        Refresh();
    }

    /// <summary>Releases the in-memory parse result. No quote data outlives this call.</summary>
    private void DiscardParse()
    {
        parsedQuote = null;
        completedStatus = null;
        Result = new NoResultViewModel();
    }

    private void RunSecondary()
    {
        if (isParsing)
        {
            PrimaryCommand.Cancel();
            return;
        }

        Reset();
    }

    private void Reset()
    {
        selectedFile = null;
        invalidFile = null;
        SplitBySolutionId = false;
        DiscardParse();

        foreach (var input in AllPricingFields)
        {
            input.Clear();
        }

        ApplyVendorDefaults();
        Refresh();
    }

    private void Escape()
    {
        if (Dialog is { } open)
        {
            open.CancelCommand.Execute(null);
            return;
        }

        if (isParsing)
        {
            PrimaryCommand.Cancel();
        }
    }

    private void ViewUpdate()
    {
        if (update is { } available)
        {
            shell.Open(available.ReleasePage.ToString());
        }
    }

    private void DismissUpdate()
    {
        updateDismissed = true;
        OnPropertyChanged(nameof(HasUpdate));
    }

    private void CopyDetails()
    {
        if (Result is FailureResultViewModel failure)
        {
            clipboard.SetText(failure.TechnicalDetails);
        }
    }

    private void OpenSavedFolder()
    {
        if (SavedPath is { } path)
        {
            shell.RevealInFolder(path);
        }
    }

    private Task RunPrimaryAsync(CancellationToken ct) => CanSave ? SaveAsync(ct) : ParseAsync(ct);

    private async Task ParseAsync(CancellationToken ct)
    {
        if (selectedFile is not { } file || SelectedParser is not { } parser || SelectedVendor is not { } vendor)
        {
            return;
        }

        DiscardParse();
        IsParsing = true;
        try
        {
            var request = new QuoteParseRequest(
                file.Path,
                vendor.Name,
                parser.Descriptor.Slug,
                new ParseOptions(),
                file.DisplayName);

            var parsed = await Task.Run(() => parseService.ParseAsync(request, ct), ct);
            ct.ThrowIfCancellationRequested();

            parsedQuote = parsed;
            completedStatus = $"Parsed · {parsed.Result.LineItems.Count} line items";
            Result = BuildResult(parsed, file);
        }
        catch (OperationCanceledException)
        {
            // Parsers are synchronous, so a cancelled run may still have completed off-thread —
            // its result is dropped and the window returns to idle.
            DiscardParse();
        }
        catch (Exception error)
        {
            Result = BuildFailure(error, parser.Descriptor.Slug, file.DisplayName);
        }
        finally
        {
            IsParsing = false;
        }
    }

    private ParsedResultViewModel BuildResult(ParsedQuote parsed, SelectedFileViewModel file)
    {
        var validation = parsed.Result.Validation;
        var currency = parsed.Result.Metadata.Currency;
        var lineCount = parsed.Result.LineItems.Count;
        var cancelled = parsed.Result.LineItems.Count(item => item.IsCancelled);

        var facts = new List<ResultFactViewModel>();
        if (parsed.Result.Metadata.BidNumber is { Length: > 0 } bidNumber)
        {
            facts.Add(new ResultFactViewModel(
                "Bid number",
                parsed.Result.Metadata.BidRevision is { Length: > 0 } revision
                    ? $"{bidNumber} v{revision}"
                    : bidNumber));
        }
        facts.Add(new ResultFactViewModel("Line items", lineCount.ToString(CultureInfo.CurrentCulture)));
        if (validation.QuotedTotal is { } quotedTotal)
        {
            facts.Add(new ResultFactViewModel("Quoted total", Money(currency, quotedTotal)));
        }
        if (parsed.WasAuto)
        {
            facts.Add(new ResultFactViewModel("Detected type", parsed.Parser.DisplayName));
        }

        var notes = new List<string>();
        if (cancelled > 0)
        {
            notes.Add($"{cancelled} line{(cancelled == 1 ? " is" : "s are")} flagged as cancelled and will be imported "
                + "with a standard price — the downstream system retrieves current pricing from SAP for those lines.");
        }
        if (parsed.Result.HasRebateIneligibleItems)
        {
            notes.Add("This quote contains parent or base SKU items that are not eligible for rebate or MDF.");
        }

        var message = guidance.MessageFor(parsed.Parser.Slug);

        if (!validation.Matches)
        {
            facts.Add(new ResultFactViewModel("Sum of line totals", Money(currency, validation.ComputedTotal)));
            facts.Add(new ResultFactViewModel("Difference", Money(currency, validation.Difference)));

            return new WarningResultViewModel
            {
                Headline = "Parsed, but totals don't match the quote",
                Detail = $"All {lineCount} line items were read from {file.DisplayName}. The sum of line totals "
                    + "differs from the vendor's quoted total — check the totals before importing to CRM.",
                Facts = facts,
                Notes = notes,
                Guidance = message
            };
        }

        if (notes.Count > 0)
        {
            return new WarningResultViewModel
            {
                Headline = $"Parsed with warnings · {lineCount} line items",
                Detail = $"{lineCount} line items were read from {file.DisplayName}. Review the warnings below, "
                    + "then save the CRM workbook.",
                Facts = facts,
                Notes = notes,
                Guidance = message
            };
        }

        return new SuccessResultViewModel
        {
            Headline = $"Parsed successfully · {lineCount} line items",
            Detail = $"{lineCount} line items were read from {file.DisplayName}. Save the CRM workbook to continue.",
            Facts = facts,
            Guidance = message
        };
    }

    private FailureResultViewModel BuildFailure(Exception error, string parserSlug, string filename)
    {
        var (headline, cause, nextStep, stage) = error switch
        {
            ParseInputException { Kind: ParseInputErrorKind.WrongFileType or ParseInputErrorKind.AutoDetectionFailed } wrongType =>
                ("This file doesn't match the selected file type",
                    wrongType.Message,
                    wrongType.SuggestedParserName is { } suggestion
                        ? $"Change the file type to {suggestion} and parse again."
                        : "Change the file type to match the document, or choose a different file.",
                    wrongType.Stage ?? "fileType"),

            ParseInputException { Kind: ParseInputErrorKind.MagicByteMismatch } =>
                ("This file isn't the format its name claims",
                    $"{filename} does not contain the expected file signature.",
                    "Re-export the quote from the vendor portal and try again.",
                    "upload"),

            ParseInputException { Kind: ParseInputErrorKind.ExtensionMismatch or ParseInputErrorKind.UnsupportedExtension } extension =>
                ("This file type can't be parsed",
                    extension.Message,
                    "Choose a file the selected format accepts, or change the file type.",
                    "upload"),

            ParseInputException other =>
                ("Couldn't parse this quote", other.Message, "Check the parse settings and try again.", other.Stage ?? "input"),

            ParseError parseError =>
                ("Couldn't parse this quote", parseError.Message, parseError.Hint, parseError.Stage),

            _ => ("Couldn't parse this quote",
                    "The file was read but no usable quote could be extracted from it.",
                    "Confirm the vendor and file type match the document, or send the file to the BidParser maintainer.",
                    "extract")
        };

        // Diagnostics only: parser, stage, exception type, version and time. No stack trace, no
        // quote contents and no absolute paths.
        var details = string.Join(
            Environment.NewLine,
            $"{parserSlug} · {stage} · {error.GetType().Name}",
            filename,
            $"BidParser Desktop {DesktopVersion.Display} · {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");

        return new FailureResultViewModel
        {
            Headline = headline,
            Cause = cause,
            NextStep = nextStep,
            TechnicalDetails = details
        };
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        if (parsedQuote is not { } quote)
        {
            return;
        }

        var split = SplitBySolutionId && Capabilities.SupportsSplit;
        WorkbookWritePlan plan;
        try
        {
            plan = writeService.Describe(quote, SelectedTemplate?.Name, split);
        }
        catch (Exception error)
        {
            Result = BuildFailure(error, quote.Parser.Slug, quote.SourceFilename);
            return;
        }

        var filter = plan.IsArchive
            ? "Solution ID archive (*.zip)|*.zip"
            : "CRM workbook (*.xlsx)|*.xlsx";

        var destination = fileDialogs.ChooseOutputFile(plan.SuggestedFilename, filter, lastSaveDirectory);
        if (destination is null)
        {
            return;
        }

        await WriteToAsync(destination, overwrite: false, ct);
    }

    private async Task WriteToAsync(string destination, bool overwrite, CancellationToken ct)
    {
        if (parsedQuote is not { } quote || isSaving)
        {
            return;
        }

        isSaving = true;
        Refresh();
        try
        {
            var written = await Task.Run(
                () => writeService.SaveAsync(BuildWriteRequest(quote, destination), overwrite, ct),
                ct);

            lastSaveDirectory = Path.GetDirectoryName(written.OutputPath);
            if (Result is ParsedResultViewModel parsed)
            {
                parsed.SavedPath = written.OutputPath;
            }
        }
        catch (ParseInputException existing) when (existing.Kind == ParseInputErrorKind.DestinationExists)
        {
            PromptOverwrite(destination);
        }
        catch (OperationCanceledException)
        {
            // The staged workbook is removed by the writer; nothing reached the destination.
        }
        catch (Exception error)
        {
            Result = BuildFailure(error, quote.Parser.Slug, quote.SourceFilename);
        }
        finally
        {
            isSaving = false;
            Refresh();
        }
    }

    private WorkbookWriteRequest BuildWriteRequest(ParsedQuote quote, string destination) => new(
        quote,
        destination,
        SelectedTemplate?.Name,
        FxRate: Capabilities.SupportsFx ? FxRate.Value ?? 1m : 1m,
        Margin: Capabilities.SupportsUplift ? Uplift.Value ?? 0m : 0m,
        ImPercent: Capabilities.SupportsDiscountOffMsrp ? DiscountOffMsrp.Value : null,
        OnCostPercent: Capabilities.SupportsOnCost ? OnCost.Value : null,
        SplitBySolutionId: SplitBySolutionId && Capabilities.SupportsSplit);

    private void PromptOverwrite(string destination)
    {
        var filename = Path.GetFileName(destination);
        var directory = Path.GetDirectoryName(destination) ?? string.Empty;

        Dialog = new OverwriteDialogViewModel(
            filename,
            directory,
            replace: () =>
            {
                Dialog = null;
                _ = WriteToAsync(destination, overwrite: true, CancellationToken.None);
            },
            saveCopy: () =>
            {
                Dialog = null;
                _ = WriteToAsync(WorkbookWriteService.UniqueDestination(destination), overwrite: false, CancellationToken.None);
            },
            cancel: () => Dialog = null);
    }

    private static string Money(string currency, decimal value)
        => $"{currency} {value.ToString("N2", CultureInfo.CurrentCulture)}";

    private void Refresh()
    {
        OnPropertyChanged(nameof(DropZone));
        OnPropertyChanged(nameof(CanParse));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsSettingsEnabled));
        OnPropertyChanged(nameof(PrimaryActionText));
        OnPropertyChanged(nameof(SecondaryActionText));
        OnPropertyChanged(nameof(SavedPath));

        PrimaryCommand.NotifyCanExecuteChanged();
        SecondaryCommand.NotifyCanExecuteChanged();
        BrowseCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
        CopyDetailsCommand.NotifyCanExecuteChanged();
    }
}
