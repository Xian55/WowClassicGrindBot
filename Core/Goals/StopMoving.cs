using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Game;

namespace Core.Goals
{
    public class StopMoving
    {
        private readonly WowProcessInput input;
        private readonly PlayerReader playerReader;

        private const double MinDist = 0.01;

        private double XCoord = 0;
        private double YCoord = 0;
        private double Direction = 0;

        public StopMoving(WowProcessInput input, PlayerReader playerReader)
        {
            this.input = input;
            this.playerReader = playerReader;
        }

        public async Task Stop()
        {
            await StopForward();
            await StopTurn();
        }

        public async Task StopForward()
        {
            if (XCoord != playerReader.XCoord || YCoord != playerReader.YCoord)
            {
                if (!input.IsKeyDown(ConsoleKey.DownArrow) && !input.IsKeyDown(ConsoleKey.UpArrow) &&
                    (Math.Abs(XCoord - playerReader.XCoord) > MinDist || Math.Abs(YCoord - playerReader.YCoord) > MinDist))
                {
                    input.SetKeyState(ConsoleKey.UpArrow, true, false, "StopForward - Cancel interact");
                    await Task.Delay(1);
                }

                input.SetKeyState(ConsoleKey.UpArrow, false, false, "");
                input.SetKeyState(ConsoleKey.DownArrow, false, false, "StopForward");
                await Task.Delay(1);
            }

            this.XCoord = playerReader.XCoord;
            this.YCoord = playerReader.YCoord;
        }

        public async Task StopTurn()
        {
            if (Direction != playerReader.Direction)
            {
                input.SetKeyState(ConsoleKey.LeftArrow, false, false, "");
                await Task.Delay(1);
                input.SetKeyState(ConsoleKey.RightArrow, false, false, "StopTurn");
                await Task.Delay(1);
            }

            this.Direction = playerReader.Direction;
        }
    }
}