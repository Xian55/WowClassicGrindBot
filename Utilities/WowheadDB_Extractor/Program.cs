using System.Threading;
using System.Threading.Tasks;

namespace WowheadDB_Extractor
{
    sealed class Program
    {
        static void Main(string[] args)
        {
            MainAsync(args).ConfigureAwait(false).GetAwaiter().GetResult();
            //Test_TspSolver();
        }

        async static Task MainAsync(string[] args)
        {
            // Optional first argument selects the client, e.g. "legacy_mop".
            // Output is per client (Json/area/<client>), so it must be explicit.
            if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
            {
                ZoneExtractor.EXP = args[0].ToLowerInvariant();
            }

            System.Console.WriteLine($"client : {ZoneExtractor.EXP}");
            System.Console.WriteLine($"wowhead: {ZoneExtractor.BaseUrl()}");
            System.Console.WriteLine();

            await ZoneExtractor.Run();
        }

        private static void Test_TspSolver()
        {
            GeneticTSPSolver solver = new(50);
            while (solver.UnchangedGens < solver.Length * 2)
            {
                solver.Evolve();
                System.Console.WriteLine(
                  solver.Length + " nodes, " +
                  solver.CurrentGen + "th gen with " +
                  solver.Mutations + " mutations. best value: " +
                  solver.BestValue +
                  " unchanged: " + solver.UnchangedGens);
                Thread.Sleep(1);
            }
            solver.Draw();
        }

    }
}