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
            c.LineSequence.Should().NotContain(".", "configs are top-level and must not have a dot");
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
    public void Parent_rows_have_zero_cost()
    {
        var result = ParseSample();

        var parents = result.LineItems
            .Where(i => !i.LineSequence!.Contains('.') && i.Cost == 0)
            .ToList();

        parents.Should().HaveCount(13, "there are 7 parents per config × 2 configs");
        parents.Should().AllSatisfy(p => p.Cost.Should().Be(0m));
    }

    [Fact]
    public void Children_have_dotted_sequence_and_zero_cost()
    {
        var result = ParseSample();

        var children = result.LineItems.Where(i => i.LineSequence!.Contains('.')).ToList();
        children.Should().HaveCount(137);
        children.Should().AllSatisfy(c =>
        {
            c.Cost.Should().Be(0m);
            c.LineSequence.Should().MatchRegex(@"^\d+\.\d{2}$");
        });
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

        // Parent 2 (7DG9CTO1WW for config 1) has 56 children after deduping the
        // redundant self-component row (the original PDF lists the server VPN itself
        // as the first component of its own configuration).
        var children = items.Where(i => i.LineSequence!.StartsWith("2.")).ToList();
        children.Should().HaveCount(56);
    }

    [Fact]
    public void Parent_vpn_is_never_repeated_as_one_of_its_children()
    {
        var items = ParseSample().LineItems;

        var parents = items.Where(i => !i.LineSequence!.Contains('.') && i.Cost == 0).ToList();
        foreach (var parent in parents)
        {
            var prefix = parent.LineSequence + ".";
            var childrenVpns = items
                .Where(i => i.LineSequence!.StartsWith(prefix))
                .Select(i => i.Vpn)
                .ToList();

            childrenVpns.Should().NotContain(parent.Vpn,
                $"parent {parent.LineSequence} ({parent.Vpn}) should not list itself as a child component");
        }
    }

    [Fact]
    public void Config1_xclarty_pro_parent_has_correct_children()
    {
        var items = ParseSample().LineItems;

        // XClarity Pro parent (5641PX3) in config 1 has 2 children after deduping the
        // redundant self-component row.
        var parent = items.Single(i => i.LineSequence == "3");
        parent.Vpn.Should().Be("5641PX3");
        parent.Qty.Should().Be(8);

        var children = items.Where(i => i.LineSequence!.StartsWith("3.")).ToList();
        children.Should().HaveCount(2);
        children.Select(c => c.Vpn).Should().NotContain("5641PX3");
    }

    // ── Config 2 spot checks ─────────────────────────────────────────────────

    [Fact]
    public void Config2_main_server_has_expected_qty_and_child_count()
    {
        var items = ParseSample().LineItems;

        var parent = items.Single(i => i.LineSequence == "9");
        parent.Vpn.Should().Be("7DG9CTO1WW");
        parent.Qty.Should().Be(2);

        var children = items.Where(i => i.LineSequence!.StartsWith("9.")).ToList();
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
