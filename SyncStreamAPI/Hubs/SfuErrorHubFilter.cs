using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using SyncStreamAPI.ServerData;

namespace SyncStreamAPI.Hubs;

/// <summary>
///     Turns a failed request to the SFU into a message the client is allowed to show.
/// </summary>
/// <remarks>
///     With detailed errors off, any exception other than a <see cref="HubException" /> reaches the client as
///     "An unexpected error occurred", so a media server that was down, or that refused a file it could not read,
///     looked exactly like a bug.
/// </remarks>
public class SfuErrorHubFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            return await next(invocationContext);
        }
        catch (SfuRequestException ex)
        {
            Console.WriteLine($"[SFU] {invocationContext.HubMethodName} failed: {ex.StatusCode} {ex.Message}");
            throw new HubException(ex.Message);
        }
    }
}
