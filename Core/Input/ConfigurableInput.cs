using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Game;

namespace Core
{
    public class ConfigurableInput : WowProcessInput
    {
        private readonly ClassConfiguration classConfig;

        private const int defaultKeyPress = 50;

        public ConfigurableInput(ILogger logger, WowProcess wowProcess, ClassConfiguration classConfiguration) : base(logger, wowProcess)
        {
            this.classConfig = classConfiguration;
        }

        public async Task TapStopKey(string desc = "")
        {
            await KeyPress(ConsoleKey.UpArrow, defaultKeyPress, $"TapStopKey: {desc}");
        }

        public async Task TapInteractKey(string source)
        {
            await KeyPress(classConfig.Interact.ConsoleKey, defaultKeyPress, $"TapInteract ({source})");
            this.classConfig.Interact.SetClicked();
        }

        public async Task TapLastTargetKey(string source)
        {
            await KeyPress(classConfig.TargetLastTarget.ConsoleKey, defaultKeyPress, $"TapLastTarget ({source})");
            this.classConfig.TargetLastTarget.SetClicked();
        }

        public async Task TapStandUpKey(string desc = "")
        {
            await KeyPress(classConfig.StandUp.ConsoleKey, defaultKeyPress, $"TapStandUpKey: {desc}");
            this.classConfig.StandUp.SetClicked();
        }

        public async Task TapClearTarget(string desc = "")
        {
            await KeyPress(classConfig.ClearTarget.ConsoleKey, defaultKeyPress, $"TapClearTarget: {desc}");
            this.classConfig.ClearTarget.SetClicked();
        }

        public async Task TapStopAttack(string desc = "")
        {
            await KeyPress(classConfig.StopAttack.ConsoleKey, defaultKeyPress, $"TapStopAttack: {desc}");
            this.classConfig.StopAttack.SetClicked();
        }

        public async Task TapNearestTarget(string desc = "")
        {
            await KeyPress(classConfig.TargetNearestTarget.ConsoleKey, defaultKeyPress, $"TapNearestTarget: {desc}");
            this.classConfig.TargetNearestTarget.SetClicked();
        }

        public async Task TapTargetPet(string desc = "")
        {
            await KeyPress(classConfig.TargetPet.ConsoleKey, defaultKeyPress, $"TapTargetPet: {desc}");
            this.classConfig.TargetPet.SetClicked();
        }

        public async Task TapTargetOfTarget(string desc = "")
        {
            await KeyPress(classConfig.TargetTargetOfTarget.ConsoleKey, defaultKeyPress, $"TapTargetsTarget: {desc}");
            this.classConfig.TargetTargetOfTarget.SetClicked();
        }

        public async Task TapJump(string desc = "")
        {
            await KeyPress(classConfig.Jump.ConsoleKey, defaultKeyPress, $"TapJump: {desc}");
            this.classConfig.Jump.SetClicked();
        }

        public async Task TapPetAttack(string source = "")
        {
            await KeyPress(classConfig.PetAttack.ConsoleKey, defaultKeyPress, $"TapPetAttack ({source})");
            this.classConfig.PetAttack.SetClicked();
        }

        public async Task TapHearthstone()
        {
            await KeyPress(ConsoleKey.I, defaultKeyPress, "TapHearthstone");
        }

        public async Task TapMount()
        {
            await KeyPress(classConfig.Mount.ConsoleKey, defaultKeyPress, "TapMount");
            this.classConfig.Mount.SetClicked();
        }

        public async Task TapDismount()
        {
            await KeyPress(classConfig.Mount.ConsoleKey, defaultKeyPress, "TapDismount");
        }
    }
}
