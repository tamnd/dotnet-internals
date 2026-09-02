namespace ClrXray;

internal enum ShapeKind
{
    /// <summary>A thing that exists. Solid border, white fill.</summary>
    Box,

    /// <summary>A remark about a thing. Dashed border, tinted fill, no arrows into it.</summary>
    Note,

    /// <summary>One cell of a strip, which is how a layout of adjacent regions is drawn.</summary>
    Cell,
}

internal sealed class Shape
{
    internal required string Id { get; init; }

    internal required ShapeKind Kind { get; init; }

    internal required double X { get; init; }

    internal required double Y { get; init; }

    internal required double Width { get; init; }

    internal required double Height { get; init; }

    /// <summary>The first line is the heading. The rest are body lines under it.</summary>
    internal required IReadOnlyList<string> Lines { get; init; }

    internal double CentreX => X + (Width / 2);

    internal double CentreY => Y + (Height / 2);

    /// <summary>
    /// Whether a line is set as a heading. A box and a cell name a thing, so their first line is
    /// the name of it. A note is a remark, and a remark with its first line in bold reads like a
    /// banner rather than like something somebody said.
    /// </summary>
    internal bool IsHeading(int line) => line == 0 && Kind != ShapeKind.Note;
}

internal sealed class Connector
{
    internal required Shape From { get; init; }

    internal required Shape To { get; init; }

    internal string Label { get; init; } = string.Empty;
}

/// <summary>
/// A diagram, read from a <c>.dg</c> file. The file is the source. The SVG next to it is what a
/// page shows, and the Excalidraw file next to that is a convenience for anybody who wants to
/// sketch a variation, not an input to anything.
/// </summary>
internal sealed class Drawing
{
    internal string Title { get; set; } = string.Empty;

    internal double Width { get; set; } = 960;

    internal double Height { get; set; } = 540;

    internal List<Shape> Shapes { get; } = [];

    internal List<Connector> Connectors { get; } = [];
}
