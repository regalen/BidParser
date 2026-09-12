using BidParser.Domain.Models;
using ClosedXML.Excel;

namespace BidParser.Output;

public sealed record CrmWriterOptions(
    string VendorName,             // required — callers state the vendor explicitly
    decimal Margin = 5.00m,
    decimal FxRate = 1.000m,
    string Currency = "USD",
    decimal? ImPercent = null,
    decimal? OnCost = null,
    bool TermAsComment = false);

/// <summary>Input capabilities projected from the same profile that drives workbook columns.</summary>
public sealed record CrmTemplateCapabilities(
    bool UsesFxRate,
    bool RequiresFxRate,
    bool UsesUplift,
    bool RequiresUplift,
    bool UsesDiscountOffMsrp,
    bool RequiresDiscountOffMsrp,
    bool SupportsOnCost);

/// <summary>
/// The single output writer. Every CRM upload template is the same 27-column ANZ-GENERIC
/// workbook, so the format-specific part is which columns to populate — that lives in
/// <see cref="CrmTemplateProfile"/>. Column order comes from <see cref="TemplateLayout"/>.
/// </summary>
public static class CrmWriter
{
    /// <summary>
    /// Whether a CRM template has a writer profile. Lets callers reject an unknown template with
    /// their own error before writing, rather than catching the <see cref="ArgumentException"/>
    /// from <see cref="Write"/> — which would also swallow genuine write failures.
    /// </summary>
    public static bool IsSupported(string crmTemplate) => CrmTemplateProfile.For(crmTemplate) is not null;

    /// <summary>Returns UI/input capabilities without duplicating template rules in a host.</summary>
    public static CrmTemplateCapabilities? GetCapabilities(string crmTemplate)
    {
        var profile = CrmTemplateProfile.For(crmTemplate);
        return profile is null
            ? null
            : new CrmTemplateCapabilities(
                UsesFxRate: profile.ForeignBlock,
                RequiresFxRate: profile.ForeignBlock,
                UsesUplift: profile.Margin,
                RequiresUplift: profile.Margin,
                UsesDiscountOffMsrp: profile.ImPercent,
                RequiresDiscountOffMsrp: profile.ImPercent,
                SupportsOnCost: profile.OnCost);
    }

    public static string Write(
        IEnumerable<LineItem> items,
        string outputPath,
        string crmTemplate,
        CrmWriterOptions options)
    {
        var profile = CrmTemplateProfile.For(crmTemplate)
            ?? throw new ArgumentException($"Unsupported CRM template: '{crmTemplate}'", nameof(crmTemplate));

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(profile.SheetName);

        TemplateLayout.WriteHeaders(sheet);

        var rowNumber = 3;
        var fallbackIndex = 1;

        foreach (var item in items)
        {
            // Col A — Item. The parser's line sequence ("1", "1.01", …) when it sets one,
            // otherwise a running counter. The counter's cell type is per-template: the local
            // templates number items as text so col A is uniformly text alongside the "1.01"
            // child sequences, while Foreign Uplift numbers them as real numbers.
            if (item.LineSequence is not null)
            {
                sheet.Cell(rowNumber, 1).Value = item.LineSequence;
            }
            else if (profile.NumericItemFallback)
            {
                sheet.Cell(rowNumber, 1).Value = fallbackIndex;
            }
            else
            {
                sheet.Cell(rowNumber, 1).Value = fallbackIndex.ToString();
            }

            // Col B — Vendor Name
            sheet.Cell(rowNumber, 2).Value = options.VendorName;

            // Col D — Vendor Part Number
            sheet.Cell(rowNumber, 4).Value = item.Vpn;

            // Col E — Description
            if (item.Description is not null)
            {
                sheet.Cell(rowNumber, 5).Value = item.Description;
            }

            // Col M — Serial Number
            if (item.SerialNumber is not null)
            {
                sheet.Cell(rowNumber, 13).Value = item.SerialNumber;
            }

            if (item.IsCancelled)
            {
                // Cancelled lines: downstream system pulls standard pricing from SAP.
                // Col F — Qty: always 1 (ignore source qty).
                sheet.Cell(rowNumber, 6).Value = 1;

                // Col H (MSRP) and col I (Cost) carry TemplateLayout.NoBidPrice — a literal 0,
                // deliberately NOT the zero-price sentinel. A cancelled line belongs on the
                // quote but is not bid-priced, so we want CRM to reject the 0 and pull the
                // line's standard price from SAP. See TemplateLayout's "two zero prices".
                if (profile.Msrp != PriceWrite.Never)
                {
                    sheet.Cell(rowNumber, profile.MsrpColumn).Value = TemplateLayout.NoBidPrice;
                }
                if (profile.Cost != PriceWrite.Never)
                {
                    sheet.Cell(rowNumber, profile.CostColumn).Value = TemplateLayout.NoBidPrice;
                }

                // Col W (Min Order Qty) and col Z (On Cost %) are intentionally left blank.
                // Col R — Comments
                if (item.Comments is not null)
                {
                    sheet.Cell(rowNumber, 18).Value = item.Comments;
                }
            }
            else
            {
                // Col F — Qty.
                sheet.Cell(rowNumber, 6).Value = item.Qty;

                // Col H or U — MSRP
                if (profile.Msrp == PriceWrite.AlwaysWithSentinel)
                {
                    sheet.Cell(rowNumber, profile.MsrpColumn).Value = TemplateLayout.NonZeroPrice(item.Msrp ?? 0m);
                }
                else if (profile.Msrp == PriceWrite.WhenPresent && item.Msrp is not null)
                {
                    sheet.Cell(rowNumber, profile.MsrpColumn).Value = TemplateLayout.NonZeroPrice(item.Msrp.Value);
                }

                // Col I or T — Cost. LineItem.Cost is non-nullable, so "when present" and
                // "always" coincide; only Never is distinct.
                if (profile.Cost != PriceWrite.Never)
                {
                    sheet.Cell(rowNumber, profile.CostColumn).Value = TemplateLayout.NonZeroPrice(item.Cost);
                }

                // Col K — Margin
                if (profile.Margin)
                {
                    sheet.Cell(rowNumber, 11).Value = options.Margin;
                }

                // Col N or R — Term
                if (profile.Term && item.Term is >= 1)
                {
                    if (options.TermAsComment)
                    {
                        sheet.Cell(rowNumber, 18).Value = $"{item.Term.Value} Months";
                    }
                    else
                    {
                        sheet.Cell(rowNumber, 14).Value = item.Term.Value;
                    }
                }

                // Col P — Start Date
                if (profile.Dates && item.StartDate is not null)
                {
                    TemplateLayout.SetDate(sheet.Cell(rowNumber, 16), item.StartDate.Value);
                }

                // Col Q — End Date
                if (profile.Dates && item.EndDate is not null)
                {
                    TemplateLayout.SetDate(sheet.Cell(rowNumber, 17), item.EndDate.Value);
                }

                // Col R — Comments
                if (profile.Comments && item.Comments is not null)
                {
                    sheet.Cell(rowNumber, 18).Value = item.Comments;
                }

                // Cols S & V — Foreign Block (Currency & FX Rate)
                if (profile.ForeignBlock)
                {
                    sheet.Cell(rowNumber, 19).Value = options.Currency;
                    sheet.Cell(rowNumber, 22).Value = options.FxRate;
                }

                // Col W — Min Order Qty
                if (profile.MinQty && item.MinQty is not null)
                {
                    sheet.Cell(rowNumber, 23).Value = item.MinQty.Value;
                }

                // Col X — IM%
                if (profile.ImPercent && options.ImPercent is not null)
                {
                    sheet.Cell(rowNumber, 24).Value = options.ImPercent.Value;
                }

                // Col Z — On Cost %
                if (profile.OnCost && options.OnCost is not null)
                {
                    sheet.Cell(rowNumber, 26).Value = options.OnCost.Value;
                }
            }

            rowNumber++;
            fallbackIndex++;
        }

        // End-loop sentinel row
        sheet.Cell(rowNumber, 2).Value = "*";
        sheet.Cell(rowNumber, 4).Value = TemplateLayout.EndLoopWarning;

        return TemplateLayout.Save(workbook, outputPath);
    }
}
