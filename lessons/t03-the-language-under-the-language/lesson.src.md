---
id: t03-the-language-under-the-language
title: The language under the language
part: tour
env: E0
platforms: [linux-x64, linux-arm64, win-x64, osx-arm64]
---

# The language under the language

What IL is, and why there is one at all.

T02 read every column of the assembly except one. This reads that one.

The runtime has never seen a `foreach`. It has never seen a `using`, a lambda, a `record`, a pattern, an `async` method or a primary constructor. None of those exist below the compiler. What the runtime is handed is a stream of bytes in a language with a couple of hundred instructions and no features at all, and everything C# has is built out of those.

That language is called IL. You will also see it called CIL, which is what the standard calls it, and MSIL, which is what it was called before the standard. They are the same thing and this book says IL.

{{needs}}

Everything here reads the same file T02 read, with the same library that ships in the box. There is one difference: the disassembler on this page is written from scratch, in about thirty lines, because writing it is the fastest way to stop thinking of IL as something a tool shows you.

## The program

The same L1 as last time.

```csharp
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
```

## Why there is a middle language at all

![Why the middle language is a stack machine](../../docs/diagrams/evaluation-stack.svg)

The short answer is that the compiler does not know what machine you are going to run on, and does not want to know.

A processor works with registers. There are sixteen general purpose ones on a current x64 chip and thirty one on arm64, they have names, and deciding which value goes in which one is a real problem with real consequences for speed. If C# compiled straight to machine code, the compiler would have to solve that problem, and it would have to solve it separately for every processor anyone might ever use, at the moment you press build, for a machine that is not in front of it.

So it does not solve it. It writes down what the program means, using a machine that does not exist and therefore has no registers to argue about, and leaves the register problem for later. Later is T05, and it happens on the machine that is going to run the code, which by then knows exactly what it is.

The imaginary machine is a stack machine. Every instruction takes its inputs off the top of a stack and leaves its output there. Nothing names a register, because there are none, and nothing names a memory address, because those are not known yet either.

There is a second reason, and it is the one that made the format worth standardising. Because every instruction says exactly what it does to the stack, the depth of the stack at every point in a method can be worked out by reading it, without running anything. A method whose stack does not come out even is rejected before it executes a single instruction. That is what makes it reasonable for a runtime to load and run code it did not build itself.

## The instruction set, without typing it out

You do not have to write the instruction table. It ships in the box, on `System.Reflection.Emit.OpCodes`, one public static field per instruction, and each one knows its own number, the shape of its operand and what it does to the stack.

{{block:table}}

{{output:table}}

Two hundred and twenty six instructions is the whole language. Not a subset, not the common part, all of it. The C# specification runs to something over a thousand pages, and everything in it comes out as some arrangement of these.

Nearly all of them are one byte. The twenty seven that are not begin with the byte `0xFE` and take a second byte after it, which is what happens when a format designed around a single byte runs out of room and has to grow without breaking anything that already exists.

The last column matters more than it looks. `pop` and `push` are part of the definition of an instruction, not a note about it, which is what makes the checking in the previous section possible. Two of the rows say `Varpop` and `Varpush`, and those are the calls: how many values a call takes off the stack depends on how many parameters the thing being called has, so the answer is in the signature rather than in the instruction.

## A disassembler, and this is all of it

{{block:decoder}}

An opcode, then an operand whose length the opcode already told us, then the next opcode. That is the entire format of a method body. There is no framing, no separator and no length prefix on anything, because none is needed once you know what an instruction is.

The operands that are four bytes long are mostly tokens, and a token is the table and row pair from T02, so turning one into something readable is a lookup in the tables.

{{block:render}}

One operand is not a token into the tables. A string literal points into a heap of its own, which is why `ldstr` needs a special case and nothing else does. T02 walked past that heap without stopping.

## The smallest method in the program

{{block:square}}

{{output:square}}

Six instructions for `side * side`.

`ldarg.0` is `this`. In an instance method the arguments are numbered from zero and argument zero is the receiver, which is why an instance method with no parameters still has an argument.

`ldfld` takes the object off the top of the stack and puts the value of one of its fields there instead. The field it wants is the four byte token after the instruction, and here it names `<side>P`, which is the field a primary constructor parameter turns into. T02 found that name in the string heap and could not say what it was for.

`mul` takes two values off and puts one back. It does not say what kind of values, because it does not have to: what is on the stack got there from a field whose type is written down, and the rules of the format make it impossible for the two values to be different kinds.

The offsets are byte offsets rather than instruction numbers. `IL_0001` to `IL_0006` is five bytes because `ldfld` is one byte with a four byte token after it, and `IL_000C` to `IL_000D` is one because `mul` has no operand at all.

## How deep does it get

{{gate:maxstack}}

{{block:stack}}

{{output:stack}}

Reading down the last column is the whole idea of a stack machine in one picture. It goes up, up again, comes back down when the multiply consumes both values, and finishes at nothing left over. A method that ended anywhere other than empty would be malformed, and would be refused rather than run.

The walk was short because the method runs straight through. Put a branch in and it stops being a matter of reading down a list, because the depth at a branch target has to come out the same however you arrive at it, and finding out means following every path into it. That rule is in the standard and the runtime enforces it, and it is the reason a stack that no hardware has can still be checked. The main method further down has three branches in it.

The last two lines disagree for the reason the gate gives, and the next section is the rest of that story.

## What sits in front of the instructions

{{block:header}}

{{output:header}}

Two formats, and the difference is stark. Seven of the eight methods get a one byte header. The eighth gets twenty eight bytes.

A method qualifies for the small header when its code is short, it has no local variables and it has no exception handling. Everything about it is then implied: the maximum stack depth is eight, the locals are none, and there is nothing else to say. Almost every method anyone writes qualifies, which is the point, because a header is paid for once per method and there are a great many methods.

The large header is twelve bytes, and the twenty eight here is those twelve plus sixteen more for a table describing one exception region. It has room for a real maximum depth, which is why the main method says three rather than eight, and a token pointing at the list of local variable types, and a flag.

That flag is `init`, and it says whether the runtime has to write zeroes over the local variables before the method starts. It is on here, as it is in nearly all C# code, and it is why a local variable in .NET does not contain rubbish from whatever ran last. Turning it off is possible and is one of the few things in this book that will genuinely make a program faster and genuinely make it dangerous.

The token pointing at the local variable list points into a table called StandAloneSig. T02's census showed exactly one row in that table and offered no explanation. This is the explanation: one method in this program has locals, and that row is the list of their types.

The third line of the output is the abstract method. Its row exists, its name exists, its signature exists, and where the address of its body would be there is a zero. There is nothing to disassemble because there is nothing there, and the flag on the row says so.

## Two ways to call

{{gate:callvirt}}

{{block:dispatch}}

{{output:dispatch}}

Three instructions in this program carry a method token. `newobj` makes an object and runs a constructor on it in one step. `call` calls a method the code has already decided on. `callvirt` calls a method after checking the receiver is not null, and looks the method up at run time if it is virtual.

Only one line in that list is a call that cannot be settled until the program runs, and it is `callvirt Shape.Area`. Every other `callvirt` here is a null check in front of a method whose identity was decided at compile time, and everything called with `call` is either on a struct, or static, or a constructor.

You can read the inheritance out of the bottom of that list. `Shape..ctor` is called twice, once by `Circle` and once by `Square`. `Object..ctor` is called twice, once by `Shape` and once by `Program`. Every constructor in .NET calls one further up until the chain reaches `Object`, and this program is small enough that the whole chain fits in four lines.

## The whole program

{{gate:constrained}}

{{block:main}}

{{output:main}}

Two hundred bytes. Read it against the ten lines of source, because most of what is interesting on this page is in the gap between the two.

**Five local slots for three variables.** Slots zero, one and three are `shapes`, `total` and `shape`. Slot two is the enumerator the `foreach` needed and slot four is the handler the interpolated string needed, and nobody wrote either. T02 found three local names in the pdb and a gap where slot two should be, and could not say what was in the gap. This is what was in it.

**The list is built with `dup`.** `newobj`, then `dup`, then the item, then `Add`, three times over, and only one `stloc` at the end. `Add` returns nothing and takes the list off the stack, so the list has to be duplicated before each call to have anything left for the next one. That is what a collection initializer is.

**There is no conversion instruction anywhere in the program.** The source says `new Circle(2)` with an integer, and the file says `ldc.r8` with a floating point two in it. The conversion happened while you were compiling, and the same thing happened to the line that starts `total` off at zero.

**The loop is tested at the bottom.** The first thing the loop does is branch forward to `IL_0066`, where the enumerator is loaded and `MoveNext` is called, and the body of the loop sits above that. One branch per iteration instead of two, and this shape is so standard that seeing it is how you recognise a loop in code you have never read.

**The enumerator is never copied.** Every touch of slot two is `ldloca.s`, which loads its address, rather than `ldloc`, which would load its value. It is a struct, and the code works on it where it lies.

**There is a try and a finally, and the source has neither.** `leave.s` at `IL_006F` is how you get out of a protected region, `endfinally` at `IL_007E` is the end of the handler, and the region itself was declared in the header rather than in the instructions. T02 predicted this from a single row in the MemberRef table. Here it is.

**One call in the whole program is genuinely virtual.** `callvirt Shape.Area` at `IL_005F` is the only place the runtime has to work out which method to run, and it has to do it on every iteration. T06 is about what happens to that call once the runtime has watched it a few times.

**The interpolated string was measured at compile time.** `ldc.i4.s` with fifteen in it and `ldc.i4.2` are the arguments to the handler's constructor: fifteen characters of literal text and two holes to fill. The literal parts are `" shapes, "` and `" total"`, which are nine characters and six. The compiler counted them so that the handler can ask for a buffer of the right size once instead of growing one.

**Nothing is boxed.** `AppendFormatted<Int32>` and `AppendFormatted<Double>` are generic, instantiated for the exact types, so the count and the total go in as themselves. On an older .NET this line allocated a boxed integer, a boxed double and several intermediate strings.

## Counted

{{block:census}}

{{output:census}}

Twenty eight distinct instructions. That is what an ordinary program is made of, out of a set eight times the size.

Most of the instruction set is not for code like this. There are arithmetic instructions for unsigned types and for checked arithmetic, loads and stores through raw pointers, prefixes for unaligned access, volatile access and tail calls, and a whole family for arrays with more than one dimension. They exist because the format is meant to be a target for more than one language and for code generators that are not compilers, and a program written the way people write programs touches almost none of them.

The frequency list is a fair picture of what a program does. Load something, call something, store the answer. `call` and `callvirt` together are a little over a fifth of every instruction in the program, which is the ordinary case and is the reason so much of the rest of this book is about making a call cheap.

## How much of it there is

{{block:scale}}

{{asserts:scale}}

The numbers are dropped because the second one belongs to whichever runtime is installed. Run it and look at the gap. Every one of those bytes goes through the same decoding this page does, on the way to becoming machine code, and almost none of it is ever touched by any one program. Part IV is about how that stays affordable.

## What your machine says

{{block:machine}}

{{asserts:machine}}

## What to take away

IL is a real language with a small instruction set, and it is written down in a standard you can read.

It is a stack machine because a stack machine has nothing in it that depends on the processor, which leaves the decision about registers until the processor is known.

Every instruction declares what it does to the stack, which is what lets a runtime check a method before running it, which is what makes it safe to load code from somewhere else.

A method body is a header and then instructions, and the header is one byte when the method is small and plain, which is nearly always.

`call` and `callvirt` are not about what you wrote. `callvirt` is a null check first and a lookup second, and only one call in this program is a lookup.

The listing does not match the source, and the places where it does not are where the interesting parts of .NET are.

## Where this goes next

T04 takes the gap between those ten lines of C# and these two hundred bytes and makes it the subject. The `foreach` that turned into four calls and a finally, the interpolated string that turned into a struct, and the ones this program did not reach.

T05 takes this same IL and turns it into machine code, which is when the register problem this page is about finally gets solved.

Part II covers the physical layout in the same detail T02's part covers the tables: what a fat header looks like byte by byte, how exception clauses are encoded, and where the local variable signature lives.

## Sources

ECMA-335, sixth edition.

Partition III is the instruction set. III.1 is the introduction, including the operand type table and the stack behaviour of each instruction, III.2 is the prefixes, with the constrained prefix in III.2.1, and III.3 and III.4 are the instructions themselves, one section each.

II.25.4 for the physical layout of a method body, with the small header in II.25.4.2, the large one in II.25.4.3, and the exception handling clauses in II.25.4.6. The rule that fixes the maximum stack depth at eight for the small header is in II.25.4.2.

II.23.2.6 for the local variable signature, and II.22.36 for the StandAloneSig table that holds it.

There are no `runtime:` citations here for the same reason as in T01 and T02. `pin.json` holds a null commit, so nothing in the runtime tree can be cited yet, and a citation that does not resolve is not accepted by the build.
