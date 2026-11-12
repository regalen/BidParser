using BidParser.Desktop.Mvvm;
using FluentAssertions;
using System.Globalization;
using Xunit;

namespace BidParser.Desktop.Configuration.Tests;

public sealed class NumericFieldViewModelTests
{
    [Fact]
    public void Iris_precision_is_rejected_before_focus_loss_and_has_no_upper_limit()
    {
        var field = new NumericFieldViewModel("IM%", "%", "2 d.p.", 2) { RejectExcessPrecision = true };
        field.Text = 1.234m.ToString(CultureInfo.CurrentCulture);
        field.Normalize();
        field.HasError.Should().BeTrue();
        field.Value.Should().BeNull();
        field.Text = 130.25m.ToString(CultureInfo.CurrentCulture);
        field.HasError.Should().BeFalse();
        field.Value.Should().Be(130.25m);
        field.Text = "0";
        field.HasError.Should().BeFalse();
    }

    [Fact]
    public void Required_error_clears_as_soon_as_valid_text_is_entered()
    {
        var field = new NumericFieldViewModel("Uplift", "%", "%, 2 d.p.", decimals: 2)
        {
            IsRequired = true
        };

        field.HasError.Should().BeTrue();

        field.Text = "6";

        field.HasError.Should().BeFalse();
        field.ErrorMessage.Should().BeNull();
        field.Value.Should().Be(6m);
    }

    [Fact]
    public void Valid_text_is_normalized_only_after_editing_finishes()
    {
        var field = new NumericFieldViewModel("Uplift", "%", "%, 2 d.p.", decimals: 2);

        field.Text = "6";

        field.Text.Should().Be("6");

        field.Normalize();

        field.Text.Should().Be(6m.ToString("F2", CultureInfo.CurrentCulture));
    }
}
