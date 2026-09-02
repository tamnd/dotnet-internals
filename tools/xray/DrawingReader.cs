using System.Globalization;

namespace ClrXray;

/// <summary>
/// Reads the diagram source format. It is line based and small on purpose, because the point of
/// having a source format at all is that a picture in this book is reviewable in a diff and
/// regenerable when the pin moves.
/// </summary>
internal static class DrawingReader
{
    internal static Drawing Read(string path)
    {
        var drawing = new Drawing();
        var byId = new Dictionary<string, Shape>(StringComparer.Ordinal);
        var connectors = new List<(string From, string To, string Label, int Line)>();
        var lines = File.ReadAllLines(path);

        for (var i = 0; i < lines.Length; i++)
        {
            var words = Tokenise(path, i, lines[i]);
            if (words.Count == 0)
            {
                continue;
            }

            switch (words[0])
            {
                case "title":
                    drawing.Title = string.Join(' ', words.Skip(1));
                    break;

                case "size":
                    Expect(path, i, words, 3);
                    drawing.Width = Number(path, i, words[1]);
                    drawing.Height = Number(path, i, words[2]);
                    break;

                case "box":
                case "note":
                    AddShape(path, i, words, byId, drawing);
                    break;

                case "strip":
                    AddStrip(path, i, words, byId, drawing);
                    break;

                case "arrow":
                    Expect(path, i, words, 3);
                    connectors.Add((words[1], words[2], words.Count > 3 ? words[3] : string.Empty, i));
                    break;

                default:
                    throw new LessonException($"{path}:{i + 1}: '{words[0]}' is not a diagram word");
            }
        }

        foreach (var (from, to, label, line) in connectors)
        {
            drawing.Connectors.Add(new Connector
            {
                From = Find(path, line, byId, from),
                To = Find(path, line, byId, to),
                Label = label,
            });
        }

        if (drawing.Shapes.Count == 0)
        {
            throw new LessonException($"{path}: a diagram with no shapes in it");
        }

        return drawing;
    }

    private static void AddShape(string path, int line, List<string> words, Dictionary<string, Shape> byId, Drawing drawing)
    {
        Expect(path, line, words, 7);
        var shape = new Shape
        {
            Id = words[1],
            Kind = words[0] == "note" ? ShapeKind.Note : ShapeKind.Box,
            X = Number(path, line, words[2]),
            Y = Number(path, line, words[3]),
            Width = Number(path, line, words[4]),
            Height = Number(path, line, words[5]),
            Lines = words.Skip(6).ToList(),
        };

        Register(path, line, byId, drawing, shape);
    }

    /// <summary>
    /// A strip is a run of equal cells laid left to right, which is how a layout of adjacent
    /// regions gets drawn without anybody adding up offsets by hand. Cell text carries an
    /// optional second line after a vertical bar.
    /// </summary>
    private static void AddStrip(string path, int line, List<string> words, Dictionary<string, Shape> byId, Drawing drawing)
    {
        Expect(path, line, words, 7);
        var x = Number(path, line, words[2]);
        var y = Number(path, line, words[3]);
        var width = Number(path, line, words[4]);
        var height = Number(path, line, words[5]);

        for (var cell = 0; cell + 6 < words.Count; cell++)
        {
            var text = words[cell + 6].Split('|', StringSplitOptions.TrimEntries);
            var shape = new Shape
            {
                Id = $"{words[1]}.{cell}",
                Kind = ShapeKind.Cell,
                X = x + (cell * width),
                Y = y,
                Width = width,
                Height = height,
                Lines = text,
            };

            Register(path, line, byId, drawing, shape);
        }
    }

    private static void Register(string path, int line, Dictionary<string, Shape> byId, Drawing drawing, Shape shape)
    {
        if (!byId.TryAdd(shape.Id, shape))
        {
            throw new LessonException($"{path}:{line + 1}: two shapes called '{shape.Id}'");
        }

        drawing.Shapes.Add(shape);
    }

    private static Shape Find(string path, int line, Dictionary<string, Shape> byId, string id)
    {
        if (!byId.TryGetValue(id, out var shape))
        {
            throw new LessonException($"{path}:{line + 1}: no shape called '{id}'");
        }

        return shape;
    }

    /// <summary>
    /// Splits a line into words, keeping anything inside double quotes together. A line whose
    /// first character is a hash is a comment, and a comment is how the author of a diagram
    /// explains a coordinate they chose by eye.
    /// </summary>
    private static List<string> Tokenise(string path, int line, string text)
    {
        var words = new List<string>();
        var word = new System.Text.StringBuilder();
        var quoted = false;

        if (text.TrimStart().StartsWith('#'))
        {
            return words;
        }

        foreach (var c in text)
        {
            if (c == '"')
            {
                quoted = !quoted;
                if (!quoted)
                {
                    words.Add(word.ToString());
                    word.Clear();
                }

                continue;
            }

            if (!quoted && char.IsWhiteSpace(c))
            {
                if (word.Length > 0)
                {
                    words.Add(word.ToString());
                    word.Clear();
                }

                continue;
            }

            word.Append(c);
        }

        if (quoted)
        {
            throw new LessonException($"{path}:{line + 1}: a quote is opened and never closed");
        }

        if (word.Length > 0)
        {
            words.Add(word.ToString());
        }

        return words;
    }

    private static void Expect(string path, int line, List<string> words, int least)
    {
        if (words.Count < least)
        {
            throw new LessonException($"{path}:{line + 1}: '{words[0]}' wants at least {least - 1} things after it");
        }
    }

    private static double Number(string path, int line, string word)
    {
        if (!double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            throw new LessonException($"{path}:{line + 1}: '{word}' is not a number");
        }

        return value;
    }
}
