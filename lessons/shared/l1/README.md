# L1, the ordinary thing

`Shapes.cs` is one of the three programs the book carries all the way through. It is not a fixture belonging to any one lesson and no lesson may edit it.

A lesson uses it by putting a project in its own `fixture/` directory that compiles the source from here.

```xml
<ItemGroup>
  <Compile Include="../../shared/l1/*.cs" />
</ItemGroup>
```

Each lesson builds its own copy into its own output directory, so two lessons running at the same time cannot tread on each other, and a lesson that needs a different build configuration can have one.

## Why it is shaped the way it is

Ten lines that reach a surprising amount of the runtime. A generic list, so there is a generic instantiation to look at. A `foreach`, so there is a lowering to find. An abstract method with two implementations called through the base type, so there is virtual dispatch and later something for the JIT to devirtualise. An interpolated string with a format specifier, so there is a second lowering and a second allocation. Four objects, so there is something for a collector to find.

It prints one line and the line is the same on every platform. Nothing in it is timing dependent, nothing in it touches the file system or the network, and nothing in it needs a privilege.

## Changing it

Do not, unless the change is worth regenerating every lesson that uses it. Adding a line here changes the row counts, the IL, the token values and the JIT output on pages spread across the whole book, and every one of those pages has committed output that has to be regenerated and re-read.

If a change really is needed, `xray check` will name every lesson that has drifted.
