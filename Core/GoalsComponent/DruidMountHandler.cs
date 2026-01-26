using Core.Goals;

using System.Numerics;
using System.Threading;

namespace Core;

public sealed class DruidMountHandler : IMountHandler
{
    private readonly MountHandler mountHandler;
    private readonly ClassConfiguration classConfig;
    private readonly CastingHandler castingHandler;
    private readonly PlayerReader playerReader;
    private readonly ConfigurableInput input;

    public DruidMountHandler(MountHandler mountHandler,
        CastingHandler castingHandler, ClassConfiguration classConfig,
        PlayerReader playerReader, ConfigurableInput input)
    {
        this.mountHandler = mountHandler;
        this.castingHandler = castingHandler;
        this.classConfig = classConfig;
        this.playerReader = playerReader;
        this.input = input;
    }

    public bool CanMount() =>
        playerReader.Form is not
        Form.Druid_Flight or
        Form.Druid_Travel &&
        mountHandler.CanMount();


    public void Dismount()
    {
        if (playerReader.Form is Form.Druid_Flight or Form.Druid_Travel &&
            classConfig.Form.Get(playerReader.Form, out KeyAction? formAction))
        {
            input.PressRandom(formAction!);
            return;
        }

        mountHandler.Dismount();
    }

    public bool IsMounted()
    {
        return
            playerReader.Form is Form.Druid_Flight or Form.Druid_Travel ||
            mountHandler.IsMounted();
    }

    public void MountUp()
    {
        for (int i = 0; i < classConfig.Form.Sequence.Length; i++)
        {
            KeyAction keyAction = classConfig.Form.Sequence[i];
            if (keyAction.FormValue is Form.Druid_Flight or Form.Druid_Travel &&
                keyAction.CanRun() &&
                castingHandler.WaitForGCD(keyAction, false, false, CancellationToken.None) &&
                castingHandler.SwitchForm(keyAction, CancellationToken.None))
            {
                return;
            }
        }

        mountHandler.MountUp();
    }

    public bool ShouldMount(Vector3 targetW) =>
        mountHandler.ShouldMount(targetW);
}
