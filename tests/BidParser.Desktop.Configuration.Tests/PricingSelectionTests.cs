using System.Globalization;
using BidParser.Application.Output;
using BidParser.Application.Parsing;
using BidParser.Desktop.Services;
using BidParser.Desktop.ViewModels;
using BidParser.Domain.Constants;
using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Desktop.Configuration.Tests;

public sealed class PricingSelectionTests
{
    [Fact]
    public void Juniper_parsers_hide_and_do_not_require_header_pricing_fields_while_non_juniper_parsers_keep_them()
    {
        var registry = new ParserRegistry();
        var shell = new UnusedShell();
        var model = new MainWindowViewModel(
            new ParserCatalog(registry), new QuoteParseService(registry, new SourceFormatInspector()),
            new WorkbookWriteService(), shell, shell, shell,
            new EmptyVendorDefaultsSource(), new NoGuidanceProvider());
        model.SelectedVendor = model.Vendors.Single(vendor => vendor.Name == Vendors.Juniper);
        model.SelectedParser = model.ParserOptions.Single(parser => parser.Descriptor.Slug == ParserSlugs.JuniperCpqXlsx);
        model.Capabilities.SupportsLinePricing.Should().BeTrue();
        model.Capabilities.SupportsFx.Should().BeFalse();
        model.Capabilities.RequiresFx.Should().BeFalse();
        model.Capabilities.SupportsUplift.Should().BeFalse();
        model.Capabilities.RequiresUplift.Should().BeFalse();
        model.Capabilities.SupportsDiscountOffMsrp.Should().BeFalse();
        model.Capabilities.RequiresDiscountOffMsrp.Should().BeFalse();
        model.FxRate.IsRequired.Should().BeFalse();
        model.Uplift.IsRequired.Should().BeFalse();
        model.DiscountOffMsrp.IsRequired.Should().BeFalse();

        model.SelectedParser = model.ParserOptions.Single(parser => parser.Descriptor.Slug == ParserSlugs.JuniperIrisXlsx);
        model.SelectedTemplate!.Name.Should().Be(CrmTemplates.ForeignUplift);
        model.Capabilities.SupportsLinePricing.Should().BeTrue();
        model.Capabilities.SupportsFx.Should().BeFalse();
        model.Capabilities.RequiresFx.Should().BeFalse();
        model.Capabilities.SupportsUplift.Should().BeFalse();
        model.Capabilities.RequiresUplift.Should().BeFalse();
        model.Capabilities.SupportsDiscountOffMsrp.Should().BeFalse();
        model.Capabilities.RequiresDiscountOffMsrp.Should().BeFalse();
        model.FxRate.IsRequired.Should().BeFalse();
        model.Uplift.IsRequired.Should().BeFalse();
        model.DiscountOffMsrp.IsRequired.Should().BeFalse();
        model.DiscountOffMsrp.HasError.Should().BeFalse();

        model.SelectedParser = model.ParserOptions.Single(parser => parser.Descriptor.Slug == ParserSlugs.JuniperIndirectXlsx);
        model.Capabilities.SupportsLinePricing.Should().BeTrue();
        model.Capabilities.SupportsFx.Should().BeFalse();
        model.Capabilities.SupportsUplift.Should().BeFalse();
        model.Capabilities.SupportsDiscountOffMsrp.Should().BeFalse();

        // Switching to a non-Juniper Foreign Uplift parser restores header FX and Uplift requirements.
        model.SelectedVendor = model.Vendors.Single(vendor => vendor.Name == Vendors.Nutanix);
        model.SelectedParser = model.ParserOptions.Single(parser => parser.Descriptor.Slug == ParserSlugs.NutanixSoftwareOnlyPdf);
        model.Capabilities.SupportsLinePricing.Should().BeFalse();
        model.Capabilities.SupportsFx.Should().BeTrue();
        model.Capabilities.RequiresFx.Should().BeTrue();
        model.Capabilities.SupportsUplift.Should().BeTrue();
        model.Capabilities.RequiresUplift.Should().BeTrue();
        model.Capabilities.SupportsDiscountOffMsrp.Should().BeFalse();
    }

    private sealed class UnusedShell : IFileDialogService, IClipboardService, IShellLauncher
    {
        public string? ChooseSourceFile(string filter) => throw new InvalidOperationException();
        public string? ChooseOutputFile(string suggestedName, string filter, string? initialDirectory)
            => throw new InvalidOperationException();
        public void SetText(string text) => throw new InvalidOperationException();
        public void Open(string target) => throw new InvalidOperationException();
        public void RevealInFolder(string path) => throw new InvalidOperationException();
    }
}
