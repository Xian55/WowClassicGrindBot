using Core.GOAP;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Core.Goals
{
    public class GoapPreCondition
    {
        public string Description { get; private set; }
        public object State { get; private set; }

        public GoapPreCondition(string description, object state)
        {
            this.Description = description;
            this.State = state;
        }
    }

    public class ActionEventArgs : EventArgs
    {
        public GoapKey Key { get; private set; }
        public object Value { get; private set; }

        public ActionEventArgs(GoapKey key, object value)
        {
            this.Key = key;
            this.Value = value;
        }
    }

    public abstract class GoapGoal
    {
        public HashSet<KeyValuePair<GoapKey, GoapPreCondition>> Preconditions { get; private set; } = new HashSet<KeyValuePair<GoapKey, GoapPreCondition>>();
        public HashSet<KeyValuePair<GoapKey, object>> Effects { get; private set; } = new HashSet<KeyValuePair<GoapKey, object>>();

        public List<KeyAction> Keys { get; private set; } = new List<KeyAction>();

        public abstract float CostOfPerformingAction { get; }

        public virtual bool Repeatable { get; } = true;

        public void DoReset()
        {
            ResetBeforePlanning();
        }

        private string name = string.Empty;

        public virtual string Name
        {
            get
            {
                if (string.IsNullOrEmpty(name))
                {
                    string output = Regex.Replace(this.GetType().Name.Replace("Action", ""), @"\p{Lu}", m => " " + m.Value.ToUpperInvariant());
                    this.name = char.ToUpperInvariant(output[0]) + output.Substring(1);
                }
                return name;
            }
        }

        public delegate void ActionEventHandler(object sender, ActionEventArgs e);

        public event ActionEventHandler? ActionEvent;

        public void SendActionEvent(ActionEventArgs e)
        {
            ActionEvent?.Invoke(this, e);
        }

        public Dictionary<string, bool> State { get; private set; } = new Dictionary<string, bool>();

        public void SetState(Dictionary<string, bool> newState)
        {
            State = newState;
        }

        public virtual void ResetBeforePlanning()
        {
        }

        public virtual bool CheckIfActionCanRun()
        {
            return true;
        }

        public abstract Task PerformAction();

        public virtual async Task Abort()
        {
            await Task.Delay(0);
        }

        public void AddPrecondition(GoapKey key, object value)
        {
            var precondition = new GoapPreCondition(GoapKeyDescription.ToString(key, value), value);
            Preconditions.Add(new KeyValuePair<GoapKey, GoapPreCondition>(key, precondition));
        }

        public void RemovePrecondition(GoapKey key)
        {
            Preconditions.Where(o => o.Key.Equals(key))
              .ToList()
              .ForEach(o => Preconditions.Remove(o));
        }

        public void AddEffect(GoapKey key, object value)
        {
            Effects.Add(new KeyValuePair<GoapKey, object>(key, value));
        }

        public void RemoveEffect(GoapKey key)
        {
            Effects.Where(o => o.Key.Equals(key))
                .ToList()
                .ForEach(o => Effects.Remove(o));
        }

        public virtual void OnActionEvent(object sender, ActionEventArgs e)
        {
        }

        public virtual string Description()
        {
            return $"{Name} " + (Keys.Count == 1 ? $"[{Keys.First().ConsoleKey}]" : "");
        }
    }
}