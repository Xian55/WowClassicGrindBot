using System;

namespace ReadDBC_CSV_WorldMapArea
{
    class Program
    {
        static void Main(string[] args)
        {
            var transfrom = new Transform();
            transfrom.CreateWorldMapAreaJson();

            transfrom.Validate();

            Console.ReadLine();
        }
    }
}
