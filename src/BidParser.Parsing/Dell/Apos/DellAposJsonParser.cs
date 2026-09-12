using System.Text.Json;
using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;

namespace BidParser.Parsing.Dell.Apos;

/// <summary>
/// Parses Dell "APOS" quotes (JSON). Flattens every item's SKUs, requires at least one non-empty
/// service-tag number (its presence distinguishes APOS from CTO), and orders lines by service tag.
/// Uses the "No Calculation" output. See docs/dell_apos_json.md.
/// </summary>
public sealed class DellAposJsonParser : IParser
{
    public string Slug => ParserSlugs.DellAposJson;
    public string DisplayName => "APOS (API)";
    public string Vendor => Vendors.Dell;
    public string AcceptedMime => "application/json";
    public string CrmTemplate => CrmTemplates.NoCalculation;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ParseResult Parse(string path)
    {
        var content = File.ReadAllText(path);

        DellAposQuote? quote;
        try
        {
            quote = JsonSerializer.Deserialize<DellAposQuote>(content, Options);
        }
        catch (JsonException)
        {
            throw WrongFileType();
        }

        if (quote?.Items is not { Count: > 0 })
        {
            throw WrongFileType();
        }

        var sourceSkus = quote.Items
            .Where(i => i.Skus is not null)
            .SelectMany(i => i.Skus!)
            .ToList();

        if (sourceSkus.Count == 0)
        {
            throw WrongFileType();
        }

        if (!sourceSkus.Any(s => HasServiceTag(s.ServiceTags?.ServiceTagNumber)))
        {
            throw WrongFileType();
        }

        // Every source SKU is emitted. The parent/child restatement rules are a CTO structure —
        // APOS has no parent lines, so its SKUs are the priced output and dropping one would break
        // the Σ(cost × qty) reconciliation against the quote's salesPrice.
        var sortedSkus = sourceSkus
            .OrderBy(s => s.ServiceTags?.ServiceTagNumber ?? string.Empty, StringComparer.Ordinal)
            .ToList();

        var items = new List<LineItem>();
        for (var idx = 0; idx < sortedSkus.Count; idx++)
        {
            var sku = sortedSkus[idx];
            items.Add(new LineItem
            {
                LineSequence = (idx + 1).ToString(),
                Vpn = sku.SkuNumber ?? string.Empty,
                Description = string.IsNullOrWhiteSpace(sku.LineOfBusiness) || sku.LineOfBusiness == "Misc"
                    ? sku.Description
                    : $"{sku.Description} - {sku.LineOfBusiness}",
                Qty = sku.Quantity,
                Msrp = sku.UnitListPrice,
                Cost = sku.UnitSalesPrice ?? 0m,
                StartDate = ToDate(sku.ServiceTags?.NewContractStartDate),
                EndDate = ToDate(sku.ServiceTags?.NewContractEndDate),
                SerialNumber = sku.ServiceTags?.ServiceTagNumber,
            });
        }

        var validation = ParseValidation.Validate(items, quote.SalesPrice);
        var bid = BidMetadataCleaner.Clean(quote.QuoteNumber?.ToString(), quote.QuoteVersion?.ToString());

        return new ParseResult
        {
            Metadata = new QuoteMetadata
            {
                QuoteNumber = quote.QuoteNumber?.ToString() ?? Path.GetFileNameWithoutExtension(path),
                BidNumber = bid.BidNumber,
                BidRevision = bid.BidRevision,
                Supplier = Vendor,
                Currency = string.IsNullOrEmpty(quote.Currency) ? "AUD" : quote.Currency,
                QuotedTotal = quote.SalesPrice,
                SourceFilename = Path.GetFileName(path),
                ParserSlug = Slug,
            },
            LineItems = items,
            Validation = validation,
        };
    }

    public double Detect(string path)
    {
        try
        {
            var content = File.ReadAllText(path);
            var quote = JsonSerializer.Deserialize<DellAposQuote>(content, Options);
            if (quote?.Items is not { Count: > 0 })
            {
                return 0.0;
            }

            var hasServiceTag = quote.Items.Any(i => i.Skus?.Any(s => HasServiceTag(s.ServiceTags?.ServiceTagNumber)) == true);
            return hasServiceTag ? 0.9 : 0.0;
        }
        catch
        {
            return 0.0;
        }
    }

    private static bool HasServiceTag(string? serviceTagNumber) =>
        !string.IsNullOrWhiteSpace(serviceTagNumber);

    private static DateOnly? ToDate(DateTime? dateTime)
    {
        if (dateTime is null || dateTime.Value == DateTime.MinValue)
        {
            return null;
        }

        return DateOnly.FromDateTime(dateTime.Value);
    }

    private static ParseError WrongFileType() =>
        new("detect", "File is not a Dell APOS quote.", "Could not read Dell APOS JSON structure.");

    private sealed record DellAposQuote
    {
        public long? QuoteNumber { get; init; }
        public int? QuoteVersion { get; init; }
        public string? Currency { get; init; }
        public decimal? SalesPrice { get; init; }
        public List<DellAposItem>? Items { get; init; }
    }

    private sealed record DellAposItem
    {
        public List<DellAposSku>? Skus { get; init; }
    }

    private sealed record DellAposSku
    {
        public string? SkuNumber { get; init; }
        public string? Description { get; init; }
        public string? LineOfBusiness { get; init; }
        public int Quantity { get; init; }
        public decimal? UnitSalesPrice { get; init; }
        public decimal? UnitListPrice { get; init; }
        public DellAposServiceTags? ServiceTags { get; init; }
    }

    private sealed record DellAposServiceTags
    {
        public string? ServiceTagNumber { get; init; }
        public DateTime? NewContractStartDate { get; init; }
        public DateTime? NewContractEndDate { get; init; }
    }
}
