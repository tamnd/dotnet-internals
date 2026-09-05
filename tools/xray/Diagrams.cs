namespace ClrXray;

/// <summary>
/// Turns every diagram source under a path into the picture a page shows and the scene somebody
/// can open in Excalidraw.
/// </summary>
/// <remarks>
/// A picture committed next to a page it no longer matches is the same defect as a number typed
/// by hand, and it is harder to spot because nobody reads a picture twice. So a diagram has a
/// source file, the two outputs are generated from it, and the check that regenerates them is the
/// check that fails the build.
/// </remarks>
internal static class Diagrams
{
    /// <summary>
    /// Step four for a picture. A diagram has nothing to execute, so it turns up whole in the
    /// generate step and the two files it produces go into the plan with everything else.
    /// </summary>
    internal static void Generate(string source, Plan plan)
    {
        var name = Path.GetFileName(source);
        var drawing = DrawingReader.Read(source);
        var stem = Path.Combine(Path.GetDirectoryName(source)!, Path.GetFileNameWithoutExtension(source));

        Fits(name, drawing, plan);

        plan.Add(stem + ".svg", DrawingSvg.Write(drawing, name));
        plan.Add(stem + ".excalidraw", DrawingExcalidraw.Write(drawing, name));
    }

    /// <summary>
    /// Catches the two ways a diagram is wrong in a way nobody notices in review: a line of text
    /// wider than the box it sits in, and a shape hanging off the edge of the canvas.
    /// </summary>
    /// <remarks>
    /// The width of a string is estimated rather than measured, because measuring it would mean
    /// shipping a font with the tool. The estimate is generous, so a diagram that trips this is a
    /// diagram that really does overflow, and the fix is a shorter line rather than a bigger
    /// coefficient.
    /// </remarks>
    private static void Fits(string name, Drawing drawing, Plan plan)
    {
        foreach (var shape in drawing.Shapes)
        {
            if (shape.X < 0 || shape.Y < 0 || shape.X + shape.Width > drawing.Width || shape.Y + shape.Height > drawing.Height)
            {
                plan.Problem($"{name}: '{shape.Id}' hangs off the canvas, which is {drawing.Width} by {drawing.Height}");
            }

            var cell = shape.Kind == ShapeKind.Cell;

            for (var i = 0; i < shape.Lines.Count; i++)
            {
                var heading = shape.IsHeading(i);
                var size = (cell, heading) switch
                {
                    (true, true) => 13.0,
                    (true, false) => 11.0,
                    (false, true) => 15.0,
                    (false, false) => 13.0,
                };

                var estimate = shape.Lines[i].Length * size * (heading ? 0.60 : 0.55);
                if (estimate > shape.Width - 16)
                {
                    plan.Problem($"{name}: '{shape.Id}' line {i + 1} is wider than the box, shorten it or widen the box");
                }
            }
        }
    }

    internal static List<string> Discover(string path)
    {
        if (File.Exists(path))
        {
            return [Path.GetFullPath(path)];
        }

        if (!Directory.Exists(path))
        {
            return [];
        }

        return Directory.EnumerateFiles(path, "*.dg", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}
