using System;
using System.Threading.Tasks;

namespace Core
{
    public class Wait
    {
        private readonly PlayerReader playerReader;

        public Wait(PlayerReader playerReader)
        {
            this.playerReader = playerReader;
        }

        public async Task Update(int n)
        {
            var s = playerReader.GlobalTime;
            while (Math.Abs(s - playerReader.GlobalTime) <= n)
            {
                await Task.Delay(2 * (int)playerReader.AvgUpdateLatency);
            }
        }


        public async Task<bool> Interrupt(int durationMs, Func<bool> interrupt)
        {
            DateTime start = DateTime.Now;
            while ((DateTime.Now - start).TotalMilliseconds < durationMs)
            {
                await Update(1);
                if (interrupt())
                    return false;
            }

            return true;
        }

        public async Task<Tuple<bool, double>> InterruptTask(int durationMs, Func<bool> interrupt)
        {
            DateTime start = DateTime.Now;
            double elapsedMs;
            while ((elapsedMs = (DateTime.Now - start).TotalMilliseconds) < durationMs)
            {
                await Update(1);
                if (interrupt())
                    return Tuple.Create(false, elapsedMs);
            }

            return Tuple.Create(true, elapsedMs);
        }

        public async Task<bool> Interrupt(int durationMs, Task<bool> exit)
        {
            DateTime start = DateTime.Now;
            while ((DateTime.Now - start).TotalMilliseconds < durationMs)
            {
                await Update(1);
                if (await exit)
                    return false;
            }

            return true;
        }

        public async Task While(Func<bool> condition)
        {
            while (condition())
            {
                await Update(1);
            }
        }
    }
}
