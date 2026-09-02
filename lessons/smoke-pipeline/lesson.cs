// This file is a lesson in the mechanical sense and not in any other sense. It exists so that the
// pipeline has something to run on all four platforms before a real lesson depends on it. If this
// one fails, the failure is in the tooling, and that is worth knowing separately.
//
// You can run it the ordinary way, with no tooling in the picture at all:
//
//     dotnet run lesson.cs
//
// The directives below are comments, so that command prints all three blocks in order.

//# block id=blocks env=E0 tags=[pipeline]
Console.WriteLine("a block is a named region of this file");
Console.WriteLine("the tool ran the whole program once and cut this out of the output afterwards");
//# end

//# block id=invariant env=E0 tags=[pipeline]
int bytes = sizeof(int);
int bits = bytes * 8;
Console.WriteLine($"an int is {bytes} bytes, so {bits} bits");
Console.WriteLine("that number is the same on linux-x64, linux-arm64, win-x64 and osx-arm64");
Console.WriteLine("which is why it is allowed to be an expected file");
//# end

// Everything this block prints is different on every machine, so it is marked drop. It still
// runs, and the page can still show the code, but there is no expected file for it and the page
// cannot quote its output. A lesson that wants to talk about a machine specific value has to
// describe it rather than assert it.
//# block id=machine env=E0 tags=[pipeline] capture=drop
Console.WriteLine(System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier);
Console.WriteLine(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
//# end

// A block marked none gets no marker inserted into it, which is what makes it the place to put a
// declaration. Top level statements have to come before types, so a marker here would not
// compile.
//# block id=helper env=E0 tags=[pipeline] capture=none
record Measurement(string Name, int Value);
//# end
