using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class ParserRegistryTests
{
    [Fact]
    public void Phase5_registry_is_explicit_and_empty_until_supplier_parsers_land()
    {
        new ParserRegistry().Parsers.Should().BeEmpty();
    }
}
