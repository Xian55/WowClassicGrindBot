using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Core
{
    public class KeyAction
    {
        public string Name { get; set; } = string.Empty;
        public bool HasCastBar { get; set; }
        public bool StopBeforeCast { get; set; } = false;
        public ConsoleKey ConsoleKey { get; set; } = 0;
        public string Key { get; set; } = string.Empty;
        public int PressDuration { get; set; } = 50;
        public string ShapeShiftForm { get; set; } = string.Empty;
        public ShapeshiftForm ShapeShiftFormEnum { get; set; } = ShapeshiftForm.None;
        public string CastIfAddsVisible { get; set; } = "";
        public float Cooldown { get; set; } = 0;

        private int _charge;
        public int Charge { get; set; } = 1;
        public SchoolMask School { get; set; } = SchoolMask.None;
        public int MinMana { get; set; } = 0;
        public int MinRage { get; set; } = 0;
        public int MinEnergy { get; set; } = 0;
        public int MinComboPoints { get; set; } = 0;

        public string Requirement { get; set; } = string.Empty;
        public List<string> Requirements { get; } = new List<string>();

        public bool WhenUsable { get; } = false;

        public bool WaitForWithinMelleRange { get; set; } = false;
        public bool ResetOnNewTarget { get; set; } = false;

        public bool Log { get; set; } = true;
        public int DelayAfterCast { get; set; } = 1100; // GCD 1500 - but spell queue window 400 ms
        public bool DelayUntilCombat { get; set; } = false;
        public int DelayBeforeCast { get; set; } = 0;
        public float Cost { get; set; } = 18;
        public string InCombat { get; set; } = "false";

        public string PathFilename { get; set; } = string.Empty;
        public List<WowPoint> Path { get; } = new List<WowPoint>();

        public int StepBackAfterCast {get; set; } = 0;

        public WowPoint LastClickPostion { get; private set; } = new WowPoint(0, 0);

        public List<Requirement> RequirementObjects { get; } = new List<Requirement>();

        protected static ConcurrentDictionary<ConsoleKey, DateTime> LastClicked { get; } = new ConcurrentDictionary<ConsoleKey, DateTime>();

        public static ConsoleKey LastKeyClicked()
        {
            if (!LastClicked.Any()) { return ConsoleKey.NoName; }

            var last = LastClicked.OrderByDescending(s => s.Value).First();
            if ( (DateTime.Now- last.Value).TotalSeconds>2)
            {
                return ConsoleKey.NoName;
            }
            return last.Key;
        }

        private PlayerReader? playerReader;

        private ILogger? logger;

        public void Initialise(AddonReader addonReader, RequirementFactory requirementFactory, ILogger logger)
        {
            this.playerReader = addonReader.PlayerReader;
            this.logger = logger;

            ResetCharges();

            KeyReader.ReadKey(logger, this);

            UpdateMinResourceRequirement(addonReader.ActionBarCostReader);

            if (!string.IsNullOrEmpty(this.Requirement))
            {
                Requirements.Add(this.Requirement);
            }

            requirementFactory.InitialiseRequirements(this);

            if (!string.IsNullOrEmpty(ShapeShiftForm))
            {
                if (Enum.TryParse(typeof(ShapeshiftForm), ShapeShiftForm, out var desiredForm))
                {
                    this.ShapeShiftFormEnum = (ShapeshiftForm)desiredForm;
                }
                else
                {
                    logger.LogInformation($"Unknown shapeshift form: {ShapeShiftForm}");
                }
            }
        }

        public void CreateCooldownRequirement()
        {
            if (this.Cooldown > 0)
            {
                this.RequirementObjects.Add(new Requirement
                {
                    HasRequirement = () => GetCooldownRemaining() == 0,
                    LogMessage = () => $"Cooldown {GetCooldownRemaining()}",
                    VisibleIfHasRequirement = false
                });
            }
        }

        public float GetCooldownRemaining()
        {
            try
            {
                if (!LastClicked.ContainsKey(this.ConsoleKey))
                {
                    return 0;
                }

                var remaining = this.Cooldown - ((int)(DateTime.Now - LastClicked[this.ConsoleKey]).TotalSeconds);

                return remaining < 0 ? 0 : remaining;
            }
            catch (Exception e)
            {
                this.logger.LogError(e, "GetCooldownRemaining()");
                return 0;
            }
        }

        internal void SetClicked()
        {
            try
            {
                if (this.playerReader != null)
                {
                    LastClickPostion = this.playerReader.PlayerLocation;
                }

                if (LastClicked.ContainsKey(this.ConsoleKey))
                {
                    LastClicked[this.ConsoleKey] = DateTime.Now;
                }
                else
                {
                    LastClicked.TryAdd(this.ConsoleKey, DateTime.Now);
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "SetClicked()");
            }
        }

        public double MillisecondsSinceLastClick => LastClicked.ContainsKey(this.ConsoleKey) ? (DateTime.Now - LastClicked[this.ConsoleKey]).TotalMilliseconds : double.MaxValue;

        internal void ResetCooldown()
        {
            if (LastClicked.ContainsKey(ConsoleKey))
            {
                LastClicked.TryRemove(ConsoleKey, out _);
            }
        }

        public void CreateChargeRequirement()
        {
            if (this.Charge > 1)
            {
                this.RequirementObjects.Add(new Requirement
                {
                    HasRequirement = () => GetChargeRemaining() != 0,
                    LogMessage = () => $"Charge {GetChargeRemaining()}",
                    VisibleIfHasRequirement = true
                });
            }
        }

        public int GetChargeRemaining()
        {
            return _charge;
        }

        public void ConsumeCharge()
        {
            if(Charge > 1)
            {
                _charge--;
                if(_charge > 0)
                {
                    ResetCooldown();
                }
                else
                {
                    ResetCharges();
                    SetClicked();
                }
            }
        }

        internal void ResetCharges()
        {
            _charge = Charge;
        }

        public bool CanRun()
        {
            return !this.RequirementObjects.Any(r => !r.HasRequirement());
        }

        private void UpdateMinResourceRequirement(ActionBarCostReader actionBarCostReader)
        {
            var tuple = actionBarCostReader.GetCostByActionBarSlot(Key);
            if (tuple.Item2 != 0)
            {
                int oldValue = 0;
                switch (tuple.Item1)
                {
                    case PowerType.Mana:
                        oldValue = MinMana;
                        MinMana = tuple.Item2;
                        break;
                    case PowerType.Rage:
                        oldValue = MinRage;
                        MinRage = tuple.Item2;
                        break;
                    case PowerType.Energy:
                        oldValue = MinEnergy;
                        MinEnergy = tuple.Item2;
                        break;
                }

                logger.LogInformation($"Updated {tuple.Item1} cost of {this.Name} to {tuple.Item2} from {oldValue}");
            }
        }

        public void LogInformation(string message)
        {
            if (this.Log)
            {
                logger.LogInformation($"{this.Name}: {message}");
            }
        }
    }
}