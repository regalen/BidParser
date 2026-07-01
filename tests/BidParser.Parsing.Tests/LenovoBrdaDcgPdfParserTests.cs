using BidParser.Parsing.Lenovo.BrdaDcgPdf;
using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class LenovoBrdaDcgPdfParserTests
{
    // ── Detection ────────────────────────────────────────────────────────────

    [Fact]
    public void Detect_returns_high_confidence_for_brda_dcg_pdf()
    {
        var root = FindRepoRoot();
        var parser = new LenovoBrdaDcgPdfParser();

        var score = parser.Detect(Path.Combine(root, "samples", "inputs", "BRDAS010260417V1.pdf"));

        score.Should().BeGreaterThanOrEqualTo(0.8);
    }

    // ── Metadata ─────────────────────────────────────────────────────────────

    [Fact]
    public void Extracts_quote_number_supplier_and_currency()
    {
        var result = ParseSample();

        result.Metadata.QuoteNumber.Should().Be("BRDAS010260417V1");
        result.Metadata.Supplier.Should().Be("Lenovo");
        result.Metadata.Currency.Should().Be("AUD");
    }

    // ── Totals ───────────────────────────────────────────────────────────────

    [Fact]
    public void Quoted_total_matches_computed_total()
    {
        var result = ParseSample();

        result.Metadata.QuotedTotal.Should().Be(393_231.78m);
        result.Validation.ComputedTotal.Should().Be(393_231.78m);
        result.Validation.Matches.Should().BeTrue();
    }

    // ── Structure ────────────────────────────────────────────────────────────

    [Fact]
    public void Produces_correct_item_count()
    {
        var result = ParseSample();

        // 2 CONFIGs + 13 PARENTs + 137 children (parent-VPN self-components deduped)
        result.LineItems.Should().HaveCount(152);
    }

    [Fact]
    public void Config_rows_have_positive_cost_and_top_level_sequence()
    {
        var result = ParseSample();

        var configs = result.LineItems.Where(i => i.Cost > 0).ToList();
        configs.Should().HaveCount(2);
        configs.Should().AllSatisfy(c =>
        {
            // Flat numbering: every line (configs included) is a plain integer, never dotted.
            c.LineSequence.Should().NotContain(".");
            c.Cost.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void Config_costs_sum_to_quoted_total()
    {
        var result = ParseSample();

        var configTotal = result.LineItems
            .Where(i => i.Cost > 0)
            .Sum(i => i.Cost * i.Qty);

        configTotal.Should().Be(result.Metadata.QuotedTotal);
    }

    [Fact]
    public void NonConfig_rows_have_zero_cost()
    {
        var result = ParseSample();

        // Everything that is not a priced config (13 parents + 137 children = 150) carries the
        // dropped 0 cost. Flat numbering no longer distinguishes parent from child in the
        // sequence, so they are counted together here by cost.
        var nonConfig = result.LineItems.Where(i => i.Cost == 0m).ToList();

        nonConfig.Should().HaveCount(150);
        nonConfig.Should().AllSatisfy(p => p.Cost.Should().Be(0m));
    }

    [Fact]
    public void All_sequences_are_flat_1_to_N()
    {
        var result = ParseSample();

        // Single running sequence 1..152; no line uses the old dotted "parent.NN" form.
        result.LineItems
            .Select(i => i.LineSequence)
            .Should()
            .Equal(Enumerable.Range(1, 152).Select(n => n.ToString()));
    }

    // ── Config 1 spot checks ─────────────────────────────────────────────────

    [Fact]
    public void First_config_has_correct_vpn_and_cost()
    {
        var items = ParseSample().LineItems;

        var cfg1 = items.First();
        cfg1.LineSequence.Should().Be("1");
        cfg1.Vpn.Should().Be("SIDX02Q2PL");
        cfg1.Cost.Should().Be(356_882.96m);
        cfg1.Qty.Should().Be(1);
    }

    [Fact]
    public void Second_config_has_correct_vpn_and_cost()
    {
        var items = ParseSample().LineItems;

        var cfg2 = items.Single(i => i.Vpn == "SIDX02Q2PM");
        cfg2.Cost.Should().Be(36_348.82m);
        cfg2.Qty.Should().Be(1);
    }

    [Fact]
    public void First_parent_of_config1_has_correct_vpn_qty_and_description()
    {
        var items = ParseSample().LineItems;

        var parent = items.Single(i => i.LineSequence == "2");
        parent.Vpn.Should().Be("7DG9CTO1WW");
        parent.Qty.Should().Be(8);
        parent.Cost.Should().Be(0m);
        parent.Description.Should().NotBeNullOrWhiteSpace();
        parent.Description.Should().Contain("ThinkSystem SR630");
    }

    // ── Config 1 children spot checks ────────────────────────────────────────

    [Fact]
    public void Config1_main_server_has_expected_child_count()
    {
        var items = ParseSample().LineItems;

        // Parent at line 2 (7DG9CTO1WW for config 1) is followed by 56 children (lines 3..58)
        // after deduping the redundant self-component row (the original PDF lists the server
        // VPN itself as the first component of its own configuration).
        var children = items.Where(i =>
        {
            var n = int.Parse(i.LineSequence!);
            return n >= 3 && n <= 58;
        }).ToList();
        children.Should().HaveCount(56);
        children.Should().OnlyContain(c => c.Cost == 0m);
        children.Select(c => c.Vpn).Should().NotContain("7DG9CTO1WW");
    }

    [Fact]
    public void Parent_vpn_is_never_repeated_as_one_of_its_children()
    {
        var items = ParseSample().LineItems;

        // The self-component dedup means a server/parent VPN appears exactly once per config
        // (as the parent) and never again inside its own component breakdown. Both configs
        // list the same two servers, so each appears exactly twice across the whole quote.
        items.Count(i => i.Vpn == "7DG9CTO1WW").Should().Be(2);
        items.Count(i => i.Vpn == "5641PX3").Should().Be(2);
    }

    [Fact]
    public void Config1_xclarty_pro_parent_has_correct_children()
    {
        var items = ParseSample().LineItems;

        // XClarity Pro parent (5641PX3) in config 1 sits at line 59 and is followed by 2
        // children (lines 60..61) after deduping the redundant self-component row.
        var parent = items.Single(i => i.LineSequence == "59");
        parent.Vpn.Should().Be("5641PX3");
        parent.Qty.Should().Be(8);

        var children = items.Where(i =>
        {
            var n = int.Parse(i.LineSequence!);
            return n >= 60 && n <= 61;
        }).ToList();
        children.Should().HaveCount(2);
        children.Select(c => c.Vpn).Should().NotContain("5641PX3");
    }

    // ── Config 2 spot checks ─────────────────────────────────────────────────

    [Fact]
    public void Config2_main_server_has_expected_qty_and_child_count()
    {
        var items = ParseSample().LineItems;

        // Config 2's main server (7DG9CTO1WW, qty 2) sits at line 77, followed by its 56
        // children (lines 78..133).
        var parent = items.Single(i => i.LineSequence == "77");
        parent.Vpn.Should().Be("7DG9CTO1WW");
        parent.Qty.Should().Be(2);

        var children = items.Where(i =>
        {
            var n = int.Parse(i.LineSequence!);
            return n >= 78 && n <= 133;
        }).ToList();
        children.Should().HaveCount(56);
    }

    // ── Parser registry ──────────────────────────────────────────────────────

    [Fact]
    public void Parser_is_registered_in_registry()
    {
        var registry = new ParserRegistry();
        registry.Parsers.Should().Contain(p => p.Slug == "lenovo_brda_dcg_pdf");
    }

    [Fact]
    public void Parser_metadata_is_correct()
    {
        var parser = new LenovoBrdaDcgPdfParser();

        parser.Slug.Should().Be("lenovo_brda_dcg_pdf");
        parser.Vendor.Should().Be("Lenovo");
        parser.AcceptedMime.Should().Be("application/pdf");
        parser.AvailableTemplates.Should().Equal("No Calculation", "Uplift");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static BidParser.Domain.Models.ParseResult? _cachedResult;
    private static readonly object _lock = new();

    private static BidParser.Domain.Models.ParseResult ParseSample()
    {
        lock (_lock)
        {
            if (_cachedResult is not null)
            {
                return _cachedResult;
            }

            var root = FindRepoRoot();
            var parser = new LenovoBrdaDcgPdfParser();
            _cachedResult = parser.Parse(Path.Combine(root, "samples", "inputs", "BRDAS010260417V1.pdf"));
            return _cachedResult;
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BidParser.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}

/// <summary>
/// Covers BRDA DCG PDFs that have only a PRODUCT AND SERVICE DETAILS section
/// (no CONFIGURATION DETAILS). All extracted rows are top-level parent items.
/// </summary>
public sealed class LenovoBrdaDcgPdfSimpleVariantTests
{
    // ── Detection ────────────────────────────────────────────────────────────

    [Fact]
    public void Detect_returns_sufficient_confidence_without_configuration_details_section()
    {
        var root = FindRepoRoot();
        var parser = new LenovoBrdaDcgPdfParser();

        var score = parser.Detect(Path.Combine(root, "samples", "inputs", "BRDAS010545504V1.pdf"));

        score.Should().BeGreaterThanOrEqualTo(0.7);
    }

    // ── Three-item quote (BRDAS010545504V1) ──────────────────────────────────

    [Fact]
    public void ThreeItemQuote_extracts_correct_quote_number()
    {
        ParseThreeItem().Metadata.QuoteNumber.Should().Be("BRDAS010545504V1");
    }

    [Fact]
    public void ThreeItemQuote_produces_three_top_level_items()
    {
        var items = ParseThreeItem().LineItems;
        items.Should().HaveCount(3);
        items.Should().AllSatisfy(i => i.LineSequence.Should().NotContain("."));
    }

    [Fact]
    public void ThreeItemQuote_validation_matches()
    {
        var result = ParseThreeItem();
        result.Metadata.QuotedTotal.Should().Be(77_545.95m);
        result.Validation.Matches.Should().BeTrue();
        result.Validation.ComputedTotal.Should().Be(77_545.95m);
    }

    [Fact]
    public void ThreeItemQuote_first_item_has_correct_fields()
    {
        var item = ParseThreeItem().LineItems[0];
        item.LineSequence.Should().Be("1");
        item.Vpn.Should().Be("4XB7A93897");
        item.Qty.Should().Be(3);
        item.Cost.Should().Be(5_340.85m);
        item.Description.Should().Contain("ThinkSystem");
    }

    [Fact]
    public void ThreeItemQuote_all_items_have_positive_cost()
    {
        ParseThreeItem().LineItems.Should().AllSatisfy(i => i.Cost.Should().BePositive());
    }

    // ── Single-item quote (BRDAS010546096V1) ─────────────────────────────────

    [Fact]
    public void SingleItemQuote_extracts_correct_quote_number()
    {
        ParseSingleItem().Metadata.QuoteNumber.Should().Be("BRDAS010546096V1");
    }

    [Fact]
    public void SingleItemQuote_produces_one_top_level_item()
    {
        var items = ParseSingleItem().LineItems;
        items.Should().HaveCount(1);
        items[0].LineSequence.Should().Be("1");
    }

    [Fact]
    public void SingleItemQuote_validation_matches()
    {
        var result = ParseSingleItem();
        result.Metadata.QuotedTotal.Should().Be(38_896.08m);
        result.Validation.Matches.Should().BeTrue();
        result.Validation.ComputedTotal.Should().Be(38_896.08m);
    }

    [Fact]
    public void SingleItemQuote_item_has_correct_fields()
    {
        var item = ParseSingleItem().LineItems[0];
        item.Vpn.Should().Be("4X77B09750");
        item.Qty.Should().Be(12);
        item.Cost.Should().Be(3_241.34m);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static BidParser.Domain.Models.ParseResult? _threeItem;
    private static BidParser.Domain.Models.ParseResult? _singleItem;
    private static readonly object _lock = new();

    private static BidParser.Domain.Models.ParseResult ParseThreeItem()
    {
        lock (_lock)
        {
            if (_threeItem is not null) return _threeItem;
            var parser = new LenovoBrdaDcgPdfParser();
            _threeItem = parser.Parse(Path.Combine(FindRepoRoot(), "samples", "inputs", "BRDAS010545504V1.pdf"));
            return _threeItem;
        }
    }

    private static BidParser.Domain.Models.ParseResult ParseSingleItem()
    {
        lock (_lock)
        {
            if (_singleItem is not null) return _singleItem;
            var parser = new LenovoBrdaDcgPdfParser();
            _singleItem = parser.Parse(Path.Combine(FindRepoRoot(), "samples", "inputs", "BRDAS010546096V1.pdf"));
            return _singleItem;
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BidParser.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
