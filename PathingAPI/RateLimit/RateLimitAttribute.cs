using Microsoft.AspNetCore.Mvc;

namespace PathingAPI.RateLimit;

public sealed class RateLimitAttribute : TypeFilterAttribute
{
    public RateLimitAttribute() : base(typeof(RateLimitFilter))
    {
    }
}
