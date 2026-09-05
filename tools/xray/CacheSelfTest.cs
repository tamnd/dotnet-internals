using System.Text;

namespace ClrXray;

/// <summary>
/// Proves the cache hands back what was put in it, keeps things apart that ought to be apart, and
/// refuses anything that has changed underneath it.
/// </summary>
/// <remarks>
/// <para>
/// A cache is the one place in this repository where a program writes files nobody is going to
/// look at, and then reads them back and believes them. Every other claim here is checked by
/// regenerating the thing and comparing, and a cache exists precisely so that the thing does not
/// get regenerated. So it needs its own proof, and the proof has to include the case that goes
/// wrong quietly: an entry that is still sitting there and is no longer what arrived.
/// </para>
/// <para>
/// None of this touches the network. Everything below stores bytes made up on the spot in a
/// directory under the temporary folder, which is also what makes it safe to run in CI on four
/// platforms without fetching anything.
/// </para>
/// </remarks>
internal static class CacheSelfTest
{
    private static int failures;

    internal static int Run()
    {
        failures = 0;

        var root = Path.Combine(Path.GetTempPath(), $"xray-cache-selftest-{Environment.ProcessId}");

        try
        {
            var cache = new Cache { Root = root };

            RoundTrip(cache);
            Apart(cache);
            Altered(cache);
            Provenance(cache);
            Escapes();
            Empty(cache);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        Console.WriteLine($"xray cache --selftest: {failures} failure(s)");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>The control. A cache that lost everything would pass every case below this one.</summary>
    private static void RoundTrip(Cache cache)
    {
        var key = new Key("dotnet/runtime", "abc123", "linux-x64", "checked", "libclrjit.so");
        cache.Store(key, Bytes("the bytes that arrived"), "https://example.invalid/one");

        Check(
            Text(cache.Read(key)) == "the bytes that arrived",
            "hands back what was put in it",
            "what came out was not what went in");

        Check(
            cache.Read(new Key("dotnet/runtime", "abc123", "linux-x64", "checked", "not-fetched.so")) is null,
            "says nothing for something it does not have",
            "it answered for a key nothing was ever stored under");
    }

    /// <summary>
    /// The reason the key has four parts. A checked and a release build of the same commit on the
    /// same platform have the same file name and are different programs, and a cache that mixed
    /// them up would produce a lesson whose output is right for a runtime nobody was using.
    /// </summary>
    private static void Apart(Cache cache)
    {
        var axes = new (string What, Key Key)[]
        {
            ("repository", new Key("dotnet/roslyn", "aaa", "linux-x64", "release", "same-name")),
            ("tag", new Key("dotnet/runtime", "bbb", "linux-x64", "release", "same-name")),
            ("platform", new Key("dotnet/runtime", "aaa", "win-x64", "release", "same-name")),
            ("configuration", new Key("dotnet/runtime", "aaa", "linux-x64", "checked", "same-name")),
        };

        var first = new Key("dotnet/runtime", "aaa", "linux-x64", "release", "same-name");
        cache.Store(first, Bytes("the first one"), "https://example.invalid/first");

        foreach (var (what, key) in axes)
        {
            cache.Store(key, Bytes("differs by " + what), "https://example.invalid/" + what);

            Check(
                Text(cache.Read(first)) == "the first one",
                $"keeps two things apart when only the {what} differs",
                $"storing something that differs only by {what} overwrote the first one");
        }
    }

    /// <summary>
    /// The case that goes wrong quietly. Something edits a cached file, and from then on every run
    /// on that machine reads the edit and believes it.
    /// </summary>
    private static void Altered(Cache cache)
    {
        var key = new Key("dotnet/runtime", "ddd", "any", "any", "src/coreclr/vm/methodtable.h");
        cache.Store(key, Bytes("what the repository actually says"), "https://example.invalid/two");

        File.WriteAllText(cache.Where(key), "what somebody would rather it said");

        var error = new StringWriter();
        var was = Console.Error;
        Console.SetError(error);

        byte[]? read;
        try
        {
            read = cache.Read(key);
        }
        finally
        {
            Console.SetError(was);
        }

        Check(
            read is null,
            "refuses an entry that has been altered since it was stored",
            "it handed back the altered bytes, so the hash beside each entry is decoration");

        Check(
            error.ToString().Contains("changed since it was cached", StringComparison.Ordinal),
            "says why it refused the altered entry",
            $"it refused without saying why, and said: {error.ToString().Trim()}");

        Check(
            !File.Exists(cache.Where(key)),
            "throws away an entry it has refused",
            "the altered file is still there, so the next run refuses it again rather than refetching");
    }

    /// <summary>
    /// A binary in a directory with no record of where it came from is the thing this repository
    /// refuses everywhere else, and a cache is not an exception to that.
    /// </summary>
    private static void Provenance(Cache cache)
    {
        var key = new Key("dotnet/runtime", "eee", "osx-arm64", "checked", "libclrjit.dylib");
        cache.Store(key, Bytes("a small pretend jit"), "https://example.invalid/jit");

        var entry = cache.Entries().FirstOrDefault(e => e.Name == "libclrjit.dylib" && e.Tag == "eee");

        Check(
            entry is not null && entry.From == "https://example.invalid/jit",
            "records where each thing came from",
            "an entry came back with no address on it");

        Check(
            entry is not null && entry.Bytes == 19 && entry.Sha256.Length == 64,
            "records the size and the hash of what arrived",
            $"the record says {entry?.Bytes} byte(s) and a hash of {entry?.Sha256.Length} characters");
    }

    /// <summary>
    /// A key is turned into directory names, so a name with a climb in it would put a file
    /// somewhere on the machine that has nothing to do with the cache.
    /// </summary>
    private static void Escapes()
    {
        foreach (var name in new[] { "../../escaped.txt", "..", string.Empty })
        {
            var refused = false;

            try
            {
                new Key("dotnet/runtime", "fff", "any", "any", name).Sound();
            }
            catch (LessonException)
            {
                refused = true;
            }

            Check(
                refused,
                $"refuses a key whose name is '{name}'",
                "it accepted a name that does not stay inside the cache");
        }
    }

    private static void Empty(Cache cache)
    {
        var before = cache.Entries().Count();
        var removed = cache.Clear();

        Check(
            removed == before && before > 0,
            "empties itself and says how much it removed",
            $"it had {before} entr(ies) and said it removed {removed}");

        Check(
            !cache.Entries().Any(),
            "is empty after being emptied",
            "something survived");
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static string? Text(byte[]? content) => content is null ? null : Encoding.UTF8.GetString(content);

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
