// This is your file. Edit it until the grader stops complaining:
//
//     dotnet run --project tools/xray -- boss lessons/smoke-pipeline
//
// The rules are the ones the tool itself follows. A block starts on a line whose first non space
// characters are //# block and ends on a line that says //# end. The start line carries attributes
// as space separated words, so id=blocks names the block. A block stores its output unless it says
// otherwise with capture=drop or capture=none.
//
// Read lesson.cs from code rather than counting the blocks by eye. The counting works here and
// stops working three lessons from now.

var lines = File.ReadAllLines("lesson.cs");

// Yours. Find the lines that open a block.
var directives = new List<string>();

// Yours. How many blocks are there.
var howMany = 0;

// Yours. How many of them store their output.
var stored = 0;

// Yours. The id on the first opening line.
var first = "nothing yet";

Console.WriteLine($"answer directives = {howMany}");
Console.WriteLine($"answer stored = {stored}");
Console.WriteLine($"answer first = {first}");

// These two lines keep the compiler quiet about the parts you have not used yet. Delete them once
// you have.
_ = lines;
_ = directives;
