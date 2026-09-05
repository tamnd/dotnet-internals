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

    /// <summary>
    /// The top of the repository, found by walking up from a path until the solution file turns
    /// up. Falls back to the working directory, which is what a lesson copied out of the tree for
    /// a test has.
    /// </summary>
    /// <remarks>
    /// Anything that ends up inside a generated file has to be relative to this rather than to the
    /// working directory. A page that says <c>xray boss lessons/smoke-pipeline</c> when the tool is
    /// run from the root and <c>xray boss ../../lessons/smoke-pipeline</c> when it is run from
    /// somewhere else is a page whose contents depend on where somebody was standing, and the way
    /// that shows up is a build that fails for no reason a person can see.
    /// </remarks>
    internal static string Root(string from)
    {
        var directory = Directory.Exists(from)
            ? new DirectoryInfo(Path.GetFullPath(from))
            : new FileInfo(Path.GetFullPath(from)).Directory;

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ClrXray.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

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
