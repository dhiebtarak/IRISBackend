using IRISBackend.Controllers;
using IRISBackend.DTOs;
using IRISBackend.Models;
using IRISBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IRISBackend.Tests;

public class UserProvisioningRulesTests
{
    [Theory]
    [InlineData("Front officer")]
    [InlineData("Back officer")]
    [InlineData("Finance officer")]
    public void Reassurance_officers_are_allowed_and_normalized(string position)
    {
        var request = ValidRequest();
        request.Department = "Reassurance";
        request.Position = position;

        Assert.Empty(RegistrationRules.Validate(request));
        Assert.Equal(("Reassurance", position), RegistrationRules.Normalize(request.Department, request.Position));
    }

    [Fact]
    public void Unsupported_department_position_pair_is_rejected()
    {
        var request = ValidRequest();
        request.Department = "Reassurance";
        request.Position = "Administrator";

        Assert.Contains(RegistrationRules.Validate(request), error => error.StartsWith("Position:"));
    }

    [Fact]
    public void Invalid_password_confirmation_and_fields_are_rejected()
    {
        var request = ValidRequest();
        request.FullName = " ";
        request.Username = "ab";
        request.Email = "invalid";
        request.Password = "weak";
        request.ConfirmPassword = "different";
        request.Department = "Other";

        var errors = RegistrationRules.Validate(request).ToArray();
        Assert.Contains(errors, error => error.StartsWith("FullName:"));
        Assert.Contains(errors, error => error.StartsWith("Username:"));
        Assert.Contains(errors, error => error.StartsWith("Email:"));
        Assert.Contains(errors, error => error.StartsWith("Password:"));
        Assert.Contains(errors, error => error.StartsWith("ConfirmPassword:"));
        Assert.Contains(errors, error => error.StartsWith("Department:"));
    }

    [Fact]
    public void User_creation_route_is_admin_only_and_public_registration_route_is_absent()
    {
        var action = typeof(UsersController).GetMethod(nameof(UsersController.CreateUser));
        Assert.NotNull(action);
        var authorization = action!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>().Single();
        Assert.Equal(IdentityRoles.Admin, authorization.Roles);

        Assert.Null(typeof(AuthController).GetMethod("Register"));
    }

    [Theory]
    [InlineData("Front officer")]
    [InlineData("Back officer")]
    [InlineData("Finance officer")]
    public async Task Provisioning_assigns_only_the_employee_role_for_officer_accounts(string position)
    {
        var users = new FakeUserManager();
        var service = new AuthService(users, null!, new FakeRoleManager(), null!, null!,
            NullLogger<AuthService>.Instance);
        var request = ValidRequest();
        request.Position = position;

        var result = await service.CreateUserAsync(request);

        Assert.True(result.Succeeded);
        Assert.Equal(StatusCodes.Status201Created, result.StatusCode);
        Assert.Equal(new[] { IdentityRoles.Employee }, users.AssignedRoles);
        Assert.Equal(IdentityRoles.Employee, Assert.Single(result.Value!.Roles));
        Assert.DoesNotContain(result.Value.GetType().GetProperties(), property =>
            property.Name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Duplicate_username_and_email_return_conflicts_without_creating_a_user()
    {
        var existing = new ApplicationUser { Id = "existing", UserName = "alex.officer", Email = "alex@example.test" };
        var users = new FakeUserManager(existing);
        var service = new AuthService(users, null!, new FakeRoleManager(), null!, null!,
            NullLogger<AuthService>.Instance);

        var usernameResult = await service.CreateUserAsync(ValidRequest());
        Assert.Equal(StatusCodes.Status409Conflict, usernameResult.StatusCode);
        Assert.Equal("DUPLICATE_USERNAME", usernameResult.Error!.Code);

        var emailRequest = ValidRequest();
        emailRequest.Username = "other.user";
        var emailResult = await service.CreateUserAsync(emailRequest);
        Assert.Equal(StatusCodes.Status409Conflict, emailResult.StatusCode);
        Assert.Equal("DUPLICATE_EMAIL", emailResult.Error!.Code);
        Assert.Empty(users.CreatedUsers);
    }

    [Fact]
    public async Task Failed_role_assignment_is_not_reported_as_success_and_created_user_is_removed()
    {
        var users = new FakeUserManager { RoleAssignmentSucceeds = false };
        var service = new AuthService(users, null!, new FakeRoleManager(), null!, null!,
            NullLogger<AuthService>.Instance);

        var result = await service.CreateUserAsync(ValidRequest());

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
        Assert.Empty(users.CreatedUsers);
    }

    private sealed class FakeUserManager : UserManager<ApplicationUser>
    {
        private readonly List<ApplicationUser> _existing;
        public List<ApplicationUser> CreatedUsers { get; } = [];
        public List<string> AssignedRoles { get; } = [];
        public bool RoleAssignmentSucceeds { get; set; } = true;

        public FakeUserManager(params ApplicationUser[] existing) : base(
            new FakeUserStore(), Microsoft.Extensions.Options.Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(),
            [], [], new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(),
            null!, NullLogger<UserManager<ApplicationUser>>.Instance)
        {
            _existing = existing.ToList();
        }

        public override Task<ApplicationUser?> FindByNameAsync(string userName) =>
            Task.FromResult(_existing.Concat(CreatedUsers).FirstOrDefault(user =>
                string.Equals(user.UserName, userName, StringComparison.OrdinalIgnoreCase)));

        public override Task<ApplicationUser?> FindByEmailAsync(string email) =>
            Task.FromResult(_existing.Concat(CreatedUsers).FirstOrDefault(user =>
                string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase)));

        public override Task<IdentityResult> CreateAsync(ApplicationUser user, string password)
        {
            user.Id = $"created-{CreatedUsers.Count + 1}";
            CreatedUsers.Add(user);
            return Task.FromResult(IdentityResult.Success);
        }

        public override Task<IdentityResult> AddToRoleAsync(ApplicationUser user, string role)
        {
            AssignedRoles.Add(role);
            return Task.FromResult(RoleAssignmentSucceeds
                ? IdentityResult.Success
                : IdentityResult.Failed(new IdentityError { Code = "RoleFailure", Description = "Role assignment failed." }));
        }

        public override Task<IdentityResult> DeleteAsync(ApplicationUser user)
        {
            CreatedUsers.Remove(user);
            return Task.FromResult(IdentityResult.Success);
        }
    }

    private sealed class FakeRoleManager : RoleManager<IdentityRole>
    {
        public FakeRoleManager() : base(new FakeRoleStore(), [], new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), NullLogger<RoleManager<IdentityRole>>.Instance) { }

        public override Task<bool> RoleExistsAsync(string roleName) => Task.FromResult(roleName == IdentityRoles.Employee);
    }

    private sealed class FakeUserStore : IUserStore<ApplicationUser>
    {
        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) => Task.FromResult<ApplicationUser?>(null);
        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) => Task.FromResult<ApplicationUser?>(null);
        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedUserName);
        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id);
        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);
        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class FakeRoleStore : IRoleStore<IdentityRole>
    {
        public Task<IdentityResult> CreateAsync(IdentityRole role, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> UpdateAsync(IdentityRole role, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> DeleteAsync(IdentityRole role, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<string> GetRoleIdAsync(IdentityRole role, CancellationToken cancellationToken) => Task.FromResult(role.Id);
        public Task<string?> GetRoleNameAsync(IdentityRole role, CancellationToken cancellationToken) => Task.FromResult(role.Name);
        public Task SetRoleNameAsync(IdentityRole role, string? roleName, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string?> GetNormalizedRoleNameAsync(IdentityRole role, CancellationToken cancellationToken) => Task.FromResult(role.NormalizedName);
        public Task SetNormalizedRoleNameAsync(IdentityRole role, string? normalizedName, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IdentityRole?> FindByIdAsync(string roleId, CancellationToken cancellationToken) => Task.FromResult<IdentityRole?>(null);
        public Task<IdentityRole?> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken) => Task.FromResult<IdentityRole?>(null);
        public void Dispose() { }
    }

    private static RegisterRequestDto ValidRequest() => new()
    {
        FullName = "Alex Officer",
        Username = "alex.officer",
        Email = "alex@example.test",
        Password = "Strong!1",
        ConfirmPassword = "Strong!1",
        Department = "Reassurance",
        Position = "Front officer"
    };
}
