using Core.GOAP;

using Game;

using SharedLib;

using System;
using System.Threading;

namespace Core.Goals;

public sealed class StopMoving : IDisposable
{
    private readonly WowProcessInput input;
    private readonly PlayerReader playerReader;
    private readonly AddonBits bits;
    private readonly CancellationToken token;

    private float direction;

    public StopMoving(WowProcessInput input,
        PlayerReader playerReader,
        CancellationTokenSource<GoapAgent> cts,
        AddonBits bits)
    {
        this.input = input;
        this.playerReader = playerReader;
        this.token = cts.Token;
        this.bits = bits;
    }

    public void Dispose()
    {
        if (!bits.Moving())
            return;

        StopForward();
    }

    public void Stop()
    {
        StopForward();
        StopTurn();
    }

    public void StopForward()
    {
        if (!bits.Moving())
            return;

        if (input.IsKeyDown(input.ForwardKey))
        {
            input.SetKeyState(input.ForwardKey, false, true);
        }
        else if (input.IsKeyDown(input.BackwardKey))
        {
            input.SetKeyState(input.BackwardKey, false, true);
        }
        else // moving by interact key
        {
            input.PressFixed(input.ForwardKey, Random.Shared.Next(2, 5), token);
        }
    }

    public void StopTurn()
    {
        if (direction == playerReader.Direction)
            return;

        bool pressedAny = false;

        if (input.IsKeyDown(input.TurnLeftKey))
        {
            input.SetKeyState(input.TurnLeftKey, false, true);
            pressedAny = true;
        }
        else if (input.IsKeyDown(input.TurnRightKey))
        {
            input.SetKeyState(input.TurnRightKey, false, true);
            pressedAny = true;
        }

        if (pressedAny)
            token.WaitHandle.WaitOne(1);

        direction = playerReader.Direction;
    }
}