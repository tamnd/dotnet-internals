using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ClrXray;

/// <summary>
/// Writes an Excalidraw scene next to the SVG.
/// </summary>
/// <remarks>
/// This file is an export and not an input. The <c>.dg</c> file is the source, and editing the
/// scene by hand produces changes that the next build throws away. It is written anyway because
/// a picture you can open, drag around and try a different arrangement of is worth more than one
/// you can only regenerate, and the way somebody proposes a better layout is to change the
/// coordinates in the source and send that.
/// </remarks>
internal static class DrawingExcalidraw
{
    private const string Ink = "#1e1e1e";
    private const string Edge = "#868e96";

    internal static string Write(Drawing drawing, string sourceName)
    {
        var json = new StringBuilder();
        var elements = new List<string>();

        // Seeds and nonces are a counter rather than a random number, because this file is
        // compared byte for byte against the committed one and a random field would make every
        // build a difference.
        var seed = 1000;

        if (drawing.Title.Length > 0)
        {
            elements.Add(Text($"t{seed++}", 24, 14, drawing.Width - 48, drawing.Title, 20, 2, "left", seed));
        }

        foreach (var shape in drawing.Shapes)
        {
            elements.Add(Rectangle(shape, seed++));

            var cell = shape.Kind == ShapeKind.Cell;
            var headingSize = cell ? 14.0 : 16.0;
            var bodySize = cell ? 12.0 : 14.0;
            var total = 0.0;
            for (var i = 0; i < shape.Lines.Count; i++)
            {
                total += (shape.IsHeading(i) ? headingSize : bodySize) * 1.25;
            }

            var y = shape.CentreY - (total / 2);

            for (var i = 0; i < shape.Lines.Count; i++)
            {
                var size = shape.IsHeading(i) ? headingSize : bodySize;
                var family = cell ? 3 : 2;
                elements.Add(Text($"t{seed++}", shape.X + 4, y, shape.Width - 8, shape.Lines[i], size, family, "center", seed));
                y += size * 1.25;
            }
        }

        foreach (var connector in drawing.Connectors)
        {
            var (x1, y1, x2, y2) = DrawingSvg.Anchors(connector.From, connector.To);
            elements.Add(Arrow(x1, y1, x2, y2, seed++));

            if (connector.Label.Length > 0)
            {
                elements.Add(Text($"t{seed++}", ((x1 + x2) / 2) - 60, ((y1 + y2) / 2) - 22, 120, connector.Label, 12, 2, "center", seed));
            }
        }

        json.Append("{\n")
            .Append("  \"type\": \"excalidraw\",\n")
            .Append("  \"version\": 2,\n")
            .Append("  \"source\": ").Append(Q($"xray, generated from {sourceName}")).Append(",\n")
            .Append("  \"elements\": [\n")
            .Append(string.Join(",\n", elements))
            .Append("\n  ],\n")
            .Append("  \"appState\": {\n")
            .Append("    \"gridSize\": 20,\n")
            .Append("    \"gridModeEnabled\": false,\n")
            .Append("    \"viewBackgroundColor\": \"#ffffff\"\n")
            .Append("  },\n")
            .Append("  \"files\": {}\n")
            .Append("}\n");

        return json.ToString();
    }

    private static string Rectangle(Shape shape, int seed)
    {
        var background = shape.Kind switch
        {
            ShapeKind.Note => "#fff9db",
            ShapeKind.Cell => "#f8f9fa",
            _ => "#ffffff",
        };

        var stroke = shape.Kind == ShapeKind.Note ? "dashed" : "solid";

        var element = new StringBuilder();
        element.Append("    {\n");
        Common(element, "rectangle", shape.Id, shape.X, shape.Y, shape.Width, shape.Height, seed);
        element.Append("      \"strokeColor\": ").Append(Q(Ink)).Append(",\n")
               .Append("      \"backgroundColor\": ").Append(Q(background)).Append(",\n")
               .Append("      \"fillStyle\": \"solid\",\n")
               .Append("      \"strokeWidth\": 1,\n")
               .Append("      \"strokeStyle\": ").Append(Q(stroke)).Append(",\n")
               .Append("      \"roundness\": { \"type\": 3 }\n")
               .Append("    }");
        return element.ToString();
    }

    private static string Text(string id, double x, double y, double width, string text, double size, int family, string align, int seed)
    {
        var element = new StringBuilder();
        element.Append("    {\n");
        Common(element, "text", id, x, y, width, size * 1.25, seed);
        element.Append("      \"strokeColor\": ").Append(Q(Ink)).Append(",\n")
               .Append("      \"backgroundColor\": \"transparent\",\n")
               .Append("      \"fillStyle\": \"solid\",\n")
               .Append("      \"strokeWidth\": 1,\n")
               .Append("      \"strokeStyle\": \"solid\",\n")
               .Append("      \"roundness\": null,\n")
               .Append("      \"text\": ").Append(Q(text)).Append(",\n")
               .Append("      \"originalText\": ").Append(Q(text)).Append(",\n")
               .Append("      \"fontSize\": ").Append(N(size)).Append(",\n")
               .Append("      \"fontFamily\": ").Append(family.ToString(CultureInfo.InvariantCulture)).Append(",\n")
               .Append("      \"textAlign\": ").Append(Q(align)).Append(",\n")
               .Append("      \"verticalAlign\": \"top\",\n")
               .Append("      \"containerId\": null,\n")
               .Append("      \"lineHeight\": 1.25,\n")
               .Append("      \"autoResize\": false\n")
               .Append("    }");
        return element.ToString();
    }

    private static string Arrow(double x1, double y1, double x2, double y2, int seed)
    {
        var element = new StringBuilder();
        element.Append("    {\n");
        Common(element, "arrow", $"a{seed}", x1, y1, x2 - x1, y2 - y1, seed);
        element.Append("      \"strokeColor\": ").Append(Q(Edge)).Append(",\n")
               .Append("      \"backgroundColor\": \"transparent\",\n")
               .Append("      \"fillStyle\": \"solid\",\n")
               .Append("      \"strokeWidth\": 1,\n")
               .Append("      \"strokeStyle\": \"solid\",\n")
               .Append("      \"roundness\": null,\n")
               .Append("      \"points\": [[0, 0], [").Append(N(x2 - x1)).Append(", ").Append(N(y2 - y1)).Append("]],\n")
               .Append("      \"lastCommittedPoint\": null,\n")
               .Append("      \"startBinding\": null,\n")
               .Append("      \"endBinding\": null,\n")
               .Append("      \"startArrowhead\": null,\n")
               .Append("      \"endArrowhead\": \"arrow\",\n")
               .Append("      \"elbowed\": false\n")
               .Append("    }");
        return element.ToString();
    }

    private static void Common(StringBuilder element, string type, string id, double x, double y, double width, double height, int seed)
    {
        element.Append("      \"id\": ").Append(Q(id)).Append(",\n")
               .Append("      \"type\": ").Append(Q(type)).Append(",\n")
               .Append("      \"x\": ").Append(N(x)).Append(",\n")
               .Append("      \"y\": ").Append(N(y)).Append(",\n")
               .Append("      \"width\": ").Append(N(width)).Append(",\n")
               .Append("      \"height\": ").Append(N(height)).Append(",\n")
               .Append("      \"angle\": 0,\n")
               .Append("      \"opacity\": 100,\n")
               .Append("      \"roughness\": 0,\n")
               .Append("      \"seed\": ").Append(seed.ToString(CultureInfo.InvariantCulture)).Append(",\n")
               .Append("      \"version\": 1,\n")
               .Append("      \"versionNonce\": ").Append(seed.ToString(CultureInfo.InvariantCulture)).Append(",\n")
               .Append("      \"updated\": 1,\n")
               .Append("      \"isDeleted\": false,\n")
               .Append("      \"groupIds\": [],\n")
               .Append("      \"frameId\": null,\n")
               .Append("      \"boundElements\": null,\n")
               .Append("      \"link\": null,\n")
               .Append("      \"locked\": false,\n");
    }

    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Q(string text) => JsonSerializer.Serialize(text);
}
