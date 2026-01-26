using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

using System.Threading.Tasks;

namespace PathingAPI.RateLimit;

public sealed class RateLimitFilter : IAsyncActionFilter
{
    private static bool isBusy;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (isBusy)
        {
            context.Result = new StatusCodeResult(StatusCodes.Status429TooManyRequests);
            return;
        }

        isBusy = true;

        await next();

        isBusy = false;
    }
}