namespace BidParser.Api.Tests;

internal sealed class ScopedEnvironment : IDisposable
{
    private readonly Dictionary<string, string?> _previousValues;

    public ScopedEnvironment(IReadOnlyDictionary<string, string> values)
    {
        _previousValues = values.Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);

        foreach (var (key, value) in values)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    public void Dispose()
    {
        foreach (var (key, value) in _previousValues)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
