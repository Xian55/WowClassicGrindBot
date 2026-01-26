using Core.GOAP;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Core.Goals;

public abstract partial class GoapGoal
{
    public Dictionary<GoapKey, bool> Preconditions { get; } = new();
    public Dictionary<GoapKey, bool> Effects { get; } = new();

    private KeyAction[] keys = Array.Empty<KeyAction>();
    public KeyAction[] Keys
    {
        get => keys;
        protected set
        {
            keys = value;
            if (keys.Length == 1)
                DisplayName = $"{keys[0].Name} [{keys[0].Key}]";
        }
    }

    public abstract float Cost { get; }

    public string Name { get; }

    public string DisplayName { get; protected set; }

    public event Action<GoapEventArgs>? GoapEvent;

    protected GoapGoal(string name)
    {
        string output = RegexGoalName().Replace(name.Replace("Goal", ""), m => " " + m.Value.ToUpperInvariant());
        DisplayName = Name = string.Concat(output[0].ToString().ToUpper(), output.AsSpan(1));
    }

    public void SendGoapEvent(GoapEventArgs e)
    {
        GoapEvent?.Invoke(e);
    }

    public virtual bool CanRun() => true;

    public virtual void OnEnter() { }

    public virtual void OnExit() { }

    public virtual void Update() { }

    protected void AddPrecondition(GoapKey key, bool value)
    {
        Preconditions[key] = value;
    }
    protected void AddEffect(GoapKey key, bool value)
    {
        Effects[key] = value;
    }

    [GeneratedRegex(@"\p{Lu}")]
    private static partial Regex RegexGoalName();
}