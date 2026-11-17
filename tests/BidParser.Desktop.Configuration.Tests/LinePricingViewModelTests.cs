using System.Globalization;
using BidParser.Application.Output;
using BidParser.Desktop.Services;
using BidParser.Desktop.ViewModels;
using BidParser.Domain.Constants;
using FluentAssertions;
using Xunit;

namespace BidParser.Desktop.Configuration.Tests;

public sealed class LinePricingViewModelTests
{
    private static LinePricingPreview CreateR5Preview()
    {
        var lines = new List<LinePricingLine>
        {
            new(0, "1", "EX2300-24P", "24-port PoE+ switch", 2, 1450.00m, 725.00m, 50.00m, false),
            new(1, "2", "EX-SFP-10GE-DAC-1M", "10GbE Direct Attach Copper Cable 1m", 3, 400.00m, 200.00m, 50.00m, false),
            new(2, "3", "SVC-COR-EX2300-24P", "Juniper Care Core Support for EX2300-24P", 1, 800.00m, 560.00m, 30.00m, false)
        };
        return new LinePricingPreview(
            ParserSlug: ParserSlugs.JuniperCpqXlsx,
            ParserDisplayName: "Juniper CPQ XLSX",
            Currency: "USD",
            CrmTemplate: CrmTemplates.ForeignUplift,
            UsesFxRate: true,
            ImPercentEditable: true,
            ImPercentRequired: false,
            Lines: lines);
    }

    private static LinePricingPreview CreateIrisPreview()
    {
        var lines = new List<LinePricingLine>
        {
            new(0, "1", "EX2300-24P", "24-port PoE+ switch", 2, 1450.00m, null, null, false),
            new(1, "2", "EX-SFP-10GE-DAC-1M", "10GbE Direct Attach Copper Cable 1m", 3, 400.00m, null, null, false)
        };
        return new LinePricingPreview(
            ParserSlug: ParserSlugs.JuniperIrisXlsx,
            ParserDisplayName: "Juniper Iris XLSX",
            Currency: "USD",
            CrmTemplate: CrmTemplates.ForeignUplift,
            UsesFxRate: true,
            ImPercentEditable: true,
            ImPercentRequired: true,
            Lines: lines);
    }

    [Fact]
    public void Prefill_from_vendor_defaults_populates_fx_header_and_row_uplifts()
    {
        var preview = CreateR5Preview();
        var defaults = new VendorDefaults(FxRate: 1.55m, Margin: 5m, ImPercent: 40m);
        var vm = new LinePricingViewModel(preview, "Juniper_CPQ.xlsx", defaults);

        vm.FxRate.Value.Should().Be(1.55m);
        vm.HeaderImPercent.Value.Should().Be(40m);
        vm.HeaderUplift.Value.Should().Be(5m);
        vm.Rows.Should().AllSatisfy(row =>
        {
            row.Uplift.Should().Be(5m.ToString("F2", CultureInfo.CurrentCulture));
            row.IsImOverridden.Should().BeFalse();
        });
        vm.Rows[0].ImPercent.Should().Be(50.00m.ToString("F2", CultureInfo.CurrentCulture));
        vm.Rows[1].ImPercent.Should().Be(50.00m.ToString("F2", CultureInfo.CurrentCulture));
        vm.Rows[2].ImPercent.Should().Be(30.00m.ToString("F2", CultureInfo.CurrentCulture));
        vm.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ApplyIm_and_ApplyUplift_overwrite_row_edits_and_mark_cpq_overrides()
    {
        var preview = CreateR5Preview();
        var vm = new LinePricingViewModel(preview, "Juniper_CPQ.xlsx");

        // Manual edit on row 0 first
        vm.Rows[0].Uplift = 8m.ToString(CultureInfo.CurrentCulture);
        vm.Rows[0].ImPercent = 55m.ToString(CultureInfo.CurrentCulture);
        vm.Rows[0].IsImOverridden.Should().BeTrue();
        vm.Rows[0].IsImEditedAfterApply.Should().BeTrue();
        vm.Rows[0].IsUpliftEditedAfterApply.Should().BeTrue();
        vm.Rows[1].IsImOverridden.Should().BeFalse();

        // Header Apply IM% overwrites all rows, marks CPQ rows as overridden, and clears post-apply edited flag
        vm.HeaderImPercent.Text = 45m.ToString(CultureInfo.CurrentCulture);
        vm.ApplyImPercentCommand.CanExecute(null).Should().BeTrue();
        vm.ApplyImPercentCommand.Execute(null);

        vm.Rows.Should().AllSatisfy(row =>
        {
            row.ImPercent.Should().Be(45m.ToString("F2", CultureInfo.CurrentCulture));
            row.IsImOverridden.Should().BeTrue();
            row.IsImEditedAfterApply.Should().BeFalse();
        });

        // Header Apply Uplift overwrites all rows and clears post-apply edited flag
        vm.HeaderUplift.Text = 6m.ToString(CultureInfo.CurrentCulture);
        vm.ApplyUpliftCommand.CanExecute(null).Should().BeTrue();
        vm.ApplyUpliftCommand.Execute(null);

        vm.Rows.Should().AllSatisfy(row =>
        {
            row.Uplift.Should().Be(6m.ToString("F2", CultureInfo.CurrentCulture));
            row.IsUpliftEditedAfterApply.Should().BeFalse();
        });
    }

    [Fact]
    public void Clearing_cpq_row_im_reverts_to_derived_im_and_unrounded_source_cost()
    {
        var preview = new LinePricingPreview(
            ParserSlug: ParserSlugs.JuniperCpqXlsx,
            ParserDisplayName: "Juniper CPQ XLSX",
            Currency: "USD",
            CrmTemplate: CrmTemplates.ForeignUplift,
            UsesFxRate: true,
            ImPercentEditable: true,
            ImPercentRequired: false,
            Lines:
            [
                new LinePricingLine(0, "1", "SUB-1", "Subscription", 1, 558.38m, 362.9512m, 35.00m, false)
            ]);

        var vm = new LinePricingViewModel(
            preview,
            "Juniper_CPQ.xlsx",
            new VendorDefaults(FxRate: 1.55m, Margin: 5m, ImPercent: null));

        var row = vm.Rows[0];
        row.IsImOverridden.Should().BeFalse();
        row.UnitCost.Should().Be(362.9512m);
        row.UnitCostDisplay.Should().Be(362.95m.ToString("N2", CultureInfo.CurrentCulture));

        // Override IM% to 35.00 -> calculated unit cost becomes 362.95m
        row.ImPercent = 35m.ToString("0.00", CultureInfo.CurrentCulture);
        row.IsImOverridden.Should().BeTrue();
        row.UnitCost.Should().Be(362.95m);
        vm.ToEntries()[0].ImPercent.Should().Be(35m);

        // Clear IM% on CPQ -> reverts to untouched unrounded source cost
        row.ImPercent = string.Empty;
        row.IsImOverridden.Should().BeFalse();
        row.UnitCost.Should().Be(362.9512m);
        row.ImErrorMessage.Should().BeNull();
        vm.ToEntries()[0].ImPercent.Should().BeNull();
    }

    [Fact]
    public void Validation_gates_NextCommand_for_missing_iris_im_uplift_100_excess_precision_and_fx()
    {
        var preview = CreateIrisPreview();
        var completed = false;
        var vm = new LinePricingViewModel(
            preview,
            "Juniper_Iris.xlsx",
            defaults: null,
            onNext: () => completed = true);

        // Initially missing FX, IM%, and Uplift
        vm.IsValid.Should().BeFalse();
        vm.NextCommand.CanExecute(null).Should().BeFalse();

        vm.FxRate.Text = 1.5479m.ToString(CultureInfo.CurrentCulture);
        vm.HeaderImPercent.Text = 50m.ToString(CultureInfo.CurrentCulture);
        vm.ApplyImPercentCommand.Execute(null);
        vm.HeaderUplift.Text = 5m.ToString(CultureInfo.CurrentCulture);
        vm.ApplyUpliftCommand.Execute(null);

        vm.IsValid.Should().BeTrue();
        vm.NextCommand.CanExecute(null).Should().BeTrue();

        // Uplift = 100 is rejected (strict < 100)
        vm.Rows[0].Uplift = 100m.ToString(CultureInfo.CurrentCulture);
        vm.Rows[0].UpliftErrorMessage.Should().NotBeNull();
        vm.IsValid.Should().BeFalse();
        vm.NextCommand.CanExecute(null).Should().BeFalse();

        // Restore valid uplift, test > 2 decimal places on IM%
        vm.Rows[0].Uplift = 5m.ToString(CultureInfo.CurrentCulture);
        vm.IsValid.Should().BeTrue();

        vm.Rows[0].ImPercent = 50.125m.ToString(CultureInfo.CurrentCulture);
        vm.Rows[0].ImErrorMessage.Should().NotBeNull();
        vm.IsValid.Should().BeFalse();

        // Restore valid 2 dp IM% and execute NextCommand
        vm.Rows[0].ImPercent = 50.12m.ToString(CultureInfo.CurrentCulture);
        vm.IsValid.Should().BeTrue();
        vm.NextCommand.Execute(null);
        completed.Should().BeTrue();
    }

    [Fact]
    public void Im_percent_is_limited_to_0_through_100_and_totals_hide_while_any_line_is_invalid()
    {
        var vm = new LinePricingViewModel(
            CreateIrisPreview(),
            "Juniper_Iris.xlsx",
            new VendorDefaults(FxRate: 0.652m, Margin: 4.76m, ImPercent: 15m));
        vm.ApplyImPercentCommand.Execute(null);
        vm.ApplyUpliftCommand.Execute(null);
        vm.AreTotalsAvailable.Should().BeTrue();
        vm.TotalCostDisplay.Should().NotBe("—");

        // Exactly 100 is allowed: the line becomes free.
        vm.Rows[0].ImPercent = 100m.ToString(CultureInfo.CurrentCulture);
        vm.Rows[0].ImErrorMessage.Should().BeNull();
        vm.IsValid.Should().BeTrue();

        // Above 100 is rejected, blocks Next, and hides every total rather than showing a partial sum.
        vm.Rows[0].ImPercent = 100.01m.ToString(CultureInfo.CurrentCulture);
        vm.Rows[0].ImErrorMessage.Should().Be("Line 1: IM% cannot be more than 100.");
        vm.IsValid.Should().BeFalse();
        vm.NextCommand.CanExecute(null).Should().BeFalse();
        vm.AreTotalsAvailable.Should().BeFalse();
        vm.TotalCostDisplay.Should().Be("—");
        vm.TotalPriceDisplay.Should().Be("—");
        vm.TotalMarginAmountDisplay.Should().Be("—");
        vm.TotalMarginPercentDisplay.Should().Be("—");

        // The header IM% field enforces the same ceiling, so Apply to all cannot push it past 100.
        vm.HeaderImPercent.Text = 100.5m.ToString(CultureInfo.CurrentCulture);
        vm.HeaderImPercent.HasError.Should().BeTrue();
        vm.ApplyImPercentCommand.CanExecute(null).Should().BeFalse();
        vm.HeaderImPercent.Text = 100m.ToString(CultureInfo.CurrentCulture);
        vm.HeaderImPercent.HasError.Should().BeFalse();

        // Fixing the line restores the totals.
        vm.Rows[0].ImPercent = 15m.ToString(CultureInfo.CurrentCulture);
        vm.AreTotalsAvailable.Should().BeTrue();
        vm.TotalCostDisplay.Should().NotBe("—");
    }

    [Fact]
    public void Totals_and_ToEntries_match_reference_case_R5()
    {
        var preview = CreateR5Preview();
        var vm = new LinePricingViewModel(
            preview,
            "Juniper_CPQ.xlsx",
            new VendorDefaults(FxRate: 1.5479m, Margin: 5m, ImPercent: null));

        // Row 1 (index 0): untouched CPQ, uplift 5%
        // Row 2 (index 1): override IM% to 55%, uplift 8%
        // Row 3 (index 2): untouched CPQ, uplift 0%
        vm.Rows[1].ImPercent = 55m.ToString(CultureInfo.CurrentCulture);
        vm.Rows[1].Uplift = 8m.ToString(CultureInfo.CurrentCulture);
        vm.Rows[2].Uplift = 0m.ToString(CultureInfo.CurrentCulture);

        vm.Rows[0].UnitCost.Should().Be(725.00m);
        vm.Rows[0].ExtCost.Should().Be(1450.00m);
        vm.Rows[0].UnitPrice.Should().Be(763.16m);
        vm.Rows[0].ExtPrice.Should().Be(1526.32m);

        vm.Rows[1].UnitCost.Should().Be(180.00m);
        vm.Rows[1].ExtCost.Should().Be(540.00m);
        vm.Rows[1].UnitPrice.Should().Be(195.65m);
        vm.Rows[1].ExtPrice.Should().Be(586.95m);

        vm.Rows[2].UnitCost.Should().Be(560.00m);
        vm.Rows[2].ExtCost.Should().Be(560.00m);
        vm.Rows[2].UnitPrice.Should().Be(560.00m);
        vm.Rows[2].ExtPrice.Should().Be(560.00m);

        vm.TotalCost.Should().Be(2550.00m);
        vm.TotalPrice.Should().Be(2673.27m);
        vm.TotalMarginAmount.Should().Be(123.27m);
        vm.TotalMarginPercent.Should().Be(4.61m);

        var entries = vm.ToEntries();
        entries.Should().HaveCount(3);
        entries[0].Should().Be(new LinePricingEntry(0, "EX2300-24P", 5m, null));
        entries[1].Should().Be(new LinePricingEntry(1, "EX-SFP-10GE-DAC-1M", 8m, 55m));
        entries[2].Should().Be(new LinePricingEntry(2, "SVC-COR-EX2300-24P", 0m, null));
    }
}
