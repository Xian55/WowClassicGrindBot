using SharedLib.NpcFinder;

using System;
using System.Threading;

namespace Core.Goals;

public sealed class TargetFinder
{
    private const int waitMs = 200;

    private readonly ConfigurableInput input;
    private readonly AddonBits bits;
    private readonly NpcNameTargeting npcNameTargeting;
    private readonly Wait wait;

    private DateTime lastActive;

    public int ElapsedMs =>
        (int)(DateTime.UtcNow - lastActive).TotalMilliseconds;

    public TargetFinder(ConfigurableInput input,
        AddonBits bits, NpcNameTargeting npcNameTargeting, Wait wait)
    {
        this.input = input;
        this.bits = bits;
        this.npcNameTargeting = npcNameTargeting;
        this.wait = wait;

        lastActive = DateTime.UtcNow;
    }

    public void Reset()
    {
        npcNameTargeting.ChangeNpcType(NpcNames.None);
        npcNameTargeting.Reset();
    }

    public bool Search(
        NpcNames target, Func<bool> validTarget, CancellationToken token)
    {
        return LookForTarget(target, token) && validTarget();
    }

    private bool LookForTarget(
        NpcNames target, CancellationToken token)
    {
        if (ElapsedMs < waitMs)
            return bits.Target();

        if (!input.TargetNearestTarget.OnCooldown())
        {
            lastActive = DateTime.UtcNow;
            input.PressNearestTarget(token);
            wait.Update(token);
        }

        if (!token.IsCancellationRequested &&
            !input.KeyboardOnly && !bits.Target())
        {
            npcNameTargeting.ChangeNpcType(target);
            npcNameTargeting.WaitForUpdate(token);

            if (token.IsCancellationRequested)
                return false;

            if (npcNameTargeting.FoundAny() &&
                !input.IsKeyDown(input.TurnLeftKey) &&
                !input.IsKeyDown(input.TurnRightKey))
            {
                lastActive = DateTime.UtcNow;
                return npcNameTargeting.AcquireNonBlacklisted(token);
            }
        }

        return bits.Target();
    }

}
