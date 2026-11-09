using System.Reflection;
using System.Security.Claims;
using IRISBackend.Controllers;
using IRISBackend.DTOs;
using IRISBackend.Models;
using IRISBackend.Repositories;
using IRISBackend.Services;
using IRISBackend.Validators;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace IRISBackend.Tests;
public sealed class AccessActivityTests
{
    private static IRISBackendContext Db() => new(new DbContextOptionsBuilder<IRISBackendContext>()
        .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=AccessActivityTranslationOnly;Integrated Security=true").Options);
    [Fact]
    public void SessionAndFailureUnionTranslatesToSqlServer()
    {
        using var db = Db();
        var service = new AccessActivityService(db);
        var sql = service.Rows(DateTime.UtcNow).Where(r => r.Result == "Failed" || r.SessionStatus == "Active")
            .OrderByDescending(r => r.LoginAtUtc).Skip(20).Take(20).ToQueryString();
        Assert.Contains("UNION ALL", sql);
        Assert.Contains("OFFSET", sql);
        Assert.DoesNotContain("PasswordHash", sql);
        Assert.DoesNotContain("SecurityStamp", sql);
        Assert.DoesNotContain("SessionIdentifier", sql);
    }
    [Theory]
    [InlineData(0, 20)] [InlineData(1, 101)] [InlineData(1, 0)]
    public void InvalidPagingIsRejected(int page, int size) => Assert.False(new AccessActivityQueryValidator()
        .Validate(new AccessActivityQuery() { PageNumber = page, PageSize = size }).IsValid);
    [Fact]
    public void InvalidFiltersAndSortAreRejected()
    {
        var result = new AccessActivityQueryValidator().Validate(new AccessActivityQuery() { SessionStatus = "Anything", Result = "Unknown", SortBy = "PasswordHash", SortDirection = "sql" });
        Assert.Equal(4, result.Errors.Count);
    }
    [Fact]
    public void DateRangeAndUtcAreValidated()
    {
        var now = DateTime.UtcNow;
        Assert.False(new AccessActivityQueryValidator().Validate(new AccessActivityQuery() { DateFrom = now, DateTo = now.AddDays(-1) }).IsValid);
        Assert.False(new AccessActivityQueryValidator().Validate(new AccessActivityQuery() { DateFrom = DateTime.SpecifyKind(now, DateTimeKind.Unspecified) }).IsValid);
    }
    [Theory]
    [InlineData("")] [InlineData("bad\nreason")]
    public void UnsafeRevocationReasonsAreRejected(string reason) => Assert.False(new RevokeSessionRequestValidator().Validate(new RevokeSessionRequest() { Reason = reason }).IsValid);
    [Fact]
    public void AdministratorIsExcludedFromOperationalFinancialAccessEvenWithModuleClaims()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new System.Security.Claims.Claim(ClaimTypes.Role, "Admin"), new System.Security.Claims.Claim(ModuleAccessCatalog.ClaimType, ModuleAccessCatalog.BackOffice)], "test"));
        Assert.False(FinancialAccessRules.IsFinanceOrBackOfficer(principal));
    }
    [Fact]
    public void EveryActivityEndpointRequiresAdministrator()
    {
        Assert.Equal("Admin", typeof(AccessActivityController).GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.DoesNotContain(typeof(AccessActivityController).GetMethods(), m => m.GetCustomAttribute<AllowAnonymousAttribute>() != null);
    }
    [Fact]
    public async Task CachedRevokedSessionIsImmediatelyRejectedAndLogoutIsIdempotentlyAllowed()
    {
        using var db = Db();
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var service = new AuthSessionService(db, null!, new HttpContextAccessor(), new ConfigurationBuilder().Build(), cache);
        var sid = new string('A', 64);
        await cache.SetStringAsync("iris:session:" + sid, "user-1");
        Assert.True(await service.ValidateAsync(sid, "user-1", false, default));
        await cache.SetStringAsync("iris:session:" + sid + ":terminal", "Revoked");
        Assert.False(await service.ValidateAsync(sid, "user-1", false, default));
        Assert.False(await service.ValidateAsync(sid, "user-1", true, default));
        await cache.SetStringAsync("iris:session:" + sid + ":terminal", "LoggedOut");
        Assert.True(await service.ValidateAsync(sid, "user-1", true, default));
        Assert.False(await service.ValidateAsync(sid, "user-1", false, default));
        Assert.False(await service.ValidateAsync(null, "user-1", false, default));
    }
    [Fact]
    public async Task FailedLoginAuditHasSafeMetadataAndNoCredentials()
    {
        var repo = new AuditRepository();
        var http = new DefaultHttpContext(); http.Request.Headers.UserAgent = "Browser\r\n" + new string('x', 600);
        var service = new LoginAuditService(repo, new HttpContextAccessor { HttpContext = http });
        await service.RecordAsync(null, "unknown\nuser", false, "Invalid password.");
        Assert.NotNull(repo.Last); Assert.Null(repo.Last!.UserId);
        Assert.Equal("LoginFailed", repo.Last.EventType); Assert.False(repo.Last.Succeeded);
        Assert.Equal("unknownuser", repo.Last.EmailOrUsername); Assert.Equal(512, repo.Last.UserAgent.Length);
        Assert.DoesNotContain('\n', repo.Last.UserAgent);
    }
    [Theory]
    [InlineData(60, 60)] [InlineData(3600, 3600)] [InlineData(-1, 0)]
    public void DurationIsDerivedAndNonNegative(int seconds, int expected)
    {
        var login = DateTime.UtcNow; Assert.Equal(expected, AccessActivityService.Duration(login, login.AddSeconds(seconds)));
    }
    private sealed class AuditRepository : GenericRepository<UserLoginAudit>, ILoginAuditRepository
    {
        public UserLoginAudit? Last;
        public AuditRepository() : base(Db()) { }
        // Interface dispatch is explicit because GenericRepository methods are not virtual.
        Task IGenericRepository<UserLoginAudit>.AddAsync(UserLoginAudit entity, CancellationToken ct) { Last = entity; return Task.CompletedTask; }
        Task<int> IGenericRepository<UserLoginAudit>.SaveChangesAsync(CancellationToken ct) => Task.FromResult(1);
    }
}
