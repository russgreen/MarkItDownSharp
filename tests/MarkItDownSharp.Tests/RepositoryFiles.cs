namespace MarkItDownSharp.Tests;

/// <summary>Locates files in the repository so tests can validate packaging and UI definitions.</summary>
internal static class RepositoryFiles
{
    private static readonly Lazy<string> RootPath = new(FindRoot);

    public static string Root => RootPath.Value;

    public static string Path(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "MarkItDownSharp.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root (MarkItDownSharp.slnx) was not found above the test output folder.");
    }
}
