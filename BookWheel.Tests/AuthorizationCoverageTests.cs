using System.Reflection;
using BookWheel.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace BookWheel.Tests;

/// <summary>
/// Guards the authorization model: the app relies on a fallback policy (authenticated by
/// default), so the only way an action becomes reachable anonymously is an explicit
/// [AllowAnonymous]. These tests pin the exact anonymous surface and the admin-only surface,
/// so adding or loosening an endpoint is a deliberate, reviewed change.
/// </summary>
public sealed class AuthorizationCoverageTests
{
    private static readonly HashSet<string> ExpectedAnonymousActions = new(StringComparer.Ordinal)
    {
        "AuthController.Status",
        "AuthController.Setup",
        "AuthController.Login",
        "AuthController.Logout",
        "AuthController.CompletePasswordReset",
        "AuthController.RequestPasswordReset",
        "AuthController.ForgotUsername",
        "AuthController.ValidatePasswordResetToken",
        // MigrationController is class-level [AllowAnonymous] and enforces its own
        // "anonymous only before the first account, else admin" rule (retired by CORE-7).
        "MigrationController.Status",
        "MigrationController.Run"
    };

    private static readonly HashSet<string> ExpectedAdminActions = new(StringComparer.Ordinal)
    {
        "StatsController.GetAggregate",
        "MetricsController.GetMetrics",
        "UsersController.GetUsers",
        "UsersController.CreateUser",
        "UsersController.UpdateUser",
        "UsersController.CreatePasswordResetLink",
        "UsersController.DeleteUser"
    };

    private static IEnumerable<(string Name, MethodInfo Method)> Actions()
    {
        var controllers = typeof(AuthController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

        foreach (var controller in controllers)
        {
            var methods = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any());

            foreach (var method in methods)
            {
                yield return ($"{controller.Name}.{method.Name}", method);
            }
        }
    }

    private static bool IsAnonymous(MethodInfo method) =>
        method.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any() ||
        method.DeclaringType!.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();

    private static bool RequiresAdminPolicy(MethodInfo method) =>
        method.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Concat(method.DeclaringType!.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
            .Any(a => a.Policy == "Admin");

    [Fact]
    public void Controllers_Expose_Actions()
    {
        Assert.NotEmpty(Actions());
    }

    [Fact]
    public void Anonymous_Actions_Are_Exactly_The_Expected_Set()
    {
        var actual = Actions().Where(a => IsAnonymous(a.Method)).Select(a => a.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(ExpectedAnonymousActions.OrderBy(x => x), actual.OrderBy(x => x));
    }

    [Fact]
    public void Admin_Policy_Actions_Are_Exactly_The_Expected_Set()
    {
        var actual = Actions()
            .Where(a => !IsAnonymous(a.Method) && RequiresAdminPolicy(a.Method))
            .Select(a => a.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(ExpectedAdminActions.OrderBy(x => x), actual.OrderBy(x => x));
    }

    [Fact]
    public void Every_Other_Action_Is_Covered_By_The_Authenticated_Fallback_Policy()
    {
        // No action may carry a weaker, non-Admin [Authorize] policy or role that would
        // silently diverge from the fallback; everything non-anonymous/non-admin must be plain.
        var others = Actions().Where(a => !IsAnonymous(a.Method) && !RequiresAdminPolicy(a.Method)).ToList();

        Assert.NotEmpty(others);
        foreach (var (name, method) in others)
        {
            var custom = method.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                .Concat(method.DeclaringType!.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
                .Where(a => !string.IsNullOrEmpty(a.Policy) || !string.IsNullOrEmpty(a.Roles))
                .ToList();
            Assert.True(custom.Count == 0, $"{name} has an unexpected custom [Authorize] policy/role.");
        }
    }
}
