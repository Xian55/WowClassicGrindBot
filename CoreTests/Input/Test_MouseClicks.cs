using System.Drawing;
using Game;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace CoreTests
{
    public class Test_MouseClicks
    {
        private readonly ILogger logger;
        private readonly WowProcess wowProcess;
        private readonly WowScreen wowScreen;
        private readonly WowProcessInput wowProcessInput;

        public Test_MouseClicks(ILogger logger)
        {
            this.logger = logger;

            wowProcess = new WowProcess();
            wowScreen = new WowScreen(logger, wowProcess);
            wowProcessInput = new WowProcessInput(logger, wowProcess);
        }

        public async Task Execute()
        {
            wowProcessInput.SetForegroundWindow();

            wowProcessInput.SetCursorPosition(new Point(25, 25));
            await Task.Delay(500);

            wowProcessInput.SetCursorPosition(new Point(50, 50));
            await Task.Delay(500);

            await Task.Delay(500);

            var p = new Point(120, 120);
            await wowProcessInput.LeftClickMouse(p);

            await Task.Delay(500);

            await wowProcessInput.RightClickMouse(p);

            await Task.Delay(500);

            await wowProcessInput.RightClickMouse(p);

            wowScreen.GetRectangle(out var rect);
            p = new Point(rect.Width / 3, rect.Height / 5);

            await Task.Delay(500);

            await wowProcessInput.RightClickMouse(p);

            await Task.Delay(500);

            await wowProcessInput.RightClickMouse(p);

            logger.LogInformation("Finished");
        }
    }
}
