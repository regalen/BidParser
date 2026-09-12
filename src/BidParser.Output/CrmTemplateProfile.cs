using BidParser.Domain.Constants;

namespace BidParser.Output;

/// <summary>How a price column is emitted for a given CRM template.</summary>
internal enum PriceWrite
{
    /// <summary>The column is not part of this template's calculation mode — never written.</summary>
    Never,

    /// <summary>Written only when the parser populated the value; blank otherwise.</summary>
    WhenPresent,

    /// <summary>Always written, with zero mapped to <see cref="TemplateLayout.ZeroPriceSentinel"/>.</summary>
    AlwaysWithSentinel,
}

/// <summary>
/// Which columns of the shared 27-column ANZ-GENERIC layout a given CRM template populates.
/// Every CRM upload template is the same workbook — identical headers in identical positions —
/// and differs only in the calculation mode it drives, i.e. which columns it consumes. This
/// record is that difference, so <see cref="CrmWriter"/> stays format-agnostic and adding a
/// template is one entry here rather than a new writer.
/// </summary>
/// <remarks>
/// Columns are gated on these flags rather than on the value being null. Null-gating would mean
/// the first parser to populate a field silently starts emitting a column its CRM template does
/// not consume.
/// </remarks>
internal sealed record CrmTemplateProfile(
    string SheetName,
    PriceWrite Msrp, int MsrpColumn,   // 8 (H) for local, 21 (U) for foreign
    PriceWrite Cost, int CostColumn,   // 9 (I) for local, 20 (T) for foreign
    bool Margin,               // col K
    bool ImPercent,            // col X
    bool OnCost,               // col Z
    bool MinQty,               // col W
    bool Term,                 // col N, or col R when termAsComment
    bool Dates,                // cols P/Q
    bool Comments,             // col R
    bool ForeignBlock,         // cols S + V
    bool NumericItemFallback)  // col A when the parser sets no LineSequence: number vs text
{
    /// <summary>Returns the profile for a CRM template, or null when the template is unknown.</summary>
    public static CrmTemplateProfile? For(string crmTemplate) => crmTemplate switch
    {
        CrmTemplates.ForeignUplift => new(
            SheetName: CrmTemplates.ForeignUplift,
            Msrp: PriceWrite.AlwaysWithSentinel, MsrpColumn: 21,
            Cost: PriceWrite.AlwaysWithSentinel, CostColumn: 20,
            Margin: true,
            ImPercent: false,
            OnCost: false,
            MinQty: false,
            Term: true,
            Dates: true,
            Comments: true,
            ForeignBlock: true,
            NumericItemFallback: true),

        CrmTemplates.NoCalculation => new(
            SheetName: CrmTemplates.NoCalculation,
            Msrp: PriceWrite.WhenPresent, MsrpColumn: 8,
            Cost: PriceWrite.AlwaysWithSentinel, CostColumn: 9,
            Margin: false,
            ImPercent: false,
            OnCost: true,
            MinQty: true,
            Term: false,
            Dates: true,
            Comments: true,
            ForeignBlock: false,
            NumericItemFallback: false),

        CrmTemplates.Uplift => new(
            SheetName: CrmTemplates.Uplift,
            Msrp: PriceWrite.WhenPresent, MsrpColumn: 8,
            Cost: PriceWrite.AlwaysWithSentinel, CostColumn: 9,
            Margin: true,
            ImPercent: false,
            OnCost: true,
            MinQty: true,
            Term: false,
            Dates: true,
            Comments: true,
            ForeignBlock: false,
            NumericItemFallback: false),

        CrmTemplates.PercentOffWithUplift => new(
            SheetName: CrmTemplates.PercentOffWithUplift,
            Msrp: PriceWrite.AlwaysWithSentinel, MsrpColumn: 8,
            Cost: PriceWrite.Never, CostColumn: 9,
            Margin: true,
            ImPercent: true,
            OnCost: false,
            MinQty: false,
            Term: false,
            Dates: false,
            Comments: false,
            ForeignBlock: false,
            NumericItemFallback: false),

        _ => null,
    };
}
