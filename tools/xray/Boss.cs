using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClrXray;

/// <summary>
/// A boss fight. The reader is given a file with holes in it and a list of things to work out,
/// and a machine decides whether they got them.
/// </summary>
/// <remarks>
/// A chapter that ends with "try implementing this yourself" ends with nothing. The reader has no
/// way to know whether what they wrote is right, and the ones who most need to know are the ones
/// least able to tell. So every fight in this book is graded by a program, and the rule in
/// CONTRIBUTING that no boss fight is graded by a human is what this file exists to keep.
/// </remarks>
internal sealed class BossFight
{
    public string Title { get; init; } = string.Empty;

    /// <summary>What the reader is being asked to do, in one paragraph.</summary>
    public string Brief { get; init; } = string.Empty;

    public IReadOnlyList<BossQuestion> Questions { get; init; } = [];
}

internal sealed class BossQuestion
{
    /// <summary>The name the answer is printed under, and the name a failure is reported under.</summary>
    public string Key { get; init; } = string.Empty;

    public string Ask { get; init; } = string.Empty;
}

internal static class Boss
{
    internal const string Directory = "boss";

    private const string Stub = "boss.cs";
    private const string Solution = "solution.cs";
    private const string Answers = "answers.txt";
    private const string Prefix = "answer ";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    internal static bool Has(string lesson) => File.Exists(Path.Combine(lesson, Directory, "boss.json"));

    internal static BossFight Load(string lesson)
    {
        var path = Path.Combine(lesson, Directory, "boss.json");
        var fight = JsonSerializer.Deserialize<BossFight>(File.ReadAllText(path), Options)
            ?? throw new LessonException($"{path}: not a boss fight");

        if (fight.Questions.Count == 0)
        {
            throw new LessonException($"{path}: a boss fight with nothing to answer");
        }

        return fight;
    }

    /// <summary>
    /// Regenerates the answer file from the reference solution, and then proves the fight is a
    /// fight by running the stub and requiring it to lose.
    /// </summary>
    /// <remarks>
    /// That second check matters more than it looks. A stub that already passes is a boss fight
    /// with nothing in it, and the way that happens is not carelessness, it is a later edit to the
    /// solution that quietly makes the starting file correct. Nobody notices, because a green
    /// build looks the same either way.
    /// </remarks>
    internal static int Build(string lesson, bool write)
    {
        var name = Path.GetFileName(lesson);
        var fight = Load(lesson);
        var problems = 0;

        var solved = Answer(lesson, Solution);
        foreach (var question in fight.Questions)
        {
            if (!solved.ContainsKey(question.Key))
            {
                throw new LessonException($"{name}: the solution never printed an answer for '{question.Key}'");
            }
        }

        var file = new StringBuilder();
        foreach (var question in fight.Questions)
        {
            file.Append(question.Key).Append(' ').Append(Digest(question.Key, solved[question.Key])).Append('\n');
        }

        problems += Generated.Settle(Path.Combine(lesson, Directory, Answers), file.ToString(), write);

        var attempt = Answer(lesson, Stub);
        if (Wrong(fight, solved, attempt).Count == 0)
        {
            Console.Error.WriteLine($"{name}: the starting file already passes its own boss fight, so there is nothing to work out");
            problems++;
        }

        return problems;
    }

    /// <summary>
    /// Runs the reader's file and tells them what is wrong with it, without telling them the
    /// answer.
    /// </summary>
    internal static int Grade(string lesson)
    {
        if (!Has(lesson))
        {
            Console.Error.WriteLine($"xray boss: no boss fight in {lesson}");
            return 2;
        }

        var fight = Load(lesson);
        var expected = Expected(lesson);
        var attempt = Answer(lesson, Stub);

        Console.WriteLine(fight.Title);
        Console.WriteLine();

        var wrong = new List<string>();

        foreach (var question in fight.Questions)
        {
            if (!attempt.TryGetValue(question.Key, out var given))
            {
                Console.WriteLine($"  {question.Key}: nothing printed. {question.Ask}");
                wrong.Add(question.Key);
                continue;
            }

            if (!expected.TryGetValue(question.Key, out var digest))
            {
                throw new LessonException($"{lesson}: '{question.Key}' has no stored answer, run the build");
            }

            if (Digest(question.Key, given) == digest)
            {
                Console.WriteLine($"  {question.Key}: right");
                continue;
            }

            Console.WriteLine($"  {question.Key}: you printed '{given}', which is not it. {question.Ask}");
            wrong.Add(question.Key);
        }

        Console.WriteLine();

        if (wrong.Count == 0)
        {
            Console.WriteLine($"All {fight.Questions.Count} right. That is the fight won.");
            return 0;
        }

        Console.WriteLine($"{wrong.Count} of {fight.Questions.Count} still wrong: {string.Join(", ", wrong)}");
        return 1;
    }

    private static List<string> Wrong(BossFight fight, Dictionary<string, string> solved, Dictionary<string, string> attempt)
    {
        return fight.Questions
            .Where(question => !attempt.TryGetValue(question.Key, out var given) || given != solved[question.Key])
            .Select(question => question.Key)
            .ToList();
    }

    /// <summary>
    /// Runs one file of the boss directory and collects the lines that look like an answer. The
    /// working directory is the lesson rather than the boss directory, so that a path written in
    /// the fight is the same path the lesson uses.
    /// </summary>
    private static Dictionary<string, string> Answer(string lesson, string file)
    {
        var (exit, stdout, stderr) = Runner.Dotnet(lesson, ["run", Path.Combine(Directory, file)]);
        if (exit != 0)
        {
            throw new LessonException($"{Path.GetFileName(lesson)}: {file} exited with {exit}\n{stderr}");
        }

        var answers = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in stdout.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var equals = trimmed.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0)
            {
                throw new LessonException($"{Path.GetFileName(lesson)}: '{trimmed}' is not answer <name> = <value>");
            }

            answers[trimmed[Prefix.Length..equals].Trim()] = trimmed[(equals + 1)..].Trim();
        }

        return answers;
    }

    private static Dictionary<string, string> Expected(string lesson)
    {
        var path = Path.Combine(lesson, Directory, Answers);
        if (!File.Exists(path))
        {
            throw new LessonException($"{path}: missing, run the build");
        }

        var expected = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in File.ReadAllLines(path))
        {
            var space = line.IndexOf(' ', StringComparison.Ordinal);
            if (space > 0)
            {
                expected[line[..space]] = line[(space + 1)..].Trim();
            }
        }

        return expected;
    }

    /// <summary>
    /// The stored answer is a hash rather than the answer.
    /// </summary>
    /// <remarks>
    /// This is not security, and the worked solution sits in the same directory where anybody can
    /// read it. It is here so that the answer is not lying in plain sight in a generated file, in
    /// front of a reader who has not decided to look it up yet. The key is mixed in because a lot
    /// of answers are small numbers, and the hash of a small number is something you can look up
    /// once and then recognise on sight for the rest of the book.
    /// </remarks>
    private static string Digest(string key, string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{key}={value}"));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>
    /// Renders the fight onto the lesson page, so that the questions on the page and the questions
    /// the grader asks are the same list.
    /// </summary>
    internal static string Render(string lesson, BossFight fight)
    {
        var text = new StringBuilder();
        text.Append("## Boss fight: ").Append(fight.Title).Append("\n\n");
        text.Append(fight.Brief).Append("\n\n");

        foreach (var question in fight.Questions)
        {
            text.Append("- **").Append(question.Key).Append("** ").Append(question.Ask).Append('\n');
        }

        var path = Path.GetRelativePath(System.IO.Directory.GetCurrentDirectory(), lesson).Replace('\\', '/');

        text.Append("\nOpen `").Append(Directory).Append('/').Append(Stub).Append("`, fill in the parts marked as yours, and run this until it stops complaining.\n\n");
        text.Append("```\ndotnet run --project tools/xray -- boss ").Append(path).Append("\n```\n\n");
        text.Append("The grader names the answer that is wrong and shows you what you printed. It does not tell you what the answer is. The worked solution is in the same directory, and reading it is allowed and is a worse way to learn this.");

        return text.ToString();
    }
}
