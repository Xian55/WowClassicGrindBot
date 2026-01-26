using BenchmarkDotNet.Attributes;

using System.Runtime.CompilerServices;

namespace Requirement;

internal static class PlayerReader
{
    public static bool IsTargetCasting() => true;

    public static int SpellBeingCastByTarget { get; set; }
}

[MemoryDiagnoser]
public class Core_RequirementFactory_CreateTargetCastingSpell
{
    private const char SEP1 = ':';
    private const char SEP2 = ',';

    public Core_RequirementFactory_CreateTargetCastingSpell()
    {
        /*
        foreach(var input in CreateTargetCastingSpell_Inputs())
        {
            CreateTargetCastingSpell_Old(input);
            CreateTargetCastingSpell_New(input);
        }
        */
    }

    [Benchmark(Baseline = true)]
    [ArgumentsSource(nameof(CreateTargetCastingSpell_Inputs))]
    public void Old_CreateTargetCastingSpell(string text) => CreateTargetCastingSpell_Old(text);

    [Benchmark]
    [ArgumentsSource(nameof(CreateTargetCastingSpell_Inputs))]
    public void New_CreateTargetCastingSpell(string text) => CreateTargetCastingSpell_New(text);

    //

    public Core.Requirement CreateTargetCastingSpell_Old(string requirement)
    {
        return create(requirement);
        static Core.Requirement create(string requirement)
        {
            ReadOnlySpan<char> span = requirement;
            int sep1 = span.IndexOf(SEP1);
            // 'TargetCastingSpell'
            if (sep1 == -1)
            {
                return new Core.Requirement
                {
                    HasRequirement = PlayerReader.IsTargetCasting,
                    LogMessage = () => "Target casting"
                };
            }

            // 'TargetCastingSpell:_1_?,_n_'
            string[] spellsPart = span[(sep1 + 1)..].ToString().Split(SEP2);
            HashSet<int> spellIds = spellsPart.Select(int.Parse).ToHashSet();

            bool f() => spellIds.Contains(PlayerReader.SpellBeingCastByTarget);
            string s() => $"Target casts {PlayerReader.SpellBeingCastByTarget} ∈ [{string.Join(SEP2, spellIds)}]";
            return new Core.Requirement
            {
                HasRequirement = f,
                LogMessage = s
            };
        }
    }

    [SkipLocalsInit]
    public Core.Requirement CreateTargetCastingSpell_New(string requirement)
    {
        return create(requirement);
        static Core.Requirement create(string requirement)
        {
            ReadOnlySpan<char> span = requirement;
            int sep1 = span.IndexOf(SEP1);
            // 'TargetCastingSpell'
            if (sep1 == -1)
            {
                return new Core.Requirement
                {
                    HasRequirement = PlayerReader.IsTargetCasting,
                    LogMessage = () => "Target casting"
                };
            }

            // 'TargetCastingSpell:_1_?,_n_'
            Span<Range> ranges = stackalloc Range[span.Length];
            ReadOnlySpan<char> values = span[(sep1 + 1)..];
            int count = values.Split(ranges, SEP2);

            HashSet<int> spellIds = new(count);
            foreach (var range in ranges[..count])
            {
                spellIds.Add(int.Parse(values[range]));
            }

            bool f() => spellIds.Contains(PlayerReader.SpellBeingCastByTarget);
            string s() => $"Target casts {PlayerReader.SpellBeingCastByTarget} ∈ [{string.Join(SEP2, spellIds)}]";
            return new Core.Requirement
            {
                HasRequirement = f,
                LogMessage = s
            };
        }
    }

    public static IEnumerable<string> CreateTargetCastingSpell_Inputs()
    {
        yield return "TargetCastingSpell";
        yield return "TargetCastingSpell:1";
        yield return "TargetCastingSpell:1,12";
        yield return "TargetCastingSpell:1,12,123";
        yield return "TargetCastingSpell:1,12,123,1234";
        yield return "TargetCastingSpell:4321,321,21,1";
        yield return "TargetCastingSpell:4321648,3721,24841,148484";
    }

}
