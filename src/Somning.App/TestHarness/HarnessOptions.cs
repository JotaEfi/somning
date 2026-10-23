#if SOMNING_TEST_HARNESS
namespace Somning.App.TestHarness;

/// <summary>
/// Debug/SOMNING_TEST_HARNESS-only options (D-19, D-20): <c>--test-harness --fixture &lt;wav&gt;</c> replaces the
/// microphone with a paced FileAudioSource. Never compiled into Release builds.
/// </summary>
public sealed record HarnessOptions(string Fixture)
{
    public const string Flag = "--test-harness";

    public static bool IsRequested(IReadOnlyList<string> args) => args.Contains(Flag, StringComparer.Ordinal);

    public static HarnessOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? fixture = null;
        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case Flag:
                    break;
                case "--fixture" when index + 1 < args.Count && fixture is null:
                    fixture = args[++index];
                    break;
                default:
                    throw new ArgumentException("Unknown or duplicate harness argument.");
            }
        }
        if (fixture is null) throw new ArgumentException("--test-harness requires --fixture <wav>.");
        var full = Path.GetFullPath(fixture);
        if (!File.Exists(full)) throw new FileNotFoundException("Harness fixture not found.", full);
        return new HarnessOptions(full);
    }
}

/// <summary>The fixture used by the NEXT capture; swapped by the pipe's <c>set-fixture</c> command.</summary>
public sealed class HarnessFixture(string path)
{
    private string current = path;

    public string Path
    {
        get => Volatile.Read(ref current);
        set => Volatile.Write(ref current, value ?? throw new ArgumentNullException(nameof(value)));
    }
}
#endif
