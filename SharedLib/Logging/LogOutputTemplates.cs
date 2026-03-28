namespace SharedLib.Logging;

public static class LogOutputTemplates
{
    /// <summary>
    /// Full template with short source context (requires ShortSourceContextEnricher).
    /// </summary>
    public const string Default =
        "[{@t:HH:mm:ss:fff} {@l:u1}] {#if IsDefined(ShortSource)}[{ShortSource,-17}] {#end}{@m}\n{@x}";

    /// <summary>
    /// Minimal template for benchmarks — message and exception only.
    /// </summary>
    public const string Minimal = "{@m}\n{@x}";
}
