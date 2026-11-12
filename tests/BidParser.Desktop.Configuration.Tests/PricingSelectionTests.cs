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
    public void Switching_between_foreign_uplift_parsers_refreshes_im_requirements_and_keeps_edits()
    {
        var registry = new ParserRegistry();
        var shell = new UnusedShell();
        var model = new MainWindowViewModel(
            new ParserCatalog(registry), new QuoteParseService(registry, new SourceFormatInspector()),
            new WorkbookWriteService(), shell, shell, shell,
            new EmptyVendorDefaultsSource(), new NoGuidanceProvider());
        model.SelectedVendor = model.Vendors.Single(vendor => vendor.Name == Vendors.Juniper);
        model.SelectedParser = model.ParserOptions.Single(parser => parser.Descriptor.Slug == ParserSlugs.JuniperCpqXlsx);
        model.Capabilities.SupportsDiscountOffMsrp.Should().BeFalse();
        model.Uplift.Text = "5";

        model.SelectedParser = model.ParserOptions.Single(parser => parser.Descriptor.Slug == ParserSlugs.JuniperIrisXlsx);
        model.SelectedTemplate!.Name.Should().Be(CrmTemplates.ForeignUplift);
        model.Capabilities.RequiresDiscountOffMsrp.Should().BeTrue();
        model.DiscountOffMsrp.IsRequired.Should().BeTrue();
        model.DiscountOffMsrp.RejectExcessPrecision.Should().BeTrue();
        model.DiscountOffMsrp.Label.Should().Be("IM% (Discount Off MSRP)");
        model.DiscountOffMsrp.HasError.Should().BeTrue();
        model.DiscountOffMsrp.Text = 27.53m.ToString(CultureInfo.CurrentCulture);

        model.SelectedParser = model.ParserOptions.Single(parser => parser.Descriptor.Slug == ParserSlugs.JuniperIndirectXlsx);
        model.Capabilities.SupportsDiscountOffMsrp.Should().BeFalse();
        model.DiscountOffMsrp.IsRequired.Should().BeFalse();
        model.DiscountOffMsrp.RejectExcessPrecision.Should().BeFalse();

        model.SelectedParser = model.ParserOptions.Single(parser => parser.Descriptor.Slug == ParserSlugs.JuniperIrisXlsx);
        model.Capabilities.RequiresDiscountOffMsrp.Should().BeTrue();
        model.DiscountOffMsrp.Value.Should().Be(27.53m);
        model.Uplift.Value.Should().Be(5m);
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
