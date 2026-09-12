using BidParser.Desktop.Configuration;
using FluentAssertions;
using Xunit;

namespace BidParser.Desktop.Configuration.Tests;

public sealed class GuidanceMarkupReaderTests
{
    [Fact]
    public void Reads_a_paragraph_with_emphasis_as_runs()
    {
        var document = GuidanceMarkupReader.TryRead(
            "<p>When exporting this quote from CRM, use the <b>Standard</b> report type.</p>");

        var paragraph = document!.Blocks.Should().ContainSingle().Which.Should().BeOfType<GuidanceParagraph>().Subject;
        paragraph.Runs.Should().Equal(
            new GuidanceRun("When exporting this quote from CRM, use the ", false),
            new GuidanceRun("Standard", true),
            new GuidanceRun(" report type.", false));
    }

    [Fact]
    public void Reads_a_bullet_list()
    {
        var document = GuidanceMarkupReader.TryRead(
            "<p>When exporting this quote from CRM,</p>"
            + "<ul><li>Use the <strong>Standard</strong> report type.</li><li>Or the other one.</li></ul>");

        document!.Blocks.Should().HaveCount(2);
        var list = document.Blocks[1].Should().BeOfType<GuidanceBulletList>().Subject;
        list.Items.Should().HaveCount(2);
        list.Items[0].Runs.Should().Equal(
            new GuidanceRun("Use the ", false),
            new GuidanceRun("Standard", true),
            new GuidanceRun(" report type.", false));
        list.Items[1].Runs.Should().ContainSingle().Which.Text.Should().Be("Or the other one.");
    }

    [Fact]
    public void Plain_text_with_no_markup_becomes_one_paragraph()
    {
        var document = GuidanceMarkupReader.TryRead("Just a sentence.");

        document!.Blocks.Should().ContainSingle().Which
            .Should().BeOfType<GuidanceParagraph>().Subject
            .Runs.Should().ContainSingle().Which.Text.Should().Be("Just a sentence.");
    }

    [Fact]
    public void Whitespace_is_collapsed_and_trimmed_at_the_edges()
    {
        var document = GuidanceMarkupReader.TryRead("<p>\n   spaced\t\tout   \n</p>");

        document!.Blocks.Should().ContainSingle().Which
            .Should().BeOfType<GuidanceParagraph>().Subject
            .Runs.Should().ContainSingle().Which.Text.Should().Be("spaced out");
    }

    [Theory]
    // Anything that could execute, navigate, load a resource, or carry styling.
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<p>ok</p><script>alert(1)</script>")]
    [InlineData("<p onclick=\"steal()\">ok</p>")]
    [InlineData("<p class=\"x\">ok</p>")]
    [InlineData("<p style=\"color:red\">ok</p>")]
    [InlineData("<a href=\"https://example.com\">click</a>")]
    [InlineData("<p>see <a href=\"https://example.com\">this</a></p>")]
    [InlineData("<img src=\"https://example.com/x.png\" />")]
    [InlineData("<iframe src=\"https://example.com\"></iframe>")]
    [InlineData("<div>ok</div>")]
    [InlineData("<p>ok<br/></p>")]
    [InlineData("<Button xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">x</Button>")]
    [InlineData("<p xmlns:x=\"urn:x\">ok</p>")]
    [InlineData("<?xml-stylesheet href=\"x.css\"?><p>ok</p>")]
    [InlineData("<ul><li>ok</li><script>x</script></ul>")]
    [InlineData("<ul>loose text<li>ok</li></ul>")]
    [InlineData("<ul><div>ok</div></ul>")]
    [InlineData("<p><strong>outer <b>inner</b></strong></p>")]
    [InlineData("<p><ul><li>nested block</li></ul></p>")]
    public void Rejects_anything_outside_the_schema_subset(string markup)
        => GuidanceMarkupReader.TryRead(markup).Should().BeNull();

    [Theory]
    // A DTD is prohibited outright, so entity-expansion attacks never begin.
    [InlineData("<!DOCTYPE g [<!ENTITY x \"boom\">]><p>&x;</p>")]
    [InlineData("<p>&nbsp;</p>")]
    [InlineData("<p>unclosed")]
    [InlineData("<p>mismatched</div>")]
    public void Rejects_markup_that_is_not_well_formed_xml(string markup)
        => GuidanceMarkupReader.TryRead(markup).Should().BeNull();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<p></p>")]
    [InlineData("<ul></ul>")]
    public void Rejects_a_message_with_nothing_to_show(string? markup)
        => GuidanceMarkupReader.TryRead(markup).Should().BeNull();

    [Fact]
    public void Predefined_xml_entities_are_decoded()
    {
        var document = GuidanceMarkupReader.TryRead("<p>Ingram &amp; Micro</p>");

        document!.Blocks.Should().ContainSingle().Which
            .Should().BeOfType<GuidanceParagraph>().Subject
            .Runs.Should().ContainSingle().Which.Text.Should().Be("Ingram & Micro");
    }
}
