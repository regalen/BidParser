using System.Globalization;
using System.IO;
using BidParser.Application.Parsing;
using BidParser.Desktop.Configuration;
using BidParser.Desktop.Mvvm;
using BidParser.Output;

namespace BidParser.Desktop.ViewModels;

public sealed record VendorViewModel(string Name, IReadOnlyList<ParserOptionViewModel> Parsers)
{
    public override string ToString() => Name;
}

public sealed record ParserOptionViewModel(ParserDescriptor Descriptor)
{
    public string DisplayName => Descriptor.DisplayName;

    public override string ToString() => DisplayName;
}

public sealed record TemplateOptionViewModel(string Name)
{
    public override string ToString() => Name;
}

public sealed record SelectedFileViewModel(
    string Path,
    string DisplayName,
    long SizeBytes,
    DateTimeOffset AddedAt)
{
    public static SelectedFileViewModel FromPath(string path)
    {
        var info = new FileInfo(path);
        return new SelectedFileViewModel(info.FullName, info.Name, info.Length, DateTimeOffset.Now);
    }

    /// <summary>Uppercase extension badge, e.g. "PDF".</summary>
    public string Badge => System.IO.Path.GetExtension(DisplayName).TrimStart('.').ToUpperInvariant();

    public string Metadata
    {
        get
        {
            var folder = System.IO.Path.GetDirectoryName(Path);
            var size = FormatSize(SizeBytes);
            var added = AddedAt.ToString("HH:mm", CultureInfo.CurrentCulture);
            return string.IsNullOrEmpty(folder)
                ? $"{size} · added {added}"
                : $"{size} · {folder} · added {added}";
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024d:F0} KB",
        _ => $"{bytes / (1024d * 1024d):F1} MB"
    };
}

/// <summary>The severity of the status line rendered beside a held file.</summary>
public enum FileStatusSeverity
{
    Neutral,
    Working,
    Done
}

public abstract record DropZoneViewModel;

public sealed record EmptyDropZoneViewModel(string AcceptedCaption) : DropZoneViewModel;

public sealed record DragOverDropZoneViewModel : DropZoneViewModel;

/// <summary>The selected, parsing and parsed states — one file card, three status lines.</summary>
public sealed record FileDropZoneViewModel(
    SelectedFileViewModel File,
    string Status,
    FileStatusSeverity Severity,
    bool CanReplace) : DropZoneViewModel;

public sealed record InvalidDropZoneViewModel(string Headline, string Detail) : DropZoneViewModel;

public sealed record ResultFactViewModel(string Label, string Value);

/// <summary>Result severity: mismatch is a warning that still saves, never an error.</summary>
public enum ResultSeverity
{
    Success,
    Warning
}

public abstract class ResultViewModel : ObservableObject;

public sealed class NoResultViewModel : ResultViewModel;

/// <summary>A parse that produced line items — success or mismatch. Saving stays available on both.</summary>
public abstract class ParsedResultViewModel : ResultViewModel
{
    private string? savedPath;

    public abstract ResultSeverity Severity { get; }

    public required string Headline { get; init; }
    public required string Detail { get; init; }
    public IReadOnlyList<ResultFactViewModel> Facts { get; init; } = [];
    public IReadOnlyList<string> Notes { get; init; } = [];
    /// <summary>Validated parser guidance, already converted to a safe presentation model.</summary>
    public GuidanceDocument? Guidance { get; init; }

    public string? SavedPath
    {
        get => savedPath;
        set
        {
            if (SetProperty(ref savedPath, value))
            {
                OnPropertyChanged(nameof(SavedMessage));
                OnPropertyChanged(nameof(HasSaved));
            }
        }
    }

    public bool HasSaved => savedPath is not null;

    public string? SavedMessage => savedPath is null ? null : $"Workbook saved to {savedPath}";
}

public sealed class SuccessResultViewModel : ParsedResultViewModel
{
    public override ResultSeverity Severity => ResultSeverity.Success;
}

public sealed class WarningResultViewModel : ParsedResultViewModel
{
    public override ResultSeverity Severity => ResultSeverity.Warning;
}

public sealed class FailureResultViewModel : ResultViewModel
{
    public required string Headline { get; init; }
    public required string Cause { get; init; }
    public required string NextStep { get; init; }

    /// <summary>Parser, stage, exception type, version and timestamp — never a stack trace.</summary>
    public required string TechnicalDetails { get; init; }
}

/// <summary>The blocking overwrite decision — the only overlay dialog in the window.</summary>
public sealed class OverwriteDialogViewModel(
    string filename,
    string directory,
    Action replace,
    Action saveCopy,
    Action cancel)
{
    public string Title => "Replace existing workbook?";

    public string Message => $"{filename} already exists in {directory}. Replacing it can't be undone.";

    public RelayCommand SaveCopyCommand { get; } = new(saveCopy);
    public RelayCommand ReplaceCommand { get; } = new(replace);
    public RelayCommand CancelCommand { get; } = new(cancel);
}

public sealed record EffectiveCapabilities(
    bool SupportsFx,
    bool SupportsUplift,
    bool SupportsDiscountOffMsrp,
    bool SupportsOnCost,
    bool SupportsSplit,
    bool RequiresFx = false,
    bool RequiresUplift = false,
    bool RequiresDiscountOffMsrp = false)
{
    public static readonly EffectiveCapabilities None = new(false, false, false, false, false);

    public bool HasAnyPricingField => SupportsFx || SupportsUplift || SupportsDiscountOffMsrp || SupportsOnCost;

    public static EffectiveCapabilities From(ParserDescriptor parser, string template)
    {
        var templateCapabilities = CrmWriter.GetCapabilities(template);
        if (templateCapabilities is null)
        {
            return None with { SupportsSplit = parser.SupportsSolutionIdSplit };
        }

        return new EffectiveCapabilities(
            templateCapabilities.UsesFxRate,
            templateCapabilities.UsesUplift,
            templateCapabilities.UsesDiscountOffMsrp,
            parser.SupportsOnCost && templateCapabilities.SupportsOnCost,
            parser.SupportsSolutionIdSplit,
            templateCapabilities.RequiresFxRate,
            templateCapabilities.RequiresUplift,
            templateCapabilities.RequiresDiscountOffMsrp);
    }
}
