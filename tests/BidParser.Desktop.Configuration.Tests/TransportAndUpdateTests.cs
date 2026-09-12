using System.Net;
using BidParser.Desktop.Configuration;
using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Desktop.Configuration.Tests;

public sealed class TransportAndUpdateTests
{
    private static readonly IReadOnlySet<string> KnownSlugs =
        new ParserRegistry().Parsers.Select(parser => parser.Slug).ToHashSet(StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> KnownVendors =
        new ParserRegistry().Parsers.Select(parser => parser.Vendor).ToHashSet(StringComparer.Ordinal);

    private const string ValidGuidance =
        """{"schemaVersion": 1, "guidanceMessages": [{"fileTypes": ["strike_quote_pdf"], "html": "<p>ok</p>"}]}""";

    private static PublicDocumentFetcher Fetcher(FakeHttpHandler handler)
    {
        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BidParser/9.9.9");
        return new PublicDocumentFetcher(client);
    }

    private static Task<string?> Fetch(FakeHttpHandler handler) =>
        Fetcher(handler).TryFetchAsync(DesktopEndpoints.VendorDefaults, DesktopEndpoints.MaxConfigurationBytes, default);

    [Fact]
    public void Public_configuration_and_release_endpoints_target_the_main_repository()
    {
        DesktopEndpoints.PublicRepository.Should().Be("https://github.com/regalen/BidParser");
        DesktopEndpoints.GuidanceMessages.Should().Be(
            new Uri("https://raw.githubusercontent.com/regalen/BidParser/main/config/guidanceMessages.json"));
        DesktopEndpoints.VendorDefaults.Should().Be(
            new Uri("https://raw.githubusercontent.com/regalen/BidParser/main/config/vendorDefaults.json"));
        DesktopEndpoints.LatestRelease.Should().Be(
            new Uri("https://api.github.com/repos/regalen/BidParser/releases/latest"));
    }

    [Fact]
    public async Task Json_served_as_text_plain_is_accepted()
    {
        // raw.githubusercontent.com serves JSON as text/plain; content type is deliberately unchecked.
        var body = await Fetch(FakeHttpHandler.Returning(HttpStatusCode.OK, ValidGuidance, "text/plain"));

        body.Should().Be(ValidGuidance);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ProxyAuthenticationRequired)]
    public async Task An_unsuccessful_status_yields_nothing(HttpStatusCode status)
        => (await Fetch(FakeHttpHandler.Returning(status, ValidGuidance))).Should().BeNull();

    [Fact]
    public async Task A_redirect_is_a_failure_rather_than_something_to_follow()
        => (await Fetch(FakeHttpHandler.Redirecting("https://example.com/elsewhere.json"))).Should().BeNull();

    [Fact]
    public async Task A_body_larger_than_the_cap_is_rejected()
    {
        var oversized = new string('x', (int)DesktopEndpoints.MaxConfigurationBytes + 1);

        (await Fetch(FakeHttpHandler.Returning(HttpStatusCode.OK, oversized))).Should().BeNull();
    }

    [Fact]
    public async Task A_declared_length_over_the_cap_is_rejected_before_the_body_is_read()
        => (await Fetch(FakeHttpHandler.Returning(
                HttpStatusCode.OK, "{}", contentLengthOverride: DesktopEndpoints.MaxConfigurationBytes + 1)))
            .Should().BeNull();

    [Theory]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(IOException))]
    public async Task A_network_failure_is_silent(Type errorType)
    {
        var error = (Exception)Activator.CreateInstance(errorType)!;

        (await Fetch(FakeHttpHandler.Throwing(error))).Should().BeNull();
    }

    [Fact]
    public async Task A_request_that_never_answers_ends_at_the_per_request_timeout()
    {
        var started = DateTimeOffset.UtcNow;

        var body = await Fetch(FakeHttpHandler.Hanging());

        body.Should().BeNull();
        DateTimeOffset.UtcNow.Subtract(started).Should().BeLessThan(DesktopEndpoints.RequestTimeout * 3);
    }

    [Fact]
    public async Task The_two_configuration_documents_fall_back_independently()
    {
        // Guidance is served; defaults 404. One good document must not be discarded with the bad one.
        var guidance = await new RemoteConfigurationService(
                Fetcher(FakeHttpHandler.Returning(HttpStatusCode.OK, ValidGuidance)))
            .TryFetchGuidanceAsync(KnownSlugs, default);
        var defaults = await new RemoteConfigurationService(
                Fetcher(FakeHttpHandler.Returning(HttpStatusCode.NotFound, string.Empty)))
            .TryFetchVendorDefaultsAsync(KnownVendors, default);

        guidance.Should().NotBeNull();
        defaults.Should().BeNull();
    }

    [Fact]
    public async Task A_served_but_invalid_document_still_falls_back()
        => (await new RemoteConfigurationService(
                Fetcher(FakeHttpHandler.Returning(HttpStatusCode.OK, """{"schemaVersion": 4}""")))
            .TryFetchGuidanceAsync(KnownSlugs, default)).Should().BeNull();

    [Fact]
    public async Task The_configured_endpoint_and_user_agent_are_what_go_on_the_wire()
    {
        var handler = FakeHttpHandler.Returning(HttpStatusCode.OK, ValidGuidance);

        await new RemoteConfigurationService(Fetcher(handler)).TryFetchGuidanceAsync(KnownSlugs, default);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.RequestUri.Should().Be(DesktopEndpoints.GuidanceMessages);
        request.Headers.UserAgent.ToString().Should().StartWith("BidParser/");
        request.Headers.Authorization.Should().BeNull();
    }

    private static Task<AvailableUpdate?> Check(string installed, string tag)
        => new UpdateCheckService(Fetcher(FakeHttpHandler.Returning(
                HttpStatusCode.OK,
                $$"""{"tag_name": "{{tag}}", "html_url": "https://evil.example.com/pwn"}""")))
            .TryCheckAsync(installed, default);

    [Theory]
    [InlineData("1.0.0", "v1.0.1")]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1.0.0", "v1.1.0")]
    [InlineData("1.0.0-rc.1", "v1.0.0")]
    [InlineData("1.0.0+build77", "v1.0.1")]
    public async Task A_newer_release_is_reported(string installed, string tag)
        => (await Check(installed, tag)).Should().NotBeNull();

    [Theory]
    [InlineData("1.0.0", "v1.0.0")]
    [InlineData("1.0.0", "v1.0.0-rc.1")]
    [InlineData("1.0.0", "v1.0.0-beta.1")]
    // Build metadata does not affect precedence, so this is the same release.
    [InlineData("1.0.0", "v1.0.0+abc123")]
    [InlineData("1.0.0+abc123", "v1.0.0")]
    // A prerelease of a version that is not yet released is still ahead of the last stable.
    [InlineData("1.1.0-rc.1", "v1.0.0")]
    public async Task The_same_or_an_older_release_says_nothing(string installed, string tag)
        => (await Check(installed, tag)).Should().BeNull();

    [Theory]
    [InlineData("not-a-version", "v1.0.0")]
    [InlineData("", "v1.0.0")]
    [InlineData("1.0.0", "release-candidate")]
    [InlineData("1.0.0", "vv2.0.0")]
    [InlineData("1.0.0", "")]
    public async Task An_unparseable_version_on_either_side_says_nothing(string installed, string tag)
        => (await Check(installed, tag)).Should().BeNull();

    [Fact]
    public async Task The_release_page_is_built_from_the_tag_and_the_fixed_repository()
    {
        var update = await Check("1.0.0", "v2.0.0");

        update!.ReleasePage.Should().Be(new Uri($"{DesktopEndpoints.PublicRepository}/releases/tag/v2.0.0"));
        // Never the html_url the payload offered.
        update.ReleasePage.Host.Should().Be("github.com");
        update.Installed.Should().Be("1.0.0");
        update.Latest.Should().Be("2.0.0");
    }

    [Theory]
    [InlineData("""{"tag_name": 17}""")]
    [InlineData("""{"name": "v2.0.0"}""")]
    [InlineData("""{"tag_name": null}""")]
    [InlineData("[]")]
    [InlineData("nonsense")]
    public async Task A_malformed_release_payload_says_nothing(string body)
        => (await new UpdateCheckService(Fetcher(FakeHttpHandler.Returning(HttpStatusCode.OK, body)))
            .TryCheckAsync("1.0.0", default)).Should().BeNull();

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task An_unavailable_release_endpoint_says_nothing(HttpStatusCode status)
        => (await new UpdateCheckService(Fetcher(FakeHttpHandler.Returning(status, string.Empty)))
            .TryCheckAsync("1.0.0", default)).Should().BeNull();

    [Fact]
    public async Task A_blocked_github_says_nothing()
        => (await new UpdateCheckService(Fetcher(FakeHttpHandler.Throwing(new HttpRequestException())))
            .TryCheckAsync("1.0.0", default)).Should().BeNull();
}
