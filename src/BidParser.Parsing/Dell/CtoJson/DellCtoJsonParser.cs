using System.Globalization;
using System.Text.Json;
using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;

namespace BidParser.Parsing.Dell.CtoJson;

/// <summary>
/// Parses Dell "CTO" configuration quotes (JSON). Each item is a base SKU (parent line) with child
/// SKUs; the presence of a non-empty service-tag number means the file is actually an APOS export,
/// so those are rejected as a wrong file type. Uses the "No Calculation" output. See
/// docs/dell_cto_json.md.
/// </summary>
public sealed class DellCtoJsonParser : IParser
{
    private const string RebateIneligibleComment = "Not Dell Rebate Eligible";

    public string Slug => ParserSlugs.DellCtoJson;
    public string DisplayName => "CTO (API)";
    public string Vendor => Vendors.Dell;
    public string AcceptedMime => "application/json";
    public string CrmTemplate => CrmTemplates.NoCalculation;
    public bool SupportsSubComponentDetail => true;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ParseResult Parse(string path) => Parse(path, new ParseOptions());

    public ParseResult Parse(string path, ParseOptions options)
    {
        var content = File.ReadAllText(path);

        DellQuote? quote;
        try
        {
            quote = JsonSerializer.Deserialize<DellQuote>(content, Options);
        }
        catch (JsonException)
        {
            throw WrongFileType();
        }

        if (quote?.Items is not { Count: > 0 })
        {
            throw WrongFileType();
        }

        if (quote.Items.Any(i => i.Skus?.Any(s => HasServiceTag(s.ServiceTags?.ServiceTagNumber)) == true))
        {
            throw WrongFileType();
        }

        var items = new List<LineItem>();
        var parentSeq = 0;

        foreach (var it in quote.Items)
        {
            if (string.IsNullOrEmpty(it.BaseSkuNumber) || it.Skus is null)
            {
                throw WrongFileType();
            }

            parentSeq++;
            items.Add(new LineItem
            {
                LineSequence = parentSeq.ToString(),
                Vpn = it.BaseSkuNumber,
                Description = string.IsNullOrWhiteSpace(it.CustomProductName)
                    ? it.ProductDescription
                    : it.CustomProductName,
                Qty = it.Quantity,
                Msrp = it.UnitListPriceIncludingShipping,
                Cost = it.UnitSalesPriceIncludingShipping ?? 0m,
                Comments = ParentComments(it),
            });

            if (!options.IncludeSubComponentDetail
                && string.Equals(it.LineOfBusiness, "Displays", StringComparison.Ordinal))
            {
                continue;
            }

            var child = 0;
            foreach (var sku in it.Skus)
            {
                if (!options.IncludeSubComponentDetail && MatchesParentAttributes(it, sku))
                {
                    continue;
                }

                child++;
                items.Add(new LineItem
                {
                    LineSequence = $"{parentSeq}.{child:D2}",
                    Vpn = sku.SkuNumber ?? string.Empty,
                    Description = sku.Description,
                    Qty = sku.Quantity,
                    Msrp = 0m,
                    Cost = 0m,
                });
            }
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
            HasRebateIneligibleItems = quote.Items.Any(i => i.IsRebateEligible is false),
        };
    }

    public double Detect(string path)
    {
        try
        {
            var content = File.ReadAllText(path);
            var quote = JsonSerializer.Deserialize<DellQuote>(content, Options);
            if (quote?.Items is not { Count: > 0 })
            {
                return 0.0;
            }

            var allSkus = quote.Items.SelectMany(i => i.Skus ?? []);
            if (!allSkus.Any())
            {
                return 0.0;
            }

            var hasServiceTag = quote.Items.Any(i => i.Skus?.Any(s => HasServiceTag(s.ServiceTags?.ServiceTagNumber)) == true);
            return hasServiceTag ? 0.0 : 0.9;
        }
        catch
        {
            return 0.0;
        }
    }

    private static bool HasServiceTag(string? serviceTagNumber) =>
        !string.IsNullOrWhiteSpace(serviceTagNumber);

    // A child that restates its parent — same SKU number, same list price, same sale price — carries
    // no information the parent line does not already carry. Prices are compared like for like; the
    // null guards exist so an absent source field cannot default to 0m and fake a match.
    private static bool MatchesParentAttributes(DellItem item, DellSku sku) =>
        item.BaseSkuNumber is not null
        && sku.SkuNumber is not null
        && item.UnitListPriceIncludingShipping is not null
        && item.UnitSalesPriceIncludingShipping is not null
        && sku.UnitListPrice is not null
        && sku.UnitSalesPrice is not null
        && string.Equals(item.BaseSkuNumber, sku.SkuNumber, StringComparison.Ordinal)
        && item.UnitListPriceIncludingShipping == sku.UnitListPrice
        && item.UnitSalesPriceIncludingShipping == sku.UnitSalesPrice;

    // Estimated delivery date = the latest estimatedDeliveryDateRange.max across all of the
    // item's shipments. Written to the parent line's Comments (Col R). MinValue (0001-01-01,
    // i.e. an unset date) is ignored; when no real date is present, no comment is written.
    private static string? DeliveryComment(List<DellShipment>? shipments)
    {
        if (shipments is null)
        {
            return null;
        }

        DateTimeOffset? latest = null;
        foreach (var shipment in shipments)
        {
            var max = shipment.EstimatedDeliveryDateRange?.Max;
            if (max is null || max.Value.Year <= 1)
            {
                continue;
            }

            if (latest is null || max.Value > latest.Value)
            {
                latest = max.Value;
            }
        }

        if (latest is null)
        {
            return null;
        }

        var date = DateOnly.FromDateTime(latest.Value.DateTime);
        return $"Est. delivery on {date.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture)} if purchased today";
    }

    private static string? ParentComments(DellItem item)
    {
        var delivery = DeliveryComment(item.Shipments);
        if (item.IsRebateEligible is not false)
        {
            return delivery;
        }

        return delivery is null ? RebateIneligibleComment : $"{delivery} | {RebateIneligibleComment}";
    }

    private static ParseError WrongFileType() =>
        new("detect", "File is not a Dell CTO quote.", "Could not read Dell CTO JSON structure.");

    private sealed record DellQuote
    {
        public long? QuoteNumber { get; init; }
        public int? QuoteVersion { get; init; }
        public string? Currency { get; init; }
        public decimal? SalesPrice { get; init; }
        public List<DellItem>? Items { get; init; }
    }

    private sealed record DellItem
    {
        public string? BaseSkuNumber { get; init; }
        public string? LineOfBusiness { get; init; }
        public string? CustomProductName { get; init; }
        public string? ProductDescription { get; init; }
        public bool? IsRebateEligible { get; init; }
        public int Quantity { get; init; }
        public decimal? UnitListPriceIncludingShipping { get; init; }
        public decimal? UnitSalesPriceIncludingShipping { get; init; }
        public List<DellSku>? Skus { get; init; }
        public List<DellShipment>? Shipments { get; init; }
    }

    private sealed record DellShipment
    {
        public DellDateRange? EstimatedDeliveryDateRange { get; init; }
    }

    private sealed record DellDateRange
    {
        public DateTimeOffset? Max { get; init; }
    }

    private sealed record DellSku
    {
        public string? SkuNumber { get; init; }
        public string? Description { get; init; }
        public int Quantity { get; init; }
        public decimal? UnitListPrice { get; init; }
        public decimal? UnitSalesPrice { get; init; }
        public DellServiceTags? ServiceTags { get; init; }
    }

    private sealed record DellServiceTags
    {
        public string? ServiceTagNumber { get; init; }
    }
}
