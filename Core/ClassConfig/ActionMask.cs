namespace Core;

public static class ActionMask
{
    public const int HasCastBar = 1;
    public const int WhenUsable = 1 << 1;
    public const int ResetOnNewTarget = 1 << 2;
    public const int Log = 1 << 3;
    public const int BaseAction = 1 << 4;
    public const int Item = 1 << 5;

    public const int BeforeCastStop = 1 << 6;
    public const int BeforeCastDismount = 1 << 7;
    public const int BeforeCastFaceTarget = 1 << 8;

    public const int AfterCastWaitMeleeRange = 1 << 9;
    public const int AfterCastWaitBuff = 1 << 10;
    public const int AfterCastWaitBag = 1 << 11;
    public const int AfterCastWaitSwing = 1 << 12;
    public const int AfterCastWaitCastbar = 1 << 13;
    public const int AfterCastWaitCombat = 1 << 14;
    public const int AfterCastWaitGCD = 1 << 15;
    public const int AfterCastAuraExpected = 1 << 16;
    public const int CancelOnInterrupt = 1 << 17;

    public const int UseMount = 1 << 18;
}
