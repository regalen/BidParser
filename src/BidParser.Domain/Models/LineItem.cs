namespace BidParser.Domain.Models;

/// <summary>
/// One extracted quote line — the shared output of every parser. Only <see cref="Vpn"/>,
/// <see cref="Cost"/> and <see cref="Qty"/> are required; the rest are populated per format.
/// Field names are the locked canonical vocabulary (see AGENTS.md) — note <see cref="Cost"/>
/// is the customer "Sale Price" and <see cref="Msrp"/> the "List Price".
/// </summary>
public sealed record LineItem
{
    /// <summary>Vendor Part Number (display header "Part Number"). Required.</summary>
    public required string Vpn { get; init; }
    /// <summary>Line description.</summary>
    public string? Description { get; init; }
    /// <summary>Subscription/warranty term in months (display header "Term").</summary>
    public int? Term { get; init; }
    /// <summary>List / catalogue price (display header "List Price").</summary>
    public decimal? Msrp { get; init; }
    /// <summary>Customer price (display header "Sale Price"). Required; drives Σ(cost × qty) validation.</summary>
    public required decimal Cost { get; init; }
    /// <summary>Quantity. Required.</summary>
    public required int Qty { get; init; }
    /// <summary>Serial number, may embed a license key (display header "Serial Number").</summary>
    public string? SerialNumber { get; init; }
    /// <summary>Subscription start date (display header "Start Date").</summary>
    public DateOnly? StartDate { get; init; }
    /// <summary>Subscription end date (display header "End Date").</summary>
    public DateOnly? EndDate { get; init; }
    /// <summary>Free-text output comments (col R of ANZ-GENERIC); null = blank.</summary>
    public string? Comments { get; init; }
    /// <summary>Every source cell captured verbatim, keyed by source label — provenance for debugging.</summary>
    public IReadOnlyDictionary<string, string> Raw { get; init; } = new Dictionary<string, string>();
    /// <summary>Minimum order quantity (HP only; null for Nutanix).</summary>
    public int? MinQty { get; init; }
    /// <summary>Configurator Solution ID the line belongs to (Lenovo LBP-E ISG only; null elsewhere). Set on parents and children alike so the output can be split by solution.</summary>
    public string? SolutionId { get; init; }
    /// <summary>Output line sequence string (HP only; col A of ANZ-GENERIC, e.g. "4.01").</summary>
    public string? LineSequence { get; init; }
    /// <summary>True when the source marks the line cancelled — the line is quoted but not bid-priced, so writers emit qty 1 and the no-bid price (a literal 0 that CRM answers from SAP), not the zero-price sentinel.</summary>
    public bool IsCancelled { get; init; }
}
