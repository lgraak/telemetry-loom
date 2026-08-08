using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace TelemetryLoom.Service.Access;

public static class AccessPolicies
{
    public const string Read = "TelemetryRead";
    public const string LocalAdmin = "TelemetryLocalAdmin";
}

public sealed record AccessRequirement(RequestAuthority MinimumAuthority) : IAuthorizationRequirement;

public sealed class AccessAuthorizationHandler(RequestAuthorityClassifier classifier)
    : AuthorizationHandler<AccessRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AccessRequirement requirement)
    {
        if (context.Resource is HttpContext httpContext &&
            classifier.Classify(httpContext) >= requirement.MinimumAuthority)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public sealed class AccessAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    public Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Succeeded)
        {
            return next(context);
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
