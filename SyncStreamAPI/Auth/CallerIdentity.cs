using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Auth;

/// <summary>
///     Who is calling, resolved from the connection rather than from a method argument.
/// </summary>
/// <remarks>
///     Room membership used to trust a <c>username</c> string passed by the client, and privileges were
///     looked up from the database by that string, so claiming someone else's name claimed their privileges.
///     Nothing reads a caller's identity from arguments any more.
/// </remarks>
public sealed class CallerIdentity
{
    private CallerIdentity(string connectionId, int? userId, string? accountName, UserPrivileges privileges,
        bool approved)
    {
        ConnectionId = connectionId;
        UserId = userId;
        AccountName = accountName;
        Privileges = privileges;
        IsApproved = approved;
    }

    public string ConnectionId { get; }

    /// <summary>Null for a guest.</summary>
    public int? UserId { get; }

    /// <summary>The account's username, or null for a guest. Never client-supplied.</summary>
    public string? AccountName { get; }

    public UserPrivileges Privileges { get; }

    public bool IsApproved { get; }

    public bool IsGuest => UserId == null;

    /// <summary>
    ///     A stable identity for this caller that survives a reconnect for accounts and does not for guests.
    ///     Used to key room membership and bans.
    /// </summary>
    public string Key => UserId?.ToString() is { } id ? $"user:{id}" : $"guest:{ConnectionId}";

    public static CallerIdentity Guest(string connectionId)
    {
        return new CallerIdentity(connectionId, null, null, UserPrivileges.NotApproved, false);
    }

    public static CallerIdentity From(ClaimsPrincipal? principal, string connectionId)
    {
        if (principal?.Identity?.IsAuthenticated != true) return Guest(connectionId);

        var userIdClaim = principal.FindFirst(SyncStreamClaims.UserId)?.Value;
        if (!int.TryParse(userIdClaim, out var userId)) return Guest(connectionId);

        var privileges = UserPrivileges.NotApproved;
        if (int.TryParse(principal.FindFirst(SyncStreamClaims.Privilege)?.Value, out var privilegeValue))
            privileges = (UserPrivileges)privilegeValue;

        var approved = bool.TryParse(principal.FindFirst(SyncStreamClaims.Approved)?.Value, out var parsed) && parsed;

        return new CallerIdentity(connectionId, userId, principal.Identity.Name, privileges, approved);
    }

    public bool HasAtLeast(UserPrivileges required)
    {
        return IsApproved && Privileges >= required;
    }
}

public static class CallerIdentityExtensions
{
    public static CallerIdentity GetCaller(this HubCallerContext context)
    {
        return CallerIdentity.From(context.User, context.ConnectionId);
    }

    public static CallerIdentity GetCaller(this ControllerBase controller)
    {
        return CallerIdentity.From(controller.User, string.Empty);
    }

    /// <summary>The authenticated account's id, or null when the request is anonymous.</summary>
    public static int? CallerUserId(this ControllerBase controller)
    {
        return controller.GetCaller().UserId;
    }
}
