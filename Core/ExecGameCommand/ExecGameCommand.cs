using Game;
using System;
using System.Threading.Tasks;
using WinAPI;
using Microsoft.Extensions.Logging;
using TextCopy;

namespace Core
{
    public class ExecGameCommand
    {
        private readonly ILogger logger;
        private readonly WowProcessInput wowProcessInput;

        public ExecGameCommand(ILogger logger, WowProcessInput wowProcessInput)
        {
            this.logger = logger;
            this.wowProcessInput = wowProcessInput;
        }

        public async Task Run(string content)
        {
            wowProcessInput.SetForegroundWindow();
            logger.LogInformation(content);

            ClipboardService.SetText(content);
            await Task.Delay(50);

            // Open chat inputbox
            await wowProcessInput.KeyPress(ConsoleKey.Enter, 50);

            // Send Paste keys
            wowProcessInput.PasteFromClipboard();
            await Task.Delay(250);

            //
            await wowProcessInput.KeyPress(ConsoleKey.Enter, 50);
            await Task.Delay(250);
        }
    }
}
