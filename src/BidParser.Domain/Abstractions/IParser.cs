using BidParser.Domain.Models;

namespace BidParser.Domain.Abstractions;

public interface IParser
{
    string Slug { get; }
    string DisplayName { get; }
    string Vendor { get; }
    string AcceptedMime { get; }
    string CrmTemplate { get; }

    ParseResult Parse(string path);

    double Detect(string path) => 0.0;
}
