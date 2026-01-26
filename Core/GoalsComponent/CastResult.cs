using System;

namespace Core.Goals;

public enum CastResult
{
    Success,
    CurrentActionNotDetected,
    UIFeedbackNotDetected,
    TokenInterrupted,
    UIError
}

public static class CastResult_Extension
{
    public static string ToStringF(this CastResult value) => value switch
    {
        CastResult.Success => nameof(CastResult.Success),
        CastResult.CurrentActionNotDetected => nameof(CastResult.CurrentActionNotDetected),
        CastResult.UIFeedbackNotDetected => nameof(CastResult.UIFeedbackNotDetected),
        CastResult.TokenInterrupted => nameof(CastResult.TokenInterrupted),
        CastResult.UIError => nameof(CastResult.UIError),
        _ => throw new ArgumentNullException(nameof(value)),
    };
}
