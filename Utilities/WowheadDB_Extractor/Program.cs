using System.Threading.Tasks;

namespace WowheadDB_Extractor
{
    class Program
    {
        private static ZoneExtractor ZoneExtractor;

        static void Main(string[] args)
        {
            MainAsync(args).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        async static Task MainAsync(string[] args)
        {
            ZoneExtractor = new ZoneExtractor();
            await ZoneExtractor.Run();
        }

    }
}