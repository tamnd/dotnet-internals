namespace ClrXray;

/// <summary>
/// Walking the repository. Two commands want every markdown file under a directory and they want
/// the same answer, so the list of things that are not part of the book lives here rather than in
/// both of them.
/// </summary>
internal static class Files
{
    private static readonly string[] SkipDirectories =
    [
        ".git", "bin", "obj", "vendor", "node_modules", "artifacts", "build",
    ];

    internal static IEnumerable<string> Markdown(string root)
    {
        foreach (var path in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path);
            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!parts.Any(part => SkipDirectories.Contains(part, StringComparer.Ordinal)))
            {
                yield return path;
            }
        }
    }
}
