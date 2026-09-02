using System.Text;
using System.Text.Json;

namespace ClrXray;

/// <summary>
/// A prediction gate. The reader is asked what will happen before the output is shown to them,
/// and the answer is folded away until they have picked one.
/// </summary>
/// <remarks>
/// The point of a gate is not the question. It is that a reader who guesses wrong now has a
/// reason to read the next paragraph, and a reader who guesses right has just found out that
/// they already knew something. A lesson that prints its output under its code with nothing in
/// between teaches neither of those.
/// </remarks>
internal sealed class Gate
{
    public string Id { get; init; } = string.Empty;

    public string Question { get; init; } = string.Empty;

    public IReadOnlyList<GateOption> Options { get; init; } = [];

    /// <summary>One paragraph shown after the answer, for the thing the answer does not say.</summary>
    public string After { get; init; } = string.Empty;
}

internal sealed class GateOption
{
    public string Text { get; init; } = string.Empty;

    public bool Correct { get; init; }

    public string Why { get; init; } = string.Empty;
}

internal static class Gates
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Loads <c>gates.json</c> from a lesson directory. A lesson with no gates file has no gates,
    /// which is allowed for a lesson that is all reference and no discovery.
    /// </summary>
    internal static IReadOnlyDictionary<string, Gate> Load(string directory)
    {
        var path = Path.Combine(directory, "gates.json");
        if (!File.Exists(path))
        {
            return new Dictionary<string, Gate>(StringComparer.Ordinal);
        }

        var gates = JsonSerializer.Deserialize<List<Gate>>(File.ReadAllText(path), Options)
            ?? throw new LessonException($"{path}: not a list of gates");

        var byId = new Dictionary<string, Gate>(StringComparer.Ordinal);
        foreach (var gate in gates)
        {
            if (string.IsNullOrEmpty(gate.Id))
            {
                throw new LessonException($"{path}: a gate has no id");
            }

            if (gate.Options.Count(o => o.Correct) != 1)
            {
                throw new LessonException($"{path}: gate '{gate.Id}' needs exactly one correct option");
            }

            if (!byId.TryAdd(gate.Id, gate))
            {
                throw new LessonException($"{path}: duplicate gate id '{gate.Id}'");
            }
        }

        return byId;
    }

    /// <summary>
    /// Renders a gate as markdown that works on a plain GitHub page, with no script and no site
    /// build. The answer is inside a details element, so the fold is the browser's own and it
    /// still folds in a diff view.
    /// </summary>
    internal static string Render(Gate gate)
    {
        var text = new StringBuilder();
        text.Append("**Predict before you run it.** ").Append(gate.Question).Append("\n\n");

        for (var i = 0; i < gate.Options.Count; i++)
        {
            text.Append("- **").Append(Label(i)).Append(".** ").Append(gate.Options[i].Text).Append('\n');
        }

        text.Append("\n<details>\n<summary>Show the answer once you have picked one</summary>\n\n");

        for (var i = 0; i < gate.Options.Count; i++)
        {
            var option = gate.Options[i];
            var verdict = option.Correct ? "is right" : "is wrong";
            text.Append("**").Append(Label(i)).Append(' ').Append(verdict).Append(".** ").Append(option.Why).Append("\n\n");
        }

        if (gate.After.Length > 0)
        {
            text.Append(gate.After).Append("\n\n");
        }

        text.Append("</details>");
        return text.ToString();
    }

    private static char Label(int index) => (char)('A' + index);
}
