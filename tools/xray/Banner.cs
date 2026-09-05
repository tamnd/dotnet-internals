using System.Globalization;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ClrXray;

/// <summary>
/// The first executable block of every lesson.
/// </summary>
/// <remarks>
/// Half the confusing results in this subject are a different machine rather than a different
/// fact, so a lesson says which machine it is standing on before it claims anything. A
/// disassembly listing with no banner above it is not evidence of anything, and CI rejects one.
/// </remarks>
internal static class Banner
{
    /// <summary>How the runtime this tool is running on describes itself, as in .NET 10.0.4.</summary>
    internal static string Framework => RuntimeInformation.FrameworkDescription;

    /// <summary>The runtime identifier, which is the shortest true answer to which machine this is.</summary>
    internal static string Platform => RuntimeInformation.RuntimeIdentifier;

    internal static int Run()
    {
        foreach (var (name, value) in Collect())
        {
            Console.WriteLine($"{name,-26} {value}");
        }

        return 0;
    }

    private static List<(string Name, string Value)> Collect()
    {
        var rows = new List<(string, string)>
        {
            ("runtime", Framework),
            ("version", Environment.Version.ToString()),
            ("os", RuntimeInformation.OSDescription.Trim()),
            ("platform", Platform),
            ("process arch", RuntimeInformation.ProcessArchitecture.ToString()),
            ("os arch", RuntimeInformation.OSArchitecture.ToString()),

            // Dynamic code is compiled under CoreCLR and it is not under NativeAOT. A lesson
            // about the JIT that finds this false is a lesson running somewhere it cannot
            // observe what it came to observe.
            ("dynamic code", RuntimeFeature.IsDynamicCodeCompiled ? "compiled" : "not compiled"),

            ("gc mode", GCSettings.IsServerGC ? "server" : "workstation"),
            ("gc latency", GCSettings.LatencyMode.ToString()),
            ("processors", Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture)),
        };

        // The runtime will say what the collector was configured with rather than making the
        // banner infer it, which matters most for the settings a reader never set: DATAS has
        // been on by default for Server GC since .NET 9 and changes what the heap looks like.
        foreach (var name in GcVariables)
        {
            if (GC.GetConfigurationVariables().TryGetValue(name, out var value) && value is not null)
            {
                rows.Add(("gc." + name, Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null"));
            }
        }

        // Anything set here changes what the reader sees, so it is printed rather than assumed
        // absent. The list is the set of knobs the lessons actually turn.
        foreach (var name in Knobs)
        {
            var value = Environment.GetEnvironmentVariable("DOTNET_" + name);
            if (!string.IsNullOrEmpty(value))
            {
                rows.Add(("DOTNET_" + name, value));
            }
        }

        return rows;
    }

    private static readonly string[] GcVariables =
    [
        "GCHeapCount",
        "GCgen0size",
        "GCConserveMemory",
        "GCDynamicAdaptationMode",
        "GCHeapHardLimit",
        "gcConcurrent",
        "gcServer",
    ];

    private static readonly string[] Knobs =
    [
        "GCName",
        "TieredCompilation",
        "TieredPGO",
        "TC_QuickJitForLoops",
        "ReadyToRun",
        "Interpreter",
        "JitName",
        "AltJit",
        "AltJitName",
        "JitDisasm",
        "JitDisasmSummary",
        "JitStdOutFile",
        "LegacyExceptionHandling",
        "PerfMapEnabled",
    ];
}
