using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class ParserRegistryTests
{
    [Fact]
    public void Registry_is_explicit_and_ordered()
    {
        new ParserRegistry().Parsers
            .Select(parser => parser.Slug)
            .Should()
            .Equal(
                "nutanix_software_only_pdf",
                "nutanix_software_only_xlsx",
                "nutanix_renewal_pdf");
    }
}
