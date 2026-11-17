using BidParser.Application.Parsing;
using BidParser.Output;

namespace BidParser.Application.Output;

public sealed record LinePricingLine(
    int Index,
    string LineNumber,
    string Vpn,
    string? Description,
    int Qty,
    decimal? Msrp,
    decimal? SourceCost,
    decimal? DerivedImPercent,
    bool IsCancelled);

public sealed record LinePricingPreview(
    string ParserSlug,
    string ParserDisplayName,
    string Currency,
    string CrmTemplate,
    bool UsesFxRate,
    bool ImPercentEditable,
    bool ImPercentRequired,
    IReadOnlyList<LinePricingLine> Lines)
{
    public static LinePricingPreview Build(ParsedQuote quote, string? crmTemplate = null)
    {
        var parser = quote.Parser;
        var template = WorkbookWriteService.ResolveTemplate(quote, crmTemplate, splitBySolutionId: false);
        var capabilities = CrmWriter.GetCapabilities(template, parser.CalculatesCostFromMsrp)!;

        var lines = new List<LinePricingLine>(quote.Result.LineItems.Count);
        for (var i = 0; i < quote.Result.LineItems.Count; i++)
        {
            var item = quote.Result.LineItems[i];
            var lineNumber = item.LineSequence ?? (i + 1).ToString();
            decimal? sourceCost = parser.CalculatesCostFromMsrp ? null : item.Cost;
            decimal? derivedIm = parser.CalculatesCostFromMsrp
                ? null
                : LinePricingMath.DerivedImPercent(item.Cost, item.Msrp);

            lines.Add(new LinePricingLine(
                Index: i,
                LineNumber: lineNumber,
                Vpn: item.Vpn,
                Description: item.Description,
                Qty: item.Qty,
                Msrp: item.Msrp,
                SourceCost: sourceCost,
                DerivedImPercent: derivedIm,
                IsCancelled: item.IsCancelled));
        }

        var imPercentEditable = capabilities.UsesCostColumn
            && quote.Result.LineItems.Any(item => !item.IsCancelled && item.Msrp is > 0m);

        return new LinePricingPreview(
            ParserSlug: parser.Slug,
            ParserDisplayName: parser.DisplayName,
            Currency: quote.Result.Metadata.Currency,
            CrmTemplate: template,
            UsesFxRate: capabilities.UsesFxRate,
            ImPercentEditable: imPercentEditable,
            ImPercentRequired: parser.CalculatesCostFromMsrp,
            Lines: lines);
    }
}
