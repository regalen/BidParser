using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Output;
using BidParser.Parsing.Registry;
using ClosedXML.Excel;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class TemplateWriterTests
{
    [Theory]
    [InlineData("nutanix_software_only_pdf", "XQ-9100002.pdf", "XQ-9100002_ForeignUplift.xlsx")]
    [InlineData("nutanix_software_only_pdf", "XQ-9100005.pdf", "XQ-9100005_ForeignUplift.xlsx")]
    [InlineData("nutanix_software_only_pdf", "XQ-9100006.pdf", "XQ-9100006_ForeignUplift.xlsx")]
    [InlineData("nutanix_software_only_pdf", "XQ-9100012.pdf", "XQ-9100012_ForeignUplift.xlsx")]
    [InlineData("nutanix_software_only_xlsx", "XQ-9100002.xlsx", "XQ-9100002_ForeignUplift.xlsx")]
    [InlineData("nutanix_hardware_only_pdf", "XQ-9100003.pdf", "XQ-9100003_ForeignUplift.xlsx")]
    [InlineData("nutanix_hardware_only_xlsx", "XQ-9100003.xlsx", "XQ-9100003_ForeignUplift.xlsx")]
    [InlineData("nutanix_renewal_pdf", "XQ-9100004.pdf", "XQ-9100004_ForeignUplift.xlsx")]
    [InlineData("nutanix_renewal_pdf", "XQ-9100001.pdf", "XQ-9100001_ForeignUplift.xlsx")]
    [InlineData("nutanix_renewal_xlsx", "XQ-9100010.xlsx", "XQ-9100010_ForeignUplift.xlsx")]
    public void TemplateWriterMatchesGoldenWorkbookCells(string slug, string inputName, string expectedName)
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(parser => parser.Slug == slug);
        var result = parser.Parse(Path.Combine(root, "samples", "inputs", inputName));
        using var tempDirectory = new TempDirectory();
        var actualPath = Path.Combine(tempDirectory.Path, expectedName);

        CrmWriter.Write(
            result.LineItems, actualPath, CrmTemplates.ForeignUplift,
            new CrmWriterOptions(VendorName: "NUTANIX", FxRate: 1.000m, Margin: 5.00m, TermAsComment: parser.TermRendersAsComment));

        WorkbookComparer.AssertEqual(actualPath, Path.Combine(root, "samples", "outputs", expectedName));
    }

    // ── CrmWriter (HP No Calculation / Uplift) ────────────────────────

    [Theory]
    [InlineData("Deals_Sample_01_HPI.xlsx", "Deals_Sample_01_HPI_NoCalculation.xlsx", false)]
    [InlineData("Deals_Sample_01_HPI.xlsx", "Deals_Sample_01_HPI_Uplift.xlsx", true)]
    [InlineData("Deals_Sample_02_HPI.xlsx", "Deals_Sample_02_HPI_NoCalculation.xlsx", false)]
    [InlineData("Deals_Sample_02_HPI.xlsx", "Deals_Sample_02_HPI_Uplift.xlsx", true)]
    public void AnzGenericWriterMatchesGoldenWorkbookCells(string inputName, string expectedName, bool includeMargin)
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpBidXlsx);
        var result = parser.Parse(Path.Combine(root, "samples", "inputs", inputName));
        using var tempDirectory = new TempDirectory();
        var actualPath = Path.Combine(tempDirectory.Path, expectedName);

        var crmTemplate = includeMargin ? CrmTemplates.Uplift : CrmTemplates.NoCalculation;
        CrmWriter.Write(result.LineItems, actualPath, crmTemplate, new CrmWriterOptions(VendorName: "HP", Margin: 5.00m));

        WorkbookComparer.AssertEqual(actualPath, Path.Combine(root, "samples", "outputs", expectedName));
    }

    // ── CrmWriter (HP Services No Calculation / Uplift) ───────────────

    [Theory]
    [InlineData("CH9000000001.xlsx", "CH9000000001_NoCalculation.xlsx", false)]
    [InlineData("CH9000000001.xlsx", "CH9000000001_Uplift.xlsx", true)]
    [InlineData("CH9000000002.xlsx", "CH9000000002_NoCalculation.xlsx", false)]
    public void AnzGenericWriterMatchesGoldenWorkbookCells_HpServices(string inputName, string expectedName, bool includeMargin)
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpServicesXlsx);
        var result = parser.Parse(Path.Combine(root, "samples", "inputs", inputName));
        using var tempDirectory = new TempDirectory();
        var actualPath = Path.Combine(tempDirectory.Path, expectedName);

        var crmTemplate = includeMargin ? CrmTemplates.Uplift : CrmTemplates.NoCalculation;
        CrmWriter.Write(result.LineItems, actualPath, crmTemplate, new CrmWriterOptions(VendorName: "HP", Margin: 5.00m));

        WorkbookComparer.AssertEqual(actualPath, Path.Combine(root, "samples", "outputs", expectedName));
    }

    [Fact]
    public void AnzGenericWriterMatchesGoldenWorkbookCells_HpServices_Split()
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpServicesXlsx);
        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "CH9000000002.xlsx"));
        var groups = SolutionOutputSplitter.Split(result.LineItems);
        var group = groups.Single(g => g.SolutionId == "1073 3013 5309");

        using var tempDirectory = new TempDirectory();
        const string expectedName = "1073_3013_5309_NoCalculation.xlsx";
        var actualPath = Path.Combine(tempDirectory.Path, expectedName);

        CrmWriter.Write(group.Items, actualPath, CrmTemplates.NoCalculation, new CrmWriterOptions(VendorName: "HP"));

        WorkbookComparer.AssertEqual(actualPath, Path.Combine(root, "samples", "outputs", expectedName));
    }

    // ── CrmWriter (HPE No Calculation / Uplift) ───────────────────────

    [Theory]
    [InlineData("HPE_Deal_9500000001_v2.xlsx", "HPE_Deal_9500000001_v2_NoCalculation.xlsx", false)]
    [InlineData("HPE_Deal_9500000001_v2.xlsx", "HPE_Deal_9500000001_v2_Uplift.xlsx", true)]
    [InlineData("HPE_Deal_9500000002_v1.xlsx", "HPE_Deal_9500000002_v1_NoCalculation.xlsx", false)]
    [InlineData("HPE_Deal_9500000002_v1.xlsx", "HPE_Deal_9500000002_v1_Uplift.xlsx", true)]
    public void AnzGenericWriterMatchesGoldenWorkbookCells_Hpe(string inputName, string expectedName, bool includeMargin)
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpeBidXlsx);
        var result = parser.Parse(Path.Combine(root, "samples", "inputs", inputName));
        using var tempDirectory = new TempDirectory();
        var actualPath = Path.Combine(tempDirectory.Path, expectedName);

        var crmTemplate = includeMargin ? CrmTemplates.Uplift : CrmTemplates.NoCalculation;
        CrmWriter.Write(result.LineItems, actualPath, crmTemplate, new CrmWriterOptions(VendorName: "HPE", Margin: 5.00m));

        WorkbookComparer.AssertEqual(actualPath, Path.Combine(root, "samples", "outputs", expectedName));
    }

    // ── CrmWriter (Zebra Price Concession PDF) ───────────────────────────────

    [Fact]
    public void AnzGenericWriterMatchesGoldenWorkbookCells_Zebra()
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.ZebraPcrPdf);
        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "Zebra_PC_97000004_V1.0.pdf"));
        using var tempDirectory = new TempDirectory();
        const string expectedName = "Zebra_PC_97000004_V1.0_NoCalculation.xlsx";
        var actualPath = Path.Combine(tempDirectory.Path, expectedName);

        CrmWriter.Write(result.LineItems, actualPath, CrmTemplates.NoCalculation, new CrmWriterOptions(VendorName: "ZEBRA", OnCost: 2.85m));

        WorkbookComparer.AssertEqual(actualPath, Path.Combine(root, "samples", "outputs", expectedName));
    }

    [Theory]
    [InlineData(ParserSlugs.DatalogicQuotePdf, "Datalogic_PE930003.pdf", "Datalogic_PE930003_NoCalculation.xlsx", CrmTemplates.NoCalculation)]
    [InlineData(ParserSlugs.DatalogicQuotePdf, "Datalogic_PE930003.pdf", "Datalogic_PE930003_Uplift.xlsx", CrmTemplates.Uplift)]
    [InlineData(ParserSlugs.EpsonQuotePdf, "Epson_96000002.pdf", "Epson_96000002_NoCalculation.xlsx", CrmTemplates.NoCalculation)]
    [InlineData(ParserSlugs.StrikeQuotePdf, "Strike_Quote_9202.pdf", "Strike_Quote_9202_NoCalculation.xlsx", CrmTemplates.NoCalculation)]
    public void AnzGenericWriterMatchesGoldenWorkbookCells_NewPdfVendors(
        string slug, string inputName, string expectedName, string template)
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == slug);
        var result = parser.Parse(Path.Combine(root, "samples", "inputs", inputName));
        using var tempDirectory = new TempDirectory();
        var actualPath = Path.Combine(tempDirectory.Path, expectedName);
        CrmWriter.Write(result.LineItems, actualPath, template,
            new CrmWriterOptions(parser.OutputVendorName.ToUpperInvariant(), Margin: 5m, OnCost: 2.85m));

        WorkbookComparer.AssertEqual(actualPath, Path.Combine(root, "samples", "outputs", expectedName));
    }

    [Theory]
    [InlineData("Trellix_Quote_900001.pdf", "Trellix_Quote_900001_NoCalculation.xlsx", CrmTemplates.NoCalculation)]
    [InlineData("Trellix_Quote_900001.pdf", "Trellix_Quote_900001_Uplift.xlsx", CrmTemplates.Uplift)]
    [InlineData("Trellix_Quote_900002.pdf", "Trellix_Quote_900002_NoCalculation.xlsx", CrmTemplates.NoCalculation)]
    public void Trellix_writer_matches_synthetic_golden_and_standard_columns(
        string inputName, string expectedName, string template)
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(candidate => candidate.Slug == ParserSlugs.TrellixQuotePdf);
        var result = parser.Parse(TestSample.Path(inputName));
        using var tempDirectory = new TempDirectory();
        var actualPath = Path.Combine(tempDirectory.Path, expectedName);
        CrmWriter.Write(result.LineItems, actualPath, template,
            new CrmWriterOptions(parser.OutputVendorName.ToUpperInvariant(), Margin: 5m));

        WorkbookComparer.AssertEqual(actualPath, Path.Combine(root, "samples", "outputs", expectedName));
        using var workbook = new XLWorkbook(actualPath);
        var sheet = workbook.Worksheet(template);
        var first = result.LineItems[0];
        sheet.Cell(3, 1).GetString().Should().Be("1");
        sheet.Cell(3, 2).GetString().Should().Be("TRELLIX");
        sheet.Cell(3, 4).GetString().Should().Be(first.Vpn);
        sheet.Cell(3, 5).GetString().Should().Be(first.Description);
        sheet.Cell(3, 6).GetValue<int>().Should().Be(first.Qty);
        sheet.Cell(3, 8).GetValue<decimal>().Should().Be(first.Msrp);
        sheet.Cell(3, 9).GetValue<decimal>().Should().Be(first.Cost);
        sheet.Cell(3, 13).GetString().Should().Be(first.SerialNumber ?? string.Empty);
        sheet.Cell(3, 16).DataType.Should().Be(XLDataType.DateTime);
        sheet.Cell(3, 16).GetDateTime().Date.Should().Be(first.StartDate!.Value.ToDateTime(TimeOnly.MinValue));
        sheet.Cell(3, 18).GetString().Should().Be(first.Comments);
        sheet.Cell(3, 26).IsEmpty().Should().BeTrue();
        sheet.Cell(3, 11).IsEmpty().Should().Be(template == CrmTemplates.NoCalculation);

        if (inputName == "Trellix_Quote_900001.pdf")
        {
            sheet.Cell(3, 17).DataType.Should().Be(XLDataType.DateTime);
            sheet.Cell(11, 8).GetValue<decimal>().Should().Be(0.0001m);
            sheet.Cell(11, 9).GetValue<decimal>().Should().Be(0.0001m);
        }
        else
        {
            sheet.Cell(3, 17).IsEmpty().Should().BeTrue();
            sheet.Cell(3, 13).GetString().Should().Be("SN-02");
        }
    }

    // ── CrmWriter (Lenovo LBP-E ISG XLS/XLSX, both templates) ────────

    [Theory]
    [InlineData("BRDAD019200001.xls", "BRDAD019200001_NoCalculation.xlsx", false)]
    [InlineData("BRDAD019200001.xls", "BRDAD019200001_Uplift.xlsx", true)]
    [InlineData("Bid_Platform_Bid_Request_Sample_03.xls", "Bid_Platform_Bid_Request_Sample_03_NoCalculation.xlsx", false)]
    [InlineData("Bid_Platform_Bid_Request_Sample_04.xlsx", "Bid_Platform_Bid_Request_Sample_04_NoCalculation.xlsx", false)]
    [InlineData("Bid_Platform_Bid_Request_Sample_04.xlsx", "Bid_Platform_Bid_Request_Sample_04_Uplift.xlsx", true)]
    public void AnzGenericWriterMatchesGoldenWorkbookCells_Lenovo(
        string inputName,
        string expectedName,
        bool includeMargin)
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.LenovoLbpeIsgXls);
        var result = parser.Parse(Path.Combine(root, "samples", "inputs", inputName));
        using var tempDirectory = new TempDirectory();
        var actualPath = Path.Combine(tempDirectory.Path, expectedName);

        var crmTemplate = includeMargin ? CrmTemplates.Uplift : CrmTemplates.NoCalculation;
        CrmWriter.Write(
            result.LineItems,
            actualPath,
            crmTemplate,
            new CrmWriterOptions(VendorName: Vendors.LenovoOutput.ToUpperInvariant(), Margin: 5.00m));

        WorkbookComparer.AssertEqual(actualPath, Path.Combine(root, "samples", "outputs", expectedName));
    }

    // ── CrmWriter (Lenovo LBP-I ISG PDF, both templates) ──────────────

    [Theory]
    [InlineData("BRDAS019000004V1.pdf", "BRDAS019000004V1_NoCalculation.xlsx", false)]
    [InlineData("BRDAS019000004V1.pdf", "BRDAS019000004V1_Uplift.xlsx", true)]
    [InlineData("BRDAS019000003V1.pdf", "BRDAS019000003V1_NoCalculation.xlsx", false)]
    public void AnzGenericWriterMatchesGoldenWorkbookCells_LenovoLbpi(
        string inputName,
        string expectedName,
        bool includeMargin)
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.LenovoLbpiIsgPdf);
        var result = parser.Parse(Path.Combine(root, "samples", "inputs", inputName));
        using var tempDirectory = new TempDirectory();
        var actualPath = Path.Combine(tempDirectory.Path, expectedName);

        var crmTemplate = includeMargin ? CrmTemplates.Uplift : CrmTemplates.NoCalculation;
        CrmWriter.Write(
            result.LineItems,
            actualPath,
            crmTemplate,
            new CrmWriterOptions(VendorName: Vendors.LenovoOutput.ToUpperInvariant(), Margin: 5.00m));

        WorkbookComparer.AssertEqual(actualPath, Path.Combine(root, "samples", "outputs", expectedName));
    }

    // ── CrmWriter (Lenovo LBP-I IDG PDF, both templates) ──────────────

    [Theory]
    [InlineData("BRPAS019100001V1.pdf", "BRPAS019100001V1_NoCalculation.xlsx", false)]
    [InlineData("BRPAS019100001V1.pdf", "BRPAS019100001V1_Uplift.xlsx", true)]
    public void AnzGenericWriterMatchesGoldenWorkbookCells_LenovoLbpiIdg(
        string inputName,
        string expectedName,
        bool includeMargin)
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.LenovoLbpiIdgPdf);
        var result = parser.Parse(Path.Combine(root, "samples", "inputs", inputName));
        using var tempDirectory = new TempDirectory();
        var actualPath = Path.Combine(tempDirectory.Path, expectedName);

        var crmTemplate = includeMargin ? CrmTemplates.Uplift : CrmTemplates.NoCalculation;
        CrmWriter.Write(
            result.LineItems,
            actualPath,
            crmTemplate,
            new CrmWriterOptions(VendorName: Vendors.LenovoOutput.ToUpperInvariant(), Margin: 5.00m));

        WorkbookComparer.AssertEqual(actualPath, Path.Combine(root, "samples", "outputs", expectedName));
    }

    [Fact]
    public void LenovoLbpiSplitGroupMatchesGoldenWorkbookCells()
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.LenovoLbpiIsgPdf);
        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "BRDAS019000003V1.pdf"));
        var group = SolutionOutputSplitter.Split(result.LineItems).First();
        using var tempDirectory = new TempDirectory();
        const string expectedName = "BRDAS019000003V1_SIDX02Q2PL_NoCalculation.xlsx";
        var actualPath = Path.Combine(tempDirectory.Path, expectedName);

        group.SolutionId.Should().Be("SIDX02Q2PL");
        CrmWriter.Write(
            group.Items,
            actualPath,
            CrmTemplates.NoCalculation,
            new CrmWriterOptions(VendorName: Vendors.LenovoOutput.ToUpperInvariant()));

        WorkbookComparer.AssertEqual(actualPath, Path.Combine(root, "samples", "outputs", expectedName));
    }

    [Fact]
    public void LenovoWriter_UsesTextSequencesSentinelAndParentComments()
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.LenovoLbpeIsgXls);
        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "BRDAD019200001.xls"));
        using var tempDirectory = new TempDirectory();
        var actualPath = Path.Combine(tempDirectory.Path, "BRDAD019200001_NoCalculation.xlsx");

        CrmWriter.Write(
            result.LineItems,
            actualPath,
            CrmTemplates.NoCalculation,
            new CrmWriterOptions(VendorName: Vendors.LenovoOutput.ToUpperInvariant()));

        using var workbook = new XLWorkbook(actualPath);
        var sheet = workbook.Worksheet(CrmTemplates.NoCalculation);

        sheet.Cell(3, 1).DataType.Should().Be(XLDataType.Text);
        sheet.Cell(3, 1).GetString().Should().Be("1");
        sheet.Cell(3, 9).GetValue<decimal>().Should().Be(43838.80m);
        sheet.Cell(3, 18).GetString().Should().Be("Solution ID: SIDX02SDL3");

        sheet.Cell(4, 1).DataType.Should().Be(XLDataType.Text);
        sheet.Cell(4, 1).GetString().Should().Be("1.01");
        sheet.Cell(4, 9).GetValue<decimal>().Should().Be(0.0001m);
        sheet.Cell(4, 18).IsEmpty().Should().BeTrue();

        sheet.Cell(34, 1).GetString().Should().Be("2");
        sheet.Cell(34, 9).GetValue<decimal>().Should().Be(59703.80m);
        sheet.Cell(34, 18).GetString().Should().Be("Solution ID: SIDX02SDL3");
    }

    // ── CrmWriter (Cisco CCW Quote XLS) ────────────────────────────────

    [Fact]
    public void AnzGenericWriterMatchesGoldenWorkbookCells_Cisco()
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.CiscoCcwQuoteXls);
        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "Quote_9400000001.xls"));
        using var tempDirectory = new TempDirectory();
        const string expectedName = "Quote_9400000001_NoCalculation.xlsx";
        var actualPath = Path.Combine(tempDirectory.Path, expectedName);

        CrmWriter.Write(result.LineItems, actualPath, CrmTemplates.NoCalculation, new CrmWriterOptions(VendorName: "CISCO", Margin: 5.00m));

        WorkbookComparer.AssertEqual(actualPath, Path.Combine(root, "samples", "outputs", expectedName));
    }

    // ── CrmWriter (HP OneConfig XLSX) ───────────────────────

    [Fact]
    public void PercentOffWithUpliftWriter_MatchesGoldenWorkbookCells()
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpOneConfigXlsx);
        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "99000001.xlsx"));
        using var tempDirectory = new TempDirectory();
        const string expectedName = "99000001_PercentOffWithUplift.xlsx";
        var actualPath = Path.Combine(tempDirectory.Path, expectedName);

        CrmWriter.Write(result.LineItems, actualPath, CrmTemplates.PercentOffWithUplift, new CrmWriterOptions(VendorName: "HP", Margin: 5m, ImPercent: 30m));

        WorkbookComparer.AssertEqual(actualPath, Path.Combine(root, "samples", "outputs", expectedName));
    }

    [Fact]
    public void PercentOffWithUpliftWriter_ParentMsrpIsReal_ChildrenAreSentinel()
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpOneConfigXlsx);
        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "99000001.xlsx"));
        using var tempDirectory = new TempDirectory();
        var actualPath = Path.Combine(tempDirectory.Path, "99000001_PercentOffWithUplift.xlsx");

        CrmWriter.Write(result.LineItems, actualPath, CrmTemplates.PercentOffWithUplift, new CrmWriterOptions(VendorName: "HP", Margin: 5m, ImPercent: 30m));

        using var workbook = new XLWorkbook(actualPath);
        var sheet = workbook.Worksheets.First();

        // Row 3 = parent: MSRP col H should be the real price
        sheet.Cell(3, 8).Value.GetNumber().Should().BeApproximately(6042.77, 0.001);

        // Row 4 = first child: MSRP col H should be the sentinel
        sheet.Cell(4, 8).Value.GetNumber().Should().BeApproximately(0.0001, 0.00001);

        // Col I (Cost) should be blank for all rows
        for (var row = 3; row <= 33; row++)
        {
            sheet.Cell(row, 9).Value.IsBlank.Should().BeTrue($"Cost col I should be blank on row {row}");
        }

        // Col K (Margin) = 5 for all rows
        for (var row = 3; row <= 33; row++)
        {
            sheet.Cell(row, 11).Value.GetNumber().Should().Be(5, $"Margin on row {row}");
        }

        // Col X (IM%) = 30 for all rows
        for (var row = 3; row <= 33; row++)
        {
            sheet.Cell(row, 24).Value.GetNumber().Should().Be(30, $"IM% on row {row}");
        }
    }

    // ── Cancelled lines (Zebra Price Concession) ─────────────────────────────
    // A cancelled line is still exported, but priced at a literal 0 — never the zero-price
    // sentinel — because CRM treats 0 as "no price" and falls back to the SAP standard price.

    [Theory]
    [InlineData(CrmTemplates.NoCalculation)]
    [InlineData(CrmTemplates.Uplift)]
    public void CancelledLinesArePricedAtLiteralZero(string crmTemplate)
    {
        var items = new[]
        {
            new LineItem
            {
                Vpn = "ACTIVE-1", Description = "Active line", Cost = 12.34m, Msrp = 50m,
                Qty = 5, MinQty = 5, Comments = "Max Qty: 400", LineSequence = "1"
            },
            new LineItem
            {
                Vpn = "CANCELLED-1", Description = "Cancelled line", Cost = 0m, Msrp = 0m,
                Qty = 1, MinQty = null, IsCancelled = true,
                Comments = "Cancelled (Standard Price)", LineSequence = "2"
            },
        };

        using var tempDirectory = new TempDirectory();
        var actualPath = Path.Combine(tempDirectory.Path, "cancelled.xlsx");

        CrmWriter.Write(items, actualPath, crmTemplate, new CrmWriterOptions(VendorName: "ZEBRA", Margin: 5.00m, OnCost: 3.00m));

        using var workbook = new XLWorkbook(actualPath);
        var sheet = workbook.Worksheet(1);

        // Row 3 — active line: real prices, Qty from the item, On Cost % written.
        sheet.Cell(3, 6).Value.GetNumber().Should().Be(5);
        sheet.Cell(3, 8).Value.GetNumber().Should().BeApproximately(50, 0.001);
        sheet.Cell(3, 9).Value.GetNumber().Should().BeApproximately(12.34, 0.001);
        sheet.Cell(3, 18).Value.GetText().Should().Be("Max Qty: 400");
        sheet.Cell(3, 23).Value.GetNumber().Should().Be(5);
        sheet.Cell(3, 26).Value.GetNumber().Should().Be(3);

        // Row 4 — cancelled line: Qty 1, MSRP/Cost literal 0 (not the sentinel),
        // Min Order Qty and On Cost % blank, comment written.
        sheet.Cell(4, 6).Value.GetNumber().Should().Be(1);
        sheet.Cell(4, 8).Value.GetNumber().Should().Be(0);
        sheet.Cell(4, 9).Value.GetNumber().Should().Be(0);
        sheet.Cell(4, 18).Value.GetText().Should().Be("Cancelled (Standard Price)");
        sheet.Cell(4, 23).Value.IsBlank.Should().BeTrue("Min Order Qty is blank for cancelled lines");
        sheet.Cell(4, 26).Value.IsBlank.Should().BeTrue("On Cost % is blank for cancelled lines");
    }

    [Theory]
    [InlineData(CrmTemplates.ForeignUplift, true)]
    [InlineData(CrmTemplates.NoCalculation, true)]
    [InlineData(CrmTemplates.Uplift, true)]
    [InlineData(CrmTemplates.PercentOffWithUplift, false)]
    public void SerialNumberIsWrittenToColumnMWithoutReplacingComments(string crmTemplate, bool commentsEnabled)
    {
        var items = new[]
        {
            new LineItem
            {
                Vpn = "SERIAL-SKU", Cost = 10m, Qty = 1,
                SerialNumber = "SERIAL-123", Comments = "Keep this comment"
            },
        };

        using var tempDirectory = new TempDirectory();
        var actualPath = Path.Combine(tempDirectory.Path, "serial-number.xlsx");

        CrmWriter.Write(items, actualPath, crmTemplate, new CrmWriterOptions(VendorName: "TEST"));

        using var workbook = new XLWorkbook(actualPath);
        var sheet = workbook.Worksheet(1);
        sheet.Cell(3, 13).Value.GetText().Should().Be("SERIAL-123");

        if (commentsEnabled)
        {
            sheet.Cell(3, 18).Value.GetText().Should().Be("Keep this comment");
        }
        else
        {
            sheet.Cell(3, 18).Value.IsBlank.Should().BeTrue();
        }
    }

    // ── OutputNaming ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("XQ-9100002.pdf", CrmTemplates.ForeignUplift, "XQ-9100002_ForeignUplift.xlsx")]
    [InlineData("XQ-9100002.xlsx", CrmTemplates.ForeignUplift, "XQ-9100002_ForeignUplift.xlsx")]
    [InlineData("quote.pdf", CrmTemplates.NoCalculation, "quote_NoCalculation.xlsx")]
    [InlineData("quote.pdf", CrmTemplates.Uplift, "quote_Uplift.xlsx")]
    [InlineData("quote.pdf", CrmTemplates.PercentOffWithUplift, "quote_PercentOffWithUplift.xlsx")]
    public void OutputFilenameUsesSourceStem(string sourceFilename, string crmTemplate, string expected)
    {
        OutputNaming.OutputFilename(sourceFilename, crmTemplate).Should().Be(expected);
    }

    [Theory]
    [InlineData("source.xlsx", CrmTemplates.Uplift, "BRDAD011030915", "3", "BRDAD011030915_3_Uplift.xlsx")]
    [InlineData("source.xlsx", CrmTemplates.ForeignUplift, "XQ-9100002", "1", "XQ-9100002_1_ForeignUplift.xlsx")]
    [InlineData("source.xlsx", CrmTemplates.NoCalculation, "97000001", "2.0", "97000001_2.0_NoCalculation.xlsx")]
    public void OutputFilenameUsesBidMetadataWhenBothFieldsAreAvailable(
        string sourceFilename,
        string crmTemplate,
        string bidNumber,
        string bidRevision,
        string expected)
    {
        OutputNaming.OutputFilename(sourceFilename, crmTemplate, bidNumber, bidRevision)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "3")]
    [InlineData("BRDAD011030915", null)]
    [InlineData("", "3")]
    [InlineData("BRDAD011030915", " ")]
    public void OutputFilenameFallsBackToSourceStemWhenEitherBidFieldIsUnavailable(
        string? bidNumber,
        string? bidRevision)
    {
        OutputNaming.OutputFilename("source.xlsx", CrmTemplates.Uplift, bidNumber, bidRevision)
            .Should().Be("source_Uplift.xlsx");
    }

    [Fact]
    public void OutputFilenameSanitisesBidMetadata()
    {
        OutputNaming.OutputFilename("source.xlsx", CrmTemplates.Uplift, "BID/unsafe", "v:3")
            .Should().Be("BIDunsafe_v3_Uplift.xlsx");
    }

    [Theory]
    [InlineData("BRDAD019200001.xls", CrmTemplates.NoCalculation, "SIDX02SDL3", "BRDAD019200001_SIDX02SDL3_NoCalculation.xlsx")]
    [InlineData("BRDAD019200001.xls", CrmTemplates.NoCalculation, null, "BRDAD019200001_NoSolutionID_NoCalculation.xlsx")]
    [InlineData("quote.xls", CrmTemplates.Uplift, "SID unsafe/../", "quote_SID_unsafe.._Uplift.xlsx")]
    public void SplitOutputFilenameUsesSafeSolutionToken(
        string sourceFilename,
        string crmTemplate,
        string? solutionId,
        string expected)
    {
        OutputNaming.OutputFilename(sourceFilename, crmTemplate, solutionId).Should().Be(expected);
    }

    [Fact]
    public void OutputArchiveFilenameUsesSourceStem()
    {
        OutputNaming.OutputArchiveFilename("BRDAD019200001.xls", CrmTemplates.NoCalculation)
            .Should().Be("BRDAD019200001_NoCalculation.zip");
    }

    [Fact]
    public void SplitOutputNamesUseBidMetadata()
    {
        OutputNaming.OutputArchiveFilename(
                "source.xls", CrmTemplates.NoCalculation, "BRDAD011030915", "3")
            .Should().Be("BRDAD011030915_3_NoCalculation.zip");
        OutputNaming.OutputFilename(
                "source.xls", CrmTemplates.NoCalculation, "SID-1", "BRDAD011030915", "3")
            .Should().Be("BRDAD011030915_3_SID-1_NoCalculation.xlsx");
    }

    [Fact]
    public void LenovoSplitGroupMatchesGoldenWorkbookCells()
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.LenovoLbpeIsgXls);
        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "Bid_Platform_Bid_Request_Sample_03.xls"));
        var group = SolutionOutputSplitter.Split(result.LineItems).First();
        using var tempDirectory = new TempDirectory();
        const string expectedName = "Bid_Platform_Bid_Request_Sample_03_SIDX02YU2Q_NoCalculation.xlsx";
        var actualPath = Path.Combine(tempDirectory.Path, expectedName);

        CrmWriter.Write(
            group.Items,
            actualPath,
            CrmTemplates.NoCalculation,
            new CrmWriterOptions(VendorName: Vendors.LenovoOutput.ToUpperInvariant()));

        WorkbookComparer.AssertEqual(actualPath, Path.Combine(root, "samples", "outputs", expectedName));
    }

    [Fact]
    public void AnzGenericWriterMatchesGoldenWorkbookCells_DellCto()
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);
        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "Dell_CTO_Sample.json"));
        using var tempDirectory = new TempDirectory();
        const string expectedName = "Dell_CTO_Sample_NoCalculation.xlsx";
        var actualPath = Path.Combine(tempDirectory.Path, expectedName);

        CrmWriter.Write(result.LineItems, actualPath, CrmTemplates.NoCalculation, new CrmWriterOptions(VendorName: "DELL", Margin: 0m));

        WorkbookComparer.AssertEqual(actualPath, Path.Combine(root, "samples", "outputs", expectedName));
    }

    [Fact]
    public void AnzGenericWriterMatchesGoldenWorkbookCells_DellPeripherals()
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);
        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "Dell_Peripherals_Sample.json"));
        using var tempDirectory = new TempDirectory();
        const string expectedName = "Dell_Peripherals_Sample_NoCalculation.xlsx";
        var actualPath = Path.Combine(tempDirectory.Path, expectedName);

        CrmWriter.Write(
            result.LineItems,
            actualPath,
            CrmTemplates.NoCalculation,
            new CrmWriterOptions(VendorName: Vendors.Dell.ToUpperInvariant(), Margin: 0m));

        WorkbookComparer.AssertEqual(actualPath, Path.Combine(root, "samples", "outputs", expectedName));
    }

    [Fact]
    public void AnzGenericWriterMatchesGoldenWorkbookCells_DellApos()
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellAposJson);
        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "Dell_APOS_Sample.json"));
        using var tempDirectory = new TempDirectory();
        const string expectedName = "Dell_APOS_Sample_NoCalculation.xlsx";
        var actualPath = Path.Combine(tempDirectory.Path, expectedName);

        CrmWriter.Write(result.LineItems, actualPath, CrmTemplates.NoCalculation, new CrmWriterOptions(VendorName: "DELL", Margin: 0m));

        WorkbookComparer.AssertEqual(actualPath, Path.Combine(root, "samples", "outputs", expectedName));
    }

    // ── Col A item-sequence fallback typing ──────────────────────────────────
    // When a parser sets no LineSequence the writer falls back to a running counter. Its cell
    // type is per-template and must not drift: the local templates emit text so col A stays
    // uniformly text alongside "1.01"-style child sequences, Foreign Uplift emits real numbers.

    [Theory]
    [InlineData(CrmTemplates.NoCalculation)]
    [InlineData(CrmTemplates.Uplift)]
    public void ItemFallbackIsTextOnLocalTemplates(string crmTemplate)
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpGlobalBidXlsx);
        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "translate_quote_98000001_v25_all.xlsx"));
        result.LineItems.Should().NotBeEmpty();
        result.LineItems.Should().OnlyContain(item => item.LineSequence == null, "HP Global Bid sets no LineSequence");

        using var tempDirectory = new TempDirectory();
        var actualPath = Path.Combine(tempDirectory.Path, "item-fallback.xlsx");

        CrmWriter.Write(result.LineItems, actualPath, crmTemplate, new CrmWriterOptions(VendorName: "HP", Margin: 5.00m));

        using var workbook = new XLWorkbook(actualPath);
        var sheet = workbook.Worksheet(1);
        for (var row = 3; row < 3 + result.LineItems.Count; row++)
        {
            var cell = sheet.Cell(row, 1).Value;
            cell.IsText.Should().BeTrue($"col A on row {row} should be text");
            cell.GetText().Should().Be((row - 2).ToString());
        }
    }

    [Fact]
    public void ItemFallbackIsNumericOnForeignUplift()
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.NutanixSoftwareOnlyPdf);
        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "XQ-9100002.pdf"));
        result.LineItems.Should().NotBeEmpty();

        using var tempDirectory = new TempDirectory();
        var actualPath = Path.Combine(tempDirectory.Path, "item-fallback.xlsx");

        CrmWriter.Write(
            result.LineItems, actualPath, CrmTemplates.ForeignUplift,
            new CrmWriterOptions(VendorName: "NUTANIX", Margin: 5.00m, TermAsComment: parser.TermRendersAsComment));

        using var workbook = new XLWorkbook(actualPath);
        var sheet = workbook.Worksheet(1);
        for (var row = 3; row < 3 + result.LineItems.Count; row++)
        {
            var cell = sheet.Cell(row, 1).Value;
            cell.IsNumber.Should().BeTrue($"col A on row {row} should be numeric");
            cell.GetNumber().Should().Be(row - 2);
        }
    }


    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"bidparser-output-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
