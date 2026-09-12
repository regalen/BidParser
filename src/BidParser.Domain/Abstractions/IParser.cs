using BidParser.Domain.Constants;
using BidParser.Domain.Models;

namespace BidParser.Domain.Abstractions;

/// <summary>
/// The single contract every supplier-format parser implements. Adding a format is one
/// class implementing this interface plus one <c>ParserRegistry</c> entry — no other wiring.
/// </summary>
public interface IParser
{
    /// <summary>Stable identifier (e.g. "hp_bid_xlsx"); keys runtime guidance and the registry.</summary>
    string Slug { get; }
    /// <summary>Human-readable name shown in the UI file-type dropdown.</summary>
    string DisplayName { get; }
    /// <summary>Owning vendor (see <c>Vendors.*</c>); groups siblings for the wrong-file-type flow.</summary>
    string Vendor { get; }
    /// <summary>
    /// Vendor name written to Col B (Vendor Name) of the CRM workbook. Defaults to <see cref="Vendor"/>;
    /// override only when the UI vendor grouping is finer-grained than the name CRM expects (the Lenovo
    /// ISG/IDG split). A presentation trait of the format, so the parser declares it and the writer
    /// obeys — writers must never special-case a vendor or slug.
    /// </summary>
    string OutputVendorName => Vendor;
    /// <summary>Legacy/primary MIME type this parser accepts (retained for source/API compatibility).</summary>
    string AcceptedMime { get; }
    /// <summary>Authoritative set of MIME types this parser accepts; used for extension validation and format detection. Defaults to [<see cref="AcceptedMime"/>].</summary>
    IReadOnlyList<string> AcceptedMimes => [AcceptedMime];
    /// <summary>Default CRM output template when the caller does not specify one.</summary>
    string CrmTemplate { get; }
    /// <summary>Templates the caller may choose. Defaults to just <see cref="CrmTemplate"/>; override for multi-template parsers.</summary>
    IReadOnlyList<string> AvailableTemplates => [CrmTemplate];

    /// <summary>
    /// True when this parser honours <see cref="ParseOptions.IncludeSubComponentDetail"/>.
    /// Surfaced by /api/parsers as a capability flag; it no longer drives any UI, since
    /// sub-component suppression is unconditional. Defaults to false.
    /// </summary>
    bool SupportsSubComponentDetail => false;

    /// <summary>
    /// True when this format populates <see cref="LineItem.SolutionId"/> on every line, so the
    /// caller may ask for the output to be split into one workbook per Solution ID.
    /// Surfaced by /api/parsers as a capability flag. Defaults to false.
    /// </summary>
    bool SupportsSolutionIdSplit => false;

    /// <summary>
    /// True when this format accepts an optional On Cost percentage for CRM templates that write it.
    /// Surfaced by /api/parsers and enforced by ParseService. Defaults to false.
    /// </summary>
    bool SupportsOnCost => false;

    /// <summary>
    /// Name of the dimension SupportsSolutionIdSplit groups by, used for UI labels and
    /// messages. A presentation trait of the format, so the parser declares it and generic code
    /// obeys. Defaults to "Solution ID".
    /// </summary>
    string SolutionSplitLabel => "Solution ID";

    /// <summary>How this format's output filenames are composed. Defaults to BidScoped.</summary>
    OutputNameStyle OutputNameStyle => OutputNameStyle.BidScoped;

    /// <summary>
    /// Whether this format expresses the subscription term as a human-readable comment
    /// (<c>"{term} Months"</c> in the Comments column) rather than the numeric
    /// Warranty/Duration column. A presentation trait of the source format, so the format
    /// declares it and the writer obeys — writers must not special-case vendors or slugs.
    /// Defaults to <see langword="false"/> (numeric column).
    /// </summary>
    bool TermRendersAsComment => false;

    /// <summary>Extracts line items + metadata from the file at <paramref name="path"/>.</summary>
    ParseResult Parse(string path);

    /// <summary>
    /// Extracts line items + metadata honouring <paramref name="options"/>.
    /// Defaults to the option-free overload for parsers that take no options.
    /// </summary>
    ParseResult Parse(string path, ParseOptions options) => Parse(path);

    /// <summary>
    /// Soft recognition score 0.0–1.0 — a hint, never routing. Only the wrong-file-type
    /// flow consumes it (to name the likely-correct sibling type). Defaults to 0.0.
    /// </summary>
    double Detect(string path) => 0.0;
}
