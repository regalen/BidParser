namespace BidParser.Parsing.Tests;

internal static class TestSample
{
    public static string Root { get; } = FindRoot();

    public static string Path(string filename) =>
        System.IO.Path.Combine(Root, "samples", "inputs", filename);

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "BidParser.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
