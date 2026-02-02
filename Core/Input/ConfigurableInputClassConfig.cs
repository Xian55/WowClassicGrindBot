using System;

namespace Core;

public sealed partial class ConfigurableInput
{
    public bool KeyboardOnly => classConfig.KeyboardOnly;

    public ConsoleKey ForwardKey => classConfig.ForwardKey;
    public ConsoleKey BackwardKey => classConfig.BackwardKey;
    public ConsoleKey TurnLeftKey => classConfig.TurnLeftKey;
    public ConsoleKey TurnRightKey => classConfig.TurnRightKey;

    public KeyAction Jump => classConfig.Jump;
    public KeyAction Interact => classConfig.Interact;
    public KeyAction InteractMouseOver => classConfig.InteractMouseOver;
    public KeyAction Approach => classConfig.Approach;
    public KeyAction AutoAttack => classConfig.AutoAttack;
    public KeyAction TargetLastTarget => classConfig.TargetLastTarget;
    public KeyAction StandUp => classConfig.StandUp;
    public KeyAction ClearTarget => classConfig.ClearTarget;
    public KeyAction StopAttack => classConfig.StopAttack;
    public KeyAction TargetNearestTarget => classConfig.TargetNearestTarget;
    public KeyAction TargetTargetOfTarget => classConfig.TargetTargetOfTarget;
    public KeyAction TargetPet => classConfig.TargetPet;
    public KeyAction PetAttack => classConfig.PetAttack;
    public KeyAction TargetFocus => classConfig.TargetFocus;
    public KeyAction FollowTarget => classConfig.FollowTarget;
    public KeyAction Mount => classConfig.Mount;
    public KeyAction StrafeLeft => classConfig.StrafeLeft;
    public KeyAction StrafeRight => classConfig.StrafeRight;
}
