using System.Diagnostics;
using System.IO;
using System.Reflection;
using BidParser.Core;
using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Output;
using BidParser.Parsing.Registry;
using VendorNames = BidParser.Domain.Constants.Vendors;

namespace BidParser.Wpf.ViewModels;

public enum ParseState { Idle, Running, Success, Warning, Error }

public sealed class MainViewModel : ViewModelBase
{
    private const long MaxUploadBytes = 10 * 1024 * 1024; // 10 MB
    private const string ReleasesUrl = "https://github.com/regalen/BidParser/releases/latest";

    private readonly IParserRegistry _registry = new ParserRegistry();
    private readonly AsyncRelayCommand _convertCommand;
    private readonly RelayCommand _openFolderCommand;
    private readonly RelayCommand _openFileCommand;
    private readonly RelayCommand _resetCommand;
    private readonly RelayCommand _openReleasesCommand;

    // --- selection ---
    private IReadOnlyList<string> _vendors = [];
    private IReadOnlyList<IParser> _parsers = [];
    private IReadOnlyList<string> _templates = [];
    private string? _selectedVendor;
    private IParser? _selectedParser;
    private string? _selectedTemplate;

    // --- file ---
    private string? _inputFilePath;
    private string _inputFileName = string.Empty;
    private string _inputFileSizeDisplay = string.Empty;

    // --- numeric inputs ---
    private string _fxRate = string.Empty;
    private string _margin = string.Empty;
    private string _imPercent = string.Empty;
    private string _onCostPercent = string.Empty;

    // --- conditional visibility ---
    private bool _showFxRate;
    private bool _showMargin;
    private bool _showImPercent;
    private bool _showOnCostPercent;
    private bool _showTemplateDropdown;
    private string _crmTemplateDisplay = string.Empty;

    // --- result state ---
    private ParseState _state = ParseState.Idle;
    private string? _errorMessage;
    private string? _errorStage;

    // --- result details (computed from the ParseOutcome) ---
    private string _resultComputedTotal = string.Empty;
    private string _resultQuotedTotal = string.Empty;
    private bool _resultTotalsMatch;
    private string _resultDifference = string.Empty;
    private string _resultOutputPath = string.Empty;
    private string _resultOutputFolder = string.Empty;
    private IReadOnlyList<string> _cancelledLinesDisplay = [];
    private bool _hasCancelledLines;
    private string _resultReportType = string.Empty;
    private bool _hasResultReportType;

    public MainViewModel()
    {
        _convertCommand = new AsyncRelayCommand(ExecuteConvertAsync, () => GetCanConvert());
        _openFolderCommand = new RelayCommand(_ => OpenFolder(), _ => !string.IsNullOrEmpty(_resultOutputFolder));
        _openFileCommand = new RelayCommand(_ => OpenFile(), _ => !string.IsNullOrEmpty(_resultOutputPath));
        _resetCommand = new RelayCommand(_ => Reset());
        _openReleasesCommand = new RelayCommand(_ => OpenReleases());

        Vendors = _registry.Parsers
            .Select(p => p.Vendor)
            .Distinct()
            .ToList();

        // Auto-select when there is only one vendor; otherwise pick the first.
        SelectedVendor = Vendors.FirstOrDefault();
    }

    // Set by the View: given the default output path, shows a "Save As" dialog and
    // returns the chosen full path, or null if the user cancelled. Kept as a
    // delegate so the dialog stays in the View and the view-model stays testable.
    public Func<string, string?>? SaveFilePrompt { get; set; }

    // ── Selection properties ──────────────────────────────────────────────────

    public IReadOnlyList<string> Vendors
    {
        get => _vendors;
        private set => SetProperty(ref _vendors, value);
    }

    public IReadOnlyList<IParser> Parsers
    {
        get => _parsers;
        private set => SetProperty(ref _parsers, value);
    }

    public IReadOnlyList<string> Templates
    {
        get => _templates;
        private set => SetProperty(ref _templates, value);
    }

    public string? SelectedVendor
    {
        get => _selectedVendor;
        set
        {
            if (!SetProperty(ref _selectedVendor, value)) return;
            Parsers = _registry.Parsers.Where(p => p.Vendor == value).ToList();
            SelectedParser = Parsers.FirstOrDefault();
        }
    }

    public IParser? SelectedParser
    {
        get => _selectedParser;
        set
        {
            if (!SetProperty(ref _selectedParser, value)) return;
            Templates = value?.AvailableTemplates.ToList() ?? (IReadOnlyList<string>)[];
            SelectedTemplate = Templates.FirstOrDefault();
            UpdateConditionalFields();
        }
    }

    public string? SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (!SetProperty(ref _selectedTemplate, value)) return;
            UpdateConditionalFields();
        }
    }

    // ── File properties ───────────────────────────────────────────────────────

    public string? InputFilePath => _inputFilePath;
    public string InputFileName => _inputFileName;
    public string InputFileSizeDisplay => _inputFileSizeDisplay;
    public bool HasInputFile => !string.IsNullOrEmpty(_inputFilePath);

    public void SetInputFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) return;

        if (info.Length > MaxUploadBytes)
        {
            ErrorMessage = $"File exceeds the 10 MB limit ({info.Length / (1024.0 * 1024.0):F1} MB).";
            ErrorStage = null;
            State = ParseState.Error;
            return;
        }

        _inputFilePath = path;
        _inputFileName = info.Name;
        _inputFileSizeDisplay = FormatFileSize(info.Length);

        // Reset result when a new file is selected
        _errorMessage = null;
        _errorStage = null;
        if (_state != ParseState.Idle)
            State = ParseState.Idle;

        OnPropertyChanged(nameof(InputFilePath));
        OnPropertyChanged(nameof(InputFileName));
        OnPropertyChanged(nameof(InputFileSizeDisplay));
        OnPropertyChanged(nameof(HasInputFile));
        _convertCommand.RaiseCanExecuteChanged();
    }

    // ── Numeric inputs ────────────────────────────────────────────────────────

    public string FxRate
    {
        get => _fxRate;
        set { if (SetProperty(ref _fxRate, value)) _convertCommand.RaiseCanExecuteChanged(); }
    }

    public string Margin
    {
        get => _margin;
        set { if (SetProperty(ref _margin, value)) _convertCommand.RaiseCanExecuteChanged(); }
    }

    public string ImPercent
    {
        get => _imPercent;
        set { if (SetProperty(ref _imPercent, value)) _convertCommand.RaiseCanExecuteChanged(); }
    }

    public string OnCostPercent
    {
        get => _onCostPercent;
        set => SetProperty(ref _onCostPercent, value);
    }

    // ── Conditional visibility ────────────────────────────────────────────────

    public bool ShowFxRate
    {
        get => _showFxRate;
        private set => SetProperty(ref _showFxRate, value);
    }

    public bool ShowMargin
    {
        get => _showMargin;
        private set => SetProperty(ref _showMargin, value);
    }

    public bool ShowImPercent
    {
        get => _showImPercent;
        private set => SetProperty(ref _showImPercent, value);
    }

    public bool ShowOnCostPercent
    {
        get => _showOnCostPercent;
        private set => SetProperty(ref _showOnCostPercent, value);
    }

    public bool ShowTemplateDropdown
    {
        get => _showTemplateDropdown;
        private set => SetProperty(ref _showTemplateDropdown, value);
    }

    public string CrmTemplateDisplay
    {
        get => _crmTemplateDisplay;
        private set => SetProperty(ref _crmTemplateDisplay, value);
    }

    // The read-only template chip mirrors the web's CrmTemplateCallout, which is shown
    // only for single-template parsers — multi-template parsers surface the template via
    // their dropdown instead, so the chip would just duplicate it.
    public bool HasCrmTemplate => !string.IsNullOrEmpty(_crmTemplateDisplay) && !_showTemplateDropdown;

    // ── State ─────────────────────────────────────────────────────────────────

    public ParseState State
    {
        get => _state;
        private set
        {
            if (!SetProperty(ref _state, value)) return;
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsSuccess));
            OnPropertyChanged(nameof(IsWarning));
            OnPropertyChanged(nameof(IsError));
            OnPropertyChanged(nameof(IsNotRunning));
            _convertCommand.RaiseCanExecuteChanged();
            _openFolderCommand.RaiseCanExecuteChanged();
            _openFileCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsIdle => _state == ParseState.Idle;
    public bool IsRunning => _state == ParseState.Running;
    public bool IsSuccess => _state == ParseState.Success;
    public bool IsWarning => _state == ParseState.Warning;
    public bool IsError => _state == ParseState.Error;
    public bool IsNotRunning => _state != ParseState.Running;

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string? ErrorStage
    {
        get => _errorStage;
        private set => SetProperty(ref _errorStage, value);
    }

    // ── Result details ────────────────────────────────────────────────────────

    public string ResultComputedTotal => _resultComputedTotal;
    public string ResultQuotedTotal => _resultQuotedTotal;
    public bool ResultTotalsMatch => _resultTotalsMatch;
    public string ResultDifference => _resultDifference;
    public string ResultOutputPath => _resultOutputPath;
    public string ResultOutputFolder => _resultOutputFolder;
    public IReadOnlyList<string> CancelledLinesDisplay => _cancelledLinesDisplay;
    public bool HasCancelledLines => _hasCancelledLines;

    // Report type to tell the customer, from the hardcoded ReportTypes map keyed
    // by the parsed file type (mirrors the web result popup). Blank slugs hide it.
    public string ResultReportType => _resultReportType;
    public bool HasResultReportType => _hasResultReportType;

    // ── Commands ──────────────────────────────────────────────────────────────

    public AsyncRelayCommand ConvertCommand => _convertCommand;
    public RelayCommand OpenFolderCommand => _openFolderCommand;
    public RelayCommand OpenFileCommand => _openFileCommand;
    public RelayCommand ResetCommand => _resetCommand;
    public RelayCommand OpenReleasesCommand => _openReleasesCommand;

    // ── Footer ────────────────────────────────────────────────────────────────

    // Version comes from the assembly's informational version, which the release
    // pipeline stamps via `-p:Version=` on publish; local builds fall back to the
    // csproj default. Rendered as "vX.Y.Z" when numeric.
    public string AppVersionDisplay { get; } = ResolveVersion();

    // Year is resolved at launch so the copyright stays current without a code change.
    public string CopyrightText { get; } = $"Copyright © {DateTime.Now.Year} Ingram Micro. All rights reserved.";

    private static string ResolveVersion()
    {
        var info = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(info)) return "dev";
        var version = info.Split('+')[0]; // drop any "+<git-sha>" build-metadata suffix
        if (version.Length == 0) return "dev";
        return char.IsDigit(version[0]) ? $"v{version}" : version;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private string EffectiveTemplate =>
        _selectedParser is { } p && p.AvailableTemplates.Count > 1
            ? _selectedTemplate ?? string.Empty
            : _selectedParser?.CrmTemplate ?? string.Empty;

    // Field visibility is vendor-driven, mirroring the web ParseSettingsCard branches:
    //   Zebra            → template dropdown + On Cost % (optional); Uplift adds the Uplift field
    //   HP (multi/single)→ HpSettingsBlock: Uplift/PercentOff add Uplift; PercentOff adds Discount Off MSRP
    //   Nutanix / Lenovo → NutanixSettingsBlock: FX Rate + Uplift, regardless of CRM template
    // NOTE: Lenovo's CRM template is "No Calculation", but it still shows FX Rate + Uplift like
    // Nutanix (the NoCalculation writer ignores them) — so visibility keys off the vendor, not
    // the template. A purely template-driven rule would wrongly hide both fields for Lenovo.
    private void UpdateConditionalFields()
    {
        var parser = _selectedParser;
        var template = EffectiveTemplate;
        var isZebra = parser?.Vendor == VendorNames.Zebra;
        var isHp = parser?.Vendor == VendorNames.Hp;
        var isMultiTemplate = parser is { } p && p.AvailableTemplates.Count > 1;
        var isNutanixBlock = parser is not null && !isZebra && !isHp;

        ShowTemplateDropdown = isMultiTemplate;
        ShowFxRate = isNutanixBlock;
        ShowMargin = isNutanixBlock
            || (isHp && template is CrmTemplates.Uplift or CrmTemplates.PercentOffWithUplift)
            || (isZebra && template == CrmTemplates.Uplift);
        ShowImPercent = isHp && template == CrmTemplates.PercentOffWithUplift;
        ShowOnCostPercent = isZebra;
        CrmTemplateDisplay = template;
        OnPropertyChanged(nameof(HasCrmTemplate));

        _convertCommand.RaiseCanExecuteChanged();
    }

    private bool GetCanConvert()
    {
        if (_state == ParseState.Running) return false;
        if (_selectedParser is null || string.IsNullOrEmpty(_inputFilePath)) return false;
        if (_showTemplateDropdown && string.IsNullOrEmpty(_selectedTemplate)) return false;
        if (_showFxRate && !TryParsePositive(_fxRate, out _)) return false;
        if (_showMargin && !TryParseNonNegative(_margin, out _)) return false;
        if (_showImPercent && !TryParseNonNegative(_imPercent, out _)) return false;
        return true;
    }

    private async Task ExecuteConvertAsync()
    {
        var inputPath = _inputFilePath!;

        // Ask the user where to save (and what to name) the parsed workbook before
        // doing any work. Cancelling the dialog aborts cleanly — no parse, no file,
        // and the current state is left untouched. When no prompt is wired (e.g. a
        // headless test), fall back to the default beside-input path.
        string? outputPath = null;
        if (SaveFilePrompt is not null)
        {
            var defaultPath = Path.Combine(
                Path.GetDirectoryName(inputPath)!,
                OutputNaming.OutputFilename(Path.GetFileName(inputPath)));
            outputPath = SaveFilePrompt(defaultPath);
            if (string.IsNullOrEmpty(outputPath)) return;
        }

        State = ParseState.Running;
        ErrorMessage = null;
        ErrorStage = null;

        var runner = new ParseRunner();
        var vendor = _selectedVendor!;
        var slug = _selectedParser!.Slug;
        var fxRate = TryParsePositive(_fxRate, out var fx) ? fx : (decimal?)null;
        var margin = TryParseNonNegative(_margin, out var m) ? m : (decimal?)null;
        var imPct = TryParseNonNegative(_imPercent, out var im) ? im : (decimal?)null;
        var onCost = TryParseNonNegative(_onCostPercent, out var oc) ? oc : (decimal?)null;
        var template = EffectiveTemplate;

        try
        {
            var outcome = await Task.Run(() => runner.Run(inputPath, vendor, slug, fxRate, margin, imPct, onCost, template, outputPath));
            ApplyResult(outcome);
        }
        catch (ParseError pe)
        {
            ErrorMessage = pe.Message;
            ErrorStage = pe.Stage;
            State = ParseState.Error;
        }
        catch (ParseValidationException pve)
        {
            ErrorMessage = pve.Message;
            ErrorStage = null;
            State = ParseState.Error;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ErrorStage = null;
            State = ParseState.Error;
        }
    }

    private void ApplyResult(ParseOutcome outcome)
    {
        var v = outcome.Validation;

        _resultComputedTotal = $"{outcome.Currency} {v.ComputedTotal:N2}";
        _resultQuotedTotal = v.QuotedTotal.HasValue ? $"{outcome.Currency} {v.QuotedTotal:N2}" : "N/A";
        _resultTotalsMatch = v.Matches;
        _resultDifference = $"{outcome.Currency} {Math.Abs(v.Difference):N2}";
        _resultOutputPath = outcome.OutputPath;
        _resultOutputFolder = Path.GetDirectoryName(outcome.OutputPath) ?? string.Empty;

        _cancelledLinesDisplay = outcome.CancelledLines
            .Select(c => string.IsNullOrEmpty(c.Line) ? c.Vpn : $"{c.Line} – {c.Vpn}")
            .ToList();
        _hasCancelledLines = _cancelledLinesDisplay.Count > 0;

        _resultReportType = _selectedParser is { } p ? ReportTypes.For(p.Slug) ?? string.Empty : string.Empty;
        _hasResultReportType = _resultReportType.Length > 0;

        OnPropertyChanged(nameof(ResultComputedTotal));
        OnPropertyChanged(nameof(ResultQuotedTotal));
        OnPropertyChanged(nameof(ResultTotalsMatch));
        OnPropertyChanged(nameof(ResultDifference));
        OnPropertyChanged(nameof(ResultOutputPath));
        OnPropertyChanged(nameof(ResultOutputFolder));
        OnPropertyChanged(nameof(CancelledLinesDisplay));
        OnPropertyChanged(nameof(HasCancelledLines));
        OnPropertyChanged(nameof(ResultReportType));
        OnPropertyChanged(nameof(HasResultReportType));

        var hasMismatch = !v.Matches && v.QuotedTotal.HasValue;
        State = (hasMismatch || _hasCancelledLines) ? ParseState.Warning : ParseState.Success;
    }

    // Restore the app to its launch defaults: clear the file, numeric inputs, and
    // any result/error, then reset the vendor → file-type → template selection.
    private void Reset()
    {
        _inputFilePath = null;
        _inputFileName = string.Empty;
        _inputFileSizeDisplay = string.Empty;
        OnPropertyChanged(nameof(InputFilePath));
        OnPropertyChanged(nameof(InputFileName));
        OnPropertyChanged(nameof(InputFileSizeDisplay));
        OnPropertyChanged(nameof(HasInputFile));

        FxRate = string.Empty;
        Margin = string.Empty;
        ImPercent = string.Empty;
        OnCostPercent = string.Empty;

        ErrorMessage = null;
        ErrorStage = null;
        State = ParseState.Idle;

        // Re-pick the defaults. Each assignment no-ops when already at the default,
        // so this restores the full cascade whether the vendor, parser, or template
        // was the one changed.
        SelectedVendor = Vendors.FirstOrDefault();
        SelectedParser = Parsers.FirstOrDefault();
        SelectedTemplate = Templates.FirstOrDefault();

        _convertCommand.RaiseCanExecuteChanged();
    }

    private void OpenFolder()
    {
        if (string.IsNullOrEmpty(_resultOutputFolder)) return;
        Process.Start("explorer.exe", $"/select,\"{_resultOutputPath}\"");
    }

    private void OpenFile()
    {
        if (string.IsNullOrEmpty(_resultOutputPath)) return;
        Process.Start(new ProcessStartInfo(_resultOutputPath) { UseShellExecute = true });
    }

    // Opens the GitHub releases page in the default browser so users can grab the
    // latest build. Best-effort: a missing browser must never crash the app.
    private static void OpenReleases()
    {
        try
        {
            Process.Start(new ProcessStartInfo(ReleasesUrl) { UseShellExecute = true });
        }
        catch
        {
            // No default browser / blocked — nothing useful to do, so swallow it.
        }
    }

    private static bool TryParsePositive(string s, out decimal v)
        => decimal.TryParse(s.Trim(), out v) && v > 0;

    private static bool TryParseNonNegative(string s, out decimal v)
        => decimal.TryParse(s.Trim(), out v) && v >= 0;

    private static string FormatFileSize(long bytes) =>
        bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
        };
}
