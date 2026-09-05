namespace ClrXray;

/// <summary>
/// Proves the citation gate can go red.
/// </summary>
/// <remarks>
/// <para>
/// A gate that has never rejected anything is a gate nobody has tested, and this one is in an
/// awkward position: <c>pin.json</c> still holds a null commit, so the repository contains no
/// citations, so on a normal run the checker looks at nothing and passes. That is exactly the
/// shape of a check that quietly does not work.
/// </para>
/// <para>
/// So the self test carries its own pin, at two commits that are real and will never move, and
/// runs both halves of the checker against it: the parsing, which is offline, and the resolution,
/// which really does fetch two files out of <c>dotnet/runtime</c> and <c>dotnet/roslyn</c>. Every
/// case that should fail is asserted to fail with a message that says why, because a rejection
/// with a useless message is only half a gate.
/// </para>
/// </remarks>
internal static class CiteSelfTest
{
    // Two commits picked because they are tagged releases and are therefore never going to be
    // rewritten. Note that neither one is the hash the tag itself has: both repositories use
    // annotated tags, so the tag object and the commit have different hashes, and resolving the
    // tag name would have given a hash that raw.githubusercontent.com answers 404 for. That is the
    // whole argument for pinning by commit, and it turned up while writing this file.
    private const string RuntimeCommit = "60629d14374c56f1cb51819049ad1fa529307f8d";
    private const string RoslynCommit = "871ef6369443071681de3351d30f41ea78ab48e6";

    private const string RuntimeFile = "src/coreclr/vm/methodtable.h";
    private const string RoslynFile = "src/Compilers/Core/Portable/PEWriter/MetadataWriter.cs";

    private static readonly Pin Landed = new(
    [
        new PinnedRepo("runtime", "dotnet/runtime", RuntimeCommit),
        new PinnedRepo("roslyn", "dotnet/roslyn", RoslynCommit),
    ]);

    private static readonly Pin NotLanded = new(
    [
        new PinnedRepo("runtime", "dotnet/runtime", null),
        new PinnedRepo("roslyn", "dotnet/roslyn", null),
    ]);

    private static int failures;

    internal static int Run()
    {
        failures = 0;

        try
        {
            Parsing();
            Scanning();
            Resolution();
        }
        catch (CiteException error)
        {
            Console.Error.WriteLine($"xray cite --selftest: {error.Message}");
            return 2;
        }

        Console.WriteLine($"xray cite --selftest: {failures} failure(s)");
        return failures == 0 ? 0 : 1;
    }

    private static void Parsing()
    {
        Console.WriteLine("parsing");

        Good($"runtime:{RuntimeFile}:910@60629d1", "a line");
        Good($"runtime:{RuntimeFile}:909-911@60629d1", "a range");
        Good($"runtime:{RuntimeFile}@60629d1", "a whole file");
        Good($"runtime:{RuntimeFile}:910@60629d1#class MethodTable", "a line with an expectation");
        Good($"roslyn:{RoslynFile}:39@871ef63", "the other repository");

        Bad($"runtime:{RuntimeFile}:910", "no commit", "a citation with no commit");
        Bad($"runtime:{RuntimeFile}:910@main", "not a commit", "a citation pinned to a branch");
        Bad($"runtime:{RuntimeFile}:910@v10.0.0", "not a commit", "a citation pinned to a tag");
        Bad($"runtime:{RuntimeFile}:0@60629d1", "start at one", "line zero");
        Bad($"runtime:{RuntimeFile}:20-10@60629d1", "ends before it starts", "a backwards range");
        Bad($"runtime:{RuntimeFile}:x@60629d1", "not a line", "a line that is not a number");
        Bad("runtime:/src/x.h:1@60629d1", "does not start with a slash", "an absolute path");
        Bad(@"runtime:src\coreclr\x.h:1@60629d1", "forward slash", "a Windows path");
        Bad("runtime:../x.h:1@60629d1", "walks upwards", "a path leaving the repository");
        Bad("runtime:@60629d1", "no path", "no path at all");
        Bad($"runtime:{RuntimeFile}:910@60629d1#", "empty expectation", "a hash with nothing after it");
    }

    /// <summary>
    /// The scanner, on a page written for the purpose. What matters here as much as finding the
    /// citation is not finding the two that are examples rather than claims.
    /// </summary>
    private static void Scanning()
    {
        Console.WriteLine("scanning");

        var page = Path.Combine(Path.GetTempPath(), $"xray-cite-selftest-{Environment.ProcessId}.md");
        File.WriteAllText(page, string.Join('\n',
        [
            "# A page",
            "",
            $"The layout starts at `runtime:{RuntimeFile}:910@60629d1#class MethodTable` and runs on.",
            "",
            "```",
            $"runtime:{RuntimeFile}:1@deadbeef",
            "```",
            "",
            $"A citation looks like `runtime:some/path.h:1@abc1234`. <!-- {Citations.Allow} -->",
            "",
            "Nothing in this sentence is a citation, and `dotnet build` is not one either.",
            "",
        ]));

        try
        {
            var errors = new List<string>();
            var found = Citations.Scan(page, Landed, errors);

            Check(errors.Count == 0, "a page with one good citation reports no errors", string.Join("; ", errors));
            Check(found.Count == 1, "a page with one good citation finds exactly one", $"found {found.Count}");

            if (found.Count == 1)
            {
                Check(found[0].First == 910, "the scanner keeps the line number", $"got {found[0].First}");
                Check(found[0].SourceLine == 3, "the scanner keeps the line of the page", $"got {found[0].SourceLine}");
                Check(found[0].Expect == "class MethodTable", "the scanner keeps the expectation", $"got {found[0].Expect}");
            }

            var broken = Path.Combine(Path.GetTempPath(), $"xray-cite-selftest-broken-{Environment.ProcessId}.md");
            File.WriteAllText(broken, $"A claim about `runtime:{RuntimeFile}:910` and nothing more.\n");
            try
            {
                var alsoErrors = new List<string>();
                Citations.Scan(broken, Landed, alsoErrors);
                Check(alsoErrors.Count == 1, "a malformed citation on a page is reported rather than ignored", $"reported {alsoErrors.Count}");
            }
            finally
            {
                File.Delete(broken);
            }
        }
        finally
        {
            File.Delete(page);
        }
    }

    /// <summary>
    /// The half that needs the network. If it cannot reach GitHub it stops the command with a
    /// message about the network rather than reporting failures about citations.
    /// </summary>
    private static void Resolution()
    {
        Console.WriteLine("resolution, against dotnet/runtime and dotnet/roslyn over the network");

        var source = new Source { Cache = Path.Combine(Path.GetTempPath(), "xray-cite-selftest-cache") };

        Resolves($"runtime:{RuntimeFile}:910@60629d1#class MethodTable", Landed, source, "the class the whole book is about");
        Resolves($"runtime:{RuntimeFile}:909-911@60629d1#class MethodTable", Landed, source, "a range around it");
        Resolves($"runtime:{RuntimeFile}@60629d1", Landed, source, "a whole file");
        Resolves($"roslyn:{RoslynFile}:39@871ef63#MetadataWriter", Landed, source, "the other repository");

        Refuses($"runtime:{RuntimeFile}:99999@60629d1", Landed, source, "past the end", "a line past the end of the file");
        Refuses($"runtime:{RuntimeFile}:910@60629d1#class FieldDesc", Landed, source, "contains", "a line that does not say what the citation claims");
        Refuses("roslyn:src/Compilers/There/Is/No/Such/File.cs:1@871ef63", Landed, source, "no such path", "a path that is not there at that commit");
        Refuses($"runtime:{RuntimeFile}:910@deadbee", Landed, source, "pinned at", "a citation at a commit that is not the pin");
        Refuses($"runtime:{RuntimeFile}:910@60629d1", NotLanded, source, "has not landed", "any citation while the pin is null");
        Refuses($"sdk:{RuntimeFile}:910@60629d1", Landed, source, "not a pinned repository", "a repository nobody pinned");
    }

    private static void Good(string span, string what)
    {
        var citation = Citations.Parse(span, "selftest", 1, out var error);
        Check(citation is not null, $"parses {what}", error ?? string.Empty);
    }

    private static void Bad(string span, string fragment, string what)
    {
        var citation = Citations.Parse(span, "selftest", 1, out var error);
        if (citation is not null)
        {
            Check(false, $"rejects {what}", "it was accepted");
            return;
        }

        Check(
            error is not null && error.Contains(fragment, StringComparison.Ordinal),
            $"rejects {what}",
            $"the message was '{error}', which does not mention '{fragment}'");
    }

    private static void Resolves(string span, Pin pin, Source source, string what)
    {
        var citation = Citations.Parse(span, "selftest", 1, out var parseError);
        if (citation is null)
        {
            Check(false, $"resolves {what}", $"it did not even parse: {parseError}");
            return;
        }

        var line = Cite.Resolve(citation, pin, source, out var error);
        Check(line is not null, $"resolves {what}", error ?? string.Empty);
    }

    private static void Refuses(string span, Pin pin, Source source, string fragment, string what)
    {
        var citation = Citations.Parse(span, "selftest", 1, out var parseError);
        if (citation is null)
        {
            Check(false, $"refuses {what} at resolution", $"it was rejected earlier, while parsing: {parseError}");
            return;
        }

        var line = Cite.Resolve(citation, pin, source, out var error);
        if (line is not null)
        {
            Check(false, $"refuses {what}", $"it resolved to: {line}");
            return;
        }

        Check(
            error is not null && error.Contains(fragment, StringComparison.Ordinal),
            $"refuses {what}",
            $"the message was '{error}', which does not mention '{fragment}'");
    }

    private static void Check(bool passed, string what, string why)
    {
        if (passed)
        {
            Console.WriteLine($"  ok    {what}");
            return;
        }

        failures++;
        Console.Error.WriteLine($"  FAIL  {what}: {why}");
    }
}
