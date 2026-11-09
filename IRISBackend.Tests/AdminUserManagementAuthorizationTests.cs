using IRISBackend.Controllers;
using IRISBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace IRISBackend.Tests;

public sealed class AdminUserManagementAuthorizationTests
{
    [Theory]
    [InlineData(nameof(UsersController.CreateUser))]
    [InlineData(nameof(UsersController.ListUsers))]
    [InlineData(nameof(UsersController.GetModules))]
    [InlineData(nameof(UsersController.SetModules))]
    [InlineData(nameof(UsersController.SetAccountStatus))]
    public void UserProvisioningAndManagementActionsRequireAdmin(string actionName)
    {
        var action = typeof(UsersController).GetMethod(actionName);
        Assert.NotNull(action);
        Assert.Contains(action!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>(), attribute =>
                attribute.Roles?.Split(',').Select(role => role.Trim()).Contains(IdentityRoles.Admin) == true);
    }

    [Fact]
    public void PublicAuthControllerDoesNotExposeRegistrationAction()
    {
        Assert.DoesNotContain(typeof(AuthController).GetMethods(), method =>
            string.Equals(method.Name, "Register", StringComparison.OrdinalIgnoreCase));
    }
}
