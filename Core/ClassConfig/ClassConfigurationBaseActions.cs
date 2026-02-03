using Game;

namespace Core;

public sealed partial class ClassConfiguration
{
    public KeyAction Jump { get; } = new()
    {
        Name = nameof(Jump),
        BindingID = BindingID.JUMP,
        BaseAction = true
    };

    public KeyAction Interact { get; } = new()
    {
        Name = nameof(Interact),
        BindingID = BindingID.INTERACTTARGET,
        Cooldown = 0,
        PressDuration = InputDuration.FastPress,
        BaseAction = true
    };

    public KeyAction InteractMouseOver { get; } = new()
    {
        Name = nameof(InteractMouseOver),
        BindingID = BindingID.INTERACTMOUSEOVER,
        Cooldown = 0,
        PressDuration = InputDuration.VeryFastPress,
        BaseAction = true
    };

    public KeyAction Approach { get; } = new()
    {
        Name = nameof(Approach),
        BindingID = BindingID.INTERACTTARGET,
        PressDuration = 10,
        BaseAction = true,
        Requirement = "!SoftTargetDead"
    };

    public KeyAction AutoAttack { get; } = new()
    {
        Name = nameof(AutoAttack),
        BindingID = BindingID.STARTATTACK,
        BaseAction = true,
        Requirement = "!AutoAttacking && !MeleeSwinging"
    };

    public KeyAction TargetLastTarget { get; } = new()
    {
        Name = nameof(TargetLastTarget),
        BindingID = BindingID.TARGETLASTTARGET,
        Cooldown = 0,
        BaseAction = true
    };

    public KeyAction StandUp { get; } = new()
    {
        Name = nameof(StandUp),
        BindingID = BindingID.SITORSTAND,
        Cooldown = 0,
        BaseAction = true,
    };

    public KeyAction ClearTarget { get; } = new()
    {
        Name = nameof(ClearTarget),
        BindingID = BindingID.CUSTOM_CLEARTARGET,
        Cooldown = 0,
        BaseAction = true,
    };

    public KeyAction StopAttack { get; } = new()
    {
        Name = nameof(StopAttack),
        BindingID = BindingID.CUSTOM_STOPATTACK,
        PressDuration = InputDuration.FastPress,
        BaseAction = true,
    };

    public KeyAction TargetNearestTarget { get; } = new()
    {
        Name = nameof(TargetNearestTarget),
        BindingID = BindingID.TARGETNEARESTENEMY,
        BaseAction = true,
        PressDuration = InputDuration.FastPress
    };

    public KeyAction TargetTargetOfTarget { get; } = new()
    {
        Name = nameof(TargetTargetOfTarget),
        BindingID = BindingID.ASSISTTARGET,
        Cooldown = 0,
        BaseAction = true,
    };

    public KeyAction TargetPet { get; } = new()
    {
        Name = nameof(TargetPet),
        BindingID = BindingID.TARGETPET,
        Cooldown = 0,
        BaseAction = true,
    };

    public KeyAction PetAttack { get; } = new()
    {
        Name = nameof(PetAttack),
        BindingID = BindingID.PETATTACK,
        PressDuration = InputDuration.VeryFastPress,
        BaseAction = true,
    };

    public KeyAction TargetFocus { get; } = new()
    {
        Name = nameof(TargetFocus),
        BindingID = BindingID.TARGETFOCUS,
        Cooldown = 0,
        BaseAction = true,
    };

    public KeyAction FollowTarget { get; } = new()
    {
        Name = nameof(FollowTarget),
        BindingID = BindingID.FOLLOWTARGET,
        Cooldown = 0,
        BaseAction = true,
    };

    public KeyAction Mount { get; } = new()
    {
        Key = "O",
        Name = nameof(Mount),
        BaseAction = true,
        Cooldown = 6000,
    };

    public KeyAction StrafeLeft { get; } = new()
    {
        Name = nameof(StrafeLeft),
        BindingID = BindingID.STRAFELEFT,
        BaseAction = true,
    };

    public KeyAction StrafeRight { get; } = new()
    {
        Name = nameof(StrafeRight),
        BindingID = BindingID.STRAFERIGHT,
        BaseAction = true,
    };
}
