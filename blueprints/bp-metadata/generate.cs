// The generator for BP-METADATA. Every block below prints one section of the blueprint as
// markdown, and xray writes what it printed into generated/ and pastes it into the page.
//
// Nothing here reads a file. The whole thing is derived from two enumerations and fifteen methods
// that System.Reflection.Metadata already ships, which is the point: the table inventory and the
// coded index encodings are the two places a metadata reader is most often subtly wrong, and both
// of them are knowable from the library rather than from somebody typing out Partition II.
//
// Run it on its own to see what it produces:
//
//     dotnet run generate.cs

//# block id=usings capture=none
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
//# end

//# block id=tables
// Every table the library knows about, in table number order. The number is not decoration: it is
// the top byte of every token that points into the table, so this list is also the list of token
// types.
Console.WriteLine("| Number | Hex | Table | Token type |");
Console.WriteLine("|---|---|---|---|");

foreach (var table in Enum.GetValues<TableIndex>().OrderBy(t => (int)t))
{
    var number = (int)table;
    Console.WriteLine($"| {number} | 0x{number:X2} | {table} | 0x{number:X2}000000 |");
}
//# end

//# block id=coded
// A coded index is a row number with a small tag squeezed into its low bits, so that one column
// can point into several tables. Getting the tag values or the tag width wrong produces a reader
// that is wrong about everything downstream and fails a long way from here.
//
// The library will encode a handle from any table a given coded index accepts and refuses the
// rest, which is enough to recover the whole encoding without being told it. Encode row one from
// every table in turn: the result is (1 << bits) | tag, so the position of the top set bit is the
// tag width and what is under it is the tag.
var indexes = new SortedDictionary<string, SortedDictionary<int, string>>(StringComparer.Ordinal);
var widths = new SortedDictionary<string, int>(StringComparer.Ordinal);

foreach (var method in typeof(CodedIndex).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
{
    var tags = new SortedDictionary<int, string>();
    var bits = -1;

    foreach (var table in Enum.GetValues<TableIndex>().OrderBy(t => (int)t))
    {
        int encoded;
        try
        {
            encoded = (int)method.Invoke(null, [MetadataTokens.EntityHandle(table, 1)])!;
        }
        catch (TargetInvocationException error) when (error.InnerException is ArgumentException)
        {
            // This coded index cannot point at this table, which is most of the pairs.
            continue;
        }

        var width = BitOperations.Log2((uint)encoded);
        if (bits >= 0 && bits != width)
        {
            throw new InvalidOperationException($"{method.Name} encoded two tables at two different tag widths");
        }

        bits = width;
        tags[encoded & ((1 << width) - 1)] = table.ToString();
    }

    indexes[method.Name] = tags;
    widths[method.Name] = bits;
}

// The library splits one coded index into a narrow entry point and a wide one, so that a caller
// who means to reject a TypeSpec can say so in the type system instead of in an if. That is a
// library convenience and not a second encoding, and the two are always named as a prefix and the
// same prefix with more on the end. Fold the narrow one into the wide one, which is the whole of
// the difference between what the library exposes and what the format defines.
var aliases = new List<(string Narrow, string Wide)>();

foreach (var narrow in indexes.Keys)
{
    foreach (var wide in indexes.Keys)
    {
        if (wide.Length > narrow.Length && wide.StartsWith(narrow, StringComparison.Ordinal) && widths[wide] == widths[narrow])
        {
            aliases.Add((narrow, wide));
        }
    }
}

foreach (var (narrow, wide) in aliases)
{
    foreach (var (tag, table) in indexes[wide])
    {
        indexes[narrow][tag] = table;
    }

    indexes.Remove(wide);
    widths.Remove(wide);
}

Console.WriteLine("| Coded index | Tag bits | Tags assigned | Two byte index while no target table is longer than |");
Console.WriteLine("|---|---|---|---|");

foreach (var (name, tags) in indexes)
{
    var bits = widths[name];
    var room = (1 << (16 - bits)) - 1;
    Console.WriteLine($"| {name} | {bits} | {tags.Count} of {1 << bits} | {room.ToString("N0", CultureInfo.InvariantCulture)} rows |");
}

foreach (var (name, tags) in indexes)
{
    var bits = widths[name];
    var free = Enumerable.Range(0, 1 << bits).Where(tag => !tags.ContainsKey(tag)).ToList();

    Console.WriteLine();
    Console.WriteLine($"#### {name}");
    Console.WriteLine();
    Console.WriteLine("| Tag | Table |");
    Console.WriteLine("|---|---|");

    foreach (var (tag, table) in tags)
    {
        Console.WriteLine($"| {tag} | {table} |");
    }

    Console.WriteLine();
    Console.WriteLine(free.Count == 0
        ? $"Every one of the {1 << bits} tag values is assigned, so no tag in this column is malformed on its own."
        : $"{(free.Count == 1 ? "Tag" : "Tags")} {English(Runs(free))} of the {1 << bits} {(free.Count == 1 ? "is" : "are")} not assigned, and a column of this kind carrying {(free.Count == 1 ? "it" : "one of them")} is malformed.");
}
//# end

//# block id=helpers capture=none
// Ten unassigned tags listed one at a time is ten numbers nobody reads. A run of three or more
// becomes a range, which is how somebody would say it out loud.
static List<string> Runs(List<int> numbers)
{
    var runs = new List<string>();
    var at = 0;

    while (at < numbers.Count)
    {
        var end = at;
        while (end + 1 < numbers.Count && numbers[end + 1] == numbers[end] + 1)
        {
            end++;
        }

        if (end - at >= 2)
        {
            runs.Add($"{numbers[at]} through {numbers[end]}");
            at = end + 1;
            continue;
        }

        for (var i = at; i <= end; i++)
        {
            runs.Add(numbers[i].ToString(CultureInfo.InvariantCulture));
        }

        at = end + 1;
    }

    return runs;
}

static string English(IEnumerable<string> items)
{
    var all = items.ToList();
    return all.Count switch
    {
        0 => string.Empty,
        1 => all[0],
        _ => string.Join(", ", all.Take(all.Count - 1)) + " and " + all[^1],
    };
}
//# end
