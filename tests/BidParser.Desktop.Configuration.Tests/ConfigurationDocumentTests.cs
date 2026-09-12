using BidParser.Desktop.Configuration;
using BidParser.Domain.Constants;
using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Desktop.Configuration.Tests;

public sealed class ConfigurationDocumentTests
{
    private static readonly IReadOnlySet<string> KnownSlugs =
        new ParserRegistry().Parsers.Select(parser => parser.Slug).ToHashSet(StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> KnownVendors =
        new ParserRegistry().Parsers.Select(parser => parser.Vendor).ToHashSet(StringComparer.Ordinal);

    private static string Guidance(string entries) =>
        $$"""{"schemaVersion": 1, "guidanceMessages": [{{entries}}]}""";

    private static string Defaults(string entries) =>
        $$"""{"schemaVersion": 1, "vendorDefaults": [{{entries}}]}""";

    [Fact]
    public void The_bundled_documents_are_valid_and_cover_every_registered_parser()
    {
        var guidance = DesktopConfiguration.ReadBundledGuidance(KnownSlugs);
        var defaults = DesktopConfiguration.ReadBundledVendorDefaults(KnownVendors);

        guidance.Should().NotBeNull();
        guidance!.Count.Should().Be(KnownSlugs.Count);
        defaults.Should().NotBeNull();
        defaults!.For(Vendors.Strike)!.OnCostPercent.Should().Be(0.35m);
        // Prefill is on-cost only by design; the other three are always the user's call.
        defaults.For(Vendors.Strike)!.FxRate.Should().BeNull();
        defaults.For(Vendors.Nutanix).Should().BeNull();
    }

    [Fact]
    public void Bundled_guidance_renders_the_expected_shape()
    {
        var guidance = DesktopConfiguration.ReadBundledGuidance(KnownSlugs)!;

        guidance.For(ParserSlugs.StrikeQuotePdf)!.Blocks.Should().ContainSingle()
            .Which.Should().BeOfType<GuidanceParagraph>();
        guidance.For(ParserSlugs.HpBidXlsx)!.Blocks.Should().HaveCount(2);
        guidance.For(ParserSlugs.HpBidXlsx)!.Blocks[1]
            .Should().BeOfType<GuidanceBulletList>().Subject.Items.Should().HaveCount(3);
    }

    [Theory]
    [InlineData("""{"guidanceMessages": []}""")]
    [InlineData("""{"schemaVersion": "1", "guidanceMessages": []}""")]
    [InlineData("""{"schemaVersion": 1.0, "guidanceMessages": []}""")]
    [InlineData("""{"schemaVersion": 0, "guidanceMessages": []}""")]
    [InlineData("""{"schemaVersion": 2, "guidanceMessages": []}""")]
    [InlineData("""{"schemaVersion": true, "guidanceMessages": []}""")]
    [InlineData("""{"schemaVersion": null, "guidanceMessages": []}""")]
    [InlineData("""{"schemaVersion": 1}""")]
    [InlineData("""{"schemaVersion": 1, "guidanceMessages": {}}""")]
    [InlineData("[]")]
    [InlineData("not json at all")]
    public void An_unsupported_or_malformed_guidance_document_is_rejected_whole(string json)
        => GuidanceCatalogReader.TryRead(json, KnownSlugs).Should().BeNull();

    [Theory]
    [InlineData("""{"schemaVersion": 1, "vendorDefaults": []}""", true)]
    [InlineData("""{"schemaVersion": 2, "vendorDefaults": []}""", false)]
    [InlineData("""{"schemaVersion": 1}""", false)]
    [InlineData("""{"vendorDefaults": []}""", false)]
    public void Vendor_defaults_obey_the_same_schema_gate(string json, bool accepted)
        => (VendorDefaultsCatalogReader.TryRead(json, KnownVendors) is not null).Should().Be(accepted);

    [Fact]
    public void Unknown_properties_are_ignored_so_a_newer_document_stays_usable()
    {
        var json = """
            {"schemaVersion": 1, "publishedAt": "2026-09-10", "guidanceMessages": [
              {"fileTypes": ["strike_quote_pdf"], "html": "<p>ok</p>", "audience": "internal"}
            ]}
            """;

        GuidanceCatalogReader.TryRead(json, KnownSlugs)!.For(ParserSlugs.StrikeQuotePdf).Should().NotBeNull();
    }

    [Fact]
    public void References_this_build_does_not_recognise_are_skipped_not_rejected()
    {
        var guidance = GuidanceCatalogReader.TryRead(
            Guidance("""{"fileTypes": ["acme_quote_pdf", "strike_quote_pdf"], "html": "<p>ok</p>"}"""),
            KnownSlugs);
        var defaults = VendorDefaultsCatalogReader.TryRead(
            Defaults("""{"vendors": ["Acme", "Zebra"], "onCostPct": 1.50}"""),
            KnownVendors);

        guidance!.Count.Should().Be(1);
        guidance.For(ParserSlugs.StrikeQuotePdf).Should().NotBeNull();
        defaults!.Count.Should().Be(1);
        defaults.For(Vendors.Zebra)!.OnCostPercent.Should().Be(1.50m);
    }

    [Fact]
    public void Two_entries_claiming_one_known_parser_reject_the_document()
        => GuidanceCatalogReader.TryRead(
            Guidance("""
                {"fileTypes": ["strike_quote_pdf"], "html": "<p>first</p>"},
                {"fileTypes": ["strike_quote_pdf"], "html": "<p>second</p>"}
                """),
            KnownSlugs).Should().BeNull();

    [Fact]
    public void Two_entries_claiming_one_known_vendor_reject_the_document()
        => VendorDefaultsCatalogReader.TryRead(
            Defaults("""
                {"vendors": ["Zebra"], "onCostPct": 1.00},
                {"vendors": ["Zebra"], "onCostPct": 2.00}
                """),
            KnownVendors).Should().BeNull();

    [Theory]
    [InlineData("""{"fileTypes": ["strike_quote_pdf"]}""")]
    [InlineData("""{"html": "<p>ok</p>"}""")]
    [InlineData("""{"fileTypes": "strike_quote_pdf", "html": "<p>ok</p>"}""")]
    [InlineData("""{"fileTypes": [17], "html": "<p>ok</p>"}""")]
    [InlineData("""{"fileTypes": ["strike_quote_pdf"], "html": 17}""")]
    [InlineData("\"just a string\"")]
    public void A_malformed_guidance_entry_rejects_the_document(string entry)
        => GuidanceCatalogReader.TryRead(Guidance(entry), KnownSlugs).Should().BeNull();

    [Fact]
    public void Unsafe_markup_cannot_hide_behind_a_slug_this_build_does_not_know()
        => GuidanceCatalogReader.TryRead(
            Guidance("""{"fileTypes": ["acme_quote_pdf"], "html": "<script>alert(1)</script>"}"""),
            KnownSlugs).Should().BeNull();

    [Theory]
    [InlineData("""{"vendors": ["Zebra"], "onCostPct": -1}""")]
    [InlineData("""{"vendors": ["Zebra"], "onCostPct": "2.85"}""")]
    [InlineData("""{"vendors": ["Zebra"], "onCostPct": null}""")]
    [InlineData("""{"vendors": ["Zebra"], "fxRate": 0}""")]
    [InlineData("""{"vendors": ["Zebra"], "fxRate": -0.5}""")]
    [InlineData("""{"vendors": "Zebra", "onCostPct": 1}""")]
    [InlineData("""{"vendors": [17], "onCostPct": 1}""")]
    [InlineData("""{"onCostPct": 1}""")]
    public void An_invalid_default_rejects_the_document(string entry)
        => VendorDefaultsCatalogReader.TryRead(Defaults(entry), KnownVendors).Should().BeNull();

    [Theory]
    // A value finer than the field's scale would be silently rounded into a different number.
    [InlineData("""{"vendors": ["Zebra"], "onCostPct": 2.855}""", false)]
    [InlineData("""{"vendors": ["Zebra"], "onCostPct": 2.85}""", true)]
    [InlineData("""{"vendors": ["Zebra"], "fxRate": 1.23456}""", false)]
    [InlineData("""{"vendors": ["Zebra"], "fxRate": 1.2345}""", true)]
    public void Precision_finer_than_the_field_scale_rejects_the_document(string entry, bool accepted)
        => (VendorDefaultsCatalogReader.TryRead(Defaults(entry), KnownVendors) is not null).Should().Be(accepted);

    [Fact]
    public void Absent_numeric_fields_stay_blank_rather_than_defaulting_to_zero()
    {
        var defaults = VendorDefaultsCatalogReader.TryRead(
            Defaults("""{"vendors": ["Zebra"], "onCostPct": 2.85}"""), KnownVendors)!;

        var zebra = defaults.For(Vendors.Zebra)!;
        zebra.OnCostPercent.Should().Be(2.85m);
        zebra.FxRate.Should().BeNull();
        zebra.Margin.Should().BeNull();
        zebra.ImPercent.Should().BeNull();
    }

    [Fact]
    public void An_empty_entry_list_is_valid_and_simply_supplies_nothing()
    {
        GuidanceCatalogReader.TryRead(Guidance(string.Empty), KnownSlugs)!.Count.Should().Be(0);
        VendorDefaultsCatalogReader.TryRead(Defaults(string.Empty), KnownVendors)!.Count.Should().Be(0);
    }
}
