using BidParser.Domain.Models;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

/// <summary>
/// Pins the canonical one-cent tolerance against the LBP-I parent-cost rule
/// (cost = round(solutionTotal / qty, 2)): a rounding drift of at most one cent is accepted
/// as a match; anything larger surfaces through the validation-mismatch flow.
/// </summary>
public sealed class ParseValidationTests
{
    [Fact]
    public void RoundedDivisionDrift_WithinOneCent_IsAcceptedAsMatch()
    {
        // 100.00 / 3 → 33.33; computed 99.99, one cent under the quoted total.
        var items = new List<LineItem> { new() { Vpn = "X", Cost = 33.33m, Qty = 3 } };

        var validation = ParseValidation.Validate(items, 100.00m);

        validation.Matches.Should().BeTrue();
        validation.Difference.Should().Be(-0.01m);
    }

    [Fact]
    public void RoundedDivisionDrift_BeyondOneCent_SurfacesAsMismatch()
    {
        // 100.00 / 7 → 14.29; computed 100.03, three cents over.
        var items = new List<LineItem> { new() { Vpn = "X", Cost = 14.29m, Qty = 7 } };

        var validation = ParseValidation.Validate(items, 100.00m);

        validation.Matches.Should().BeFalse();
        validation.Difference.Should().Be(0.03m);
    }
}
