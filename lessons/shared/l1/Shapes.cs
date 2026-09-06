// L1, the ordinary thing. This is one of the three programs the whole book keeps coming back to,
// and it is deliberately the most boring C# anybody could write. That is the point. Every part of
// the book looks at this same program again at its own depth, so a reader ends up with one deep
// model of one program rather than ninety shallow ones.
//
// There is more machinery under these ten lines than most people would guess. A generic list, a
// foreach, a virtual call made three times to two different implementations, an interpolated
// string, a format specifier, and four objects on the heap. Later parts take all of it apart.
//
// Nothing in here should ever be changed for the convenience of one lesson. Lessons that need a
// different program get a fixture of their own.

var shapes = new List<Shape> { new Circle(2), new Square(3), new Circle(5) };

double total = 0;

foreach (var shape in shapes)
{
    total += shape.Area();
}

Console.WriteLine($"{shapes.Count} shapes, {total:F2} total");

public abstract class Shape
{
    public abstract double Area();
}

public sealed class Circle(double radius) : Shape
{
    public override double Area() => Math.PI * radius * radius;
}

public sealed class Square(double side) : Shape
{
    public override double Area() => side * side;
}
