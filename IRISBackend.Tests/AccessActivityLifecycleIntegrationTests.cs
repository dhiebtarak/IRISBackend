using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using IRISBackend.DTOs;
using IRISBackend.Models;
using IRISBackend.Repositories;
using IRISBackend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IRISBackend.Tests;

public sealed class AccessActivityLifecycleIntegrationTests
{
    [Fact]
    public async Task SqlServer_LoginLogoutExpirationRevocationFiltersAndSummary()
    {
        // Always use a new isolated test database; never read application connection strings or secrets.
        var name = "IRIS_AccessActivity_Test_" + Guid.NewGuid().ToString("N");
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database={name};Integrated Security=true;TrustServerCertificate=true;Connect Timeout=5";
        var services = new ServiceCollection();
        services.AddLogging(); services.AddHttpContextAccessor(); services.AddDistributedMemoryCache();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["Jwt:Issuer"]="iris-test", ["Jwt:Audience"]="iris-test", ["Jwt:SigningKey"]=new string('t',64), ["Jwt:ExpirationMinutes"]="60"
        }).Build());
        services.AddDbContext<AppIdentityContext>(o => o.UseSqlServer(connection));
        services.AddDbContext<IRISBackendContext>(o => o.UseSqlServer(connection));
        services.AddAuthentication();
        services.AddIdentity<ApplicationUser, IdentityRole>(o => { o.Password.RequireNonAlphanumeric=false; })
            .AddEntityFrameworkStores<AppIdentityContext>().AddDefaultTokenProviders();
        services.AddScoped<ILoginAuditRepository, LoginAuditRepository>(); services.AddScoped<ILoginAuditService, LoginAuditService>();
        services.AddScoped<AuthSessionService>(); services.AddScoped<AccessActivityService>();
        services.AddScoped<ITokenService, TokenService>(); services.AddScoped<IAuthService, AuthService>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<AppIdentityContext>();
        var created = false;
        try
        {
            created = await identity.Database.EnsureCreatedAsync();
            await identity.Database.ExecuteSqlRawAsync(@"CREATE TABLE dbo.UserLoginAudits (
                Id int IDENTITY PRIMARY KEY, UserId nvarchar(450) NULL, EmailOrUsername nvarchar(256) NOT NULL,
                Succeeded bit NOT NULL, FailureReason nvarchar(200) NULL, IpAddress nvarchar(64) NULL,
                UserAgent nvarchar(512) NULL, CreatedAtUtc datetime2 NOT NULL,
                CONSTRAINT FK_TestAuditUser FOREIGN KEY(UserId) REFERENCES dbo.AspNetUsers(Id));");
            await identity.Database.ExecuteSqlRawAsync(@"INSERT dbo.UserLoginAudits (EmailOrUsername,Succeeded,CreatedAtUtc) VALUES ('legacy-success',1,'2020-01-01'),('legacy-failure',0,'2020-01-01')");
            var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Deployment","20261103_access_activity.sql"));
            await identity.Database.ExecuteSqlRawAsync(script);
            await identity.Database.ExecuteSqlRawAsync(script); // deployment is repeatable
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            Assert.True((await roles.CreateAsync(new IdentityRole("Employee"))).Succeeded);
            var user = new ApplicationUser { UserName="activity-user",Email="activity@example.test",FullName="Activity User",Department="Reassurance",Position="Front officer" };
            Assert.True((await users.CreateAsync(user,"TestPassword123!")).Succeeded);
            Assert.True((await users.AddToRoleAsync(user,"Employee")).Succeeded);
            var legacyDb = scope.ServiceProvider.GetRequiredService<IRISBackendContext>();
            Assert.Equal("LoginSuccessful", (await legacyDb.UserLoginAudits.SingleAsync(a=>a.EmailOrUsername=="legacy-success")).EventType);
            Assert.Equal("LoginFailed", (await legacyDb.UserLoginAudits.SingleAsync(a=>a.EmailOrUsername=="legacy-failure")).EventType);
            var http = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            http.HttpContext = new DefaultHttpContext { RequestServices=scope.ServiceProvider };
            http.HttpContext.Request.Headers.UserAgent="Test Browser";
            var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
            var sessions = scope.ServiceProvider.GetRequiredService<AuthSessionService>();
            var activity = scope.ServiceProvider.GetRequiredService<AccessActivityService>();
            var db = scope.ServiceProvider.GetRequiredService<IRISBackendContext>();
            var login = await auth.LoginAsync(new LoginRequestDto { EmailOrUsername="activity-user",Password="TestPassword123!" });
            Assert.True(login.Succeeded);
            var first = await db.AuthSessions.SingleAsync();
            var jwt = new JsonWebToken(login.Value!.Token);
            Assert.True(jwt.Claims.Any(c=>c.Type=="sid"), "Claim types: " + string.Join(",",jwt.Claims.Select(c=>c.Type)));
            Assert.Equal(first.SessionIdentifier,jwt.Claims.Single(c=>c.Type=="sid").Value);
            Assert.Single(await db.UserLoginAudits.Where(a=>a.EventType=="LoginSuccessful" && a.UserId==user.Id).ToListAsync());
            var failed = await auth.LoginAsync(new LoginRequestDto { EmailOrUsername="missing-user",Password="WrongPassword123!" });
            Assert.False(failed.Succeeded);Assert.Equal(401,failed.StatusCode);
            Assert.Null((await db.UserLoginAudits.SingleAsync(a=>a.EventType=="LoginFailed" && a.EmailOrUsername=="missing-user")).UserId);
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new System.Security.Claims.Claim(ClaimTypes.NameIdentifier,user.Id),new System.Security.Claims.Claim("sid",first.SessionIdentifier)],"test"));
            await sessions.LogoutAsync(principal);await sessions.LogoutAsync(principal);
            Assert.Equal("LoggedOut",first.Status);Assert.NotNull(first.LogoutAtUtc);
            Assert.Single(await db.UserLoginAudits.Where(a=>a.EventType=="Logout").ToListAsync());
            var second = await sessions.StartAsync(user);
            Assert.True(await sessions.ValidateAsync(second.SessionIdentifier,user.Id,false,default));
            Assert.Equal(1,await sessions.RevokeAsync(second.Id,null,user.Id,"Test revocation",default));
            Assert.Equal(0,await sessions.RevokeAsync(second.Id,null,user.Id,"Duplicate",default));
            Assert.False(await sessions.ValidateAsync(second.SessionIdentifier,user.Id,false,default));
            Assert.Equal(user.Id,second.RevokedByUserId);
            var expired = await sessions.StartAsync(user);
            expired.LoginAtUtc=DateTime.UtcNow.AddHours(-2);expired.LastActivityAtUtc=expired.LoginAtUtc;expired.ExpiresAtUtc=DateTime.UtcNow.AddHours(-1);
            await db.SaveChangesAsync();await sessions.MaintainAsync(365,default);await sessions.MaintainAsync(365,default);
            Assert.Equal("Expired",expired.Status);Assert.Null(expired.LogoutAtUtc);
            Assert.Single(await db.UserLoginAudits.Where(a=>a.EventType=="SessionExpired").ToListAsync());
            var summary=await activity.SummaryAsync(default);
            Assert.Equal(0,summary.ActiveSessions);Assert.Equal(3,summary.SuccessfulLoginsToday);Assert.Equal(1,summary.FailedLoginsLast24Hours);Assert.Equal(1,summary.RevokedSessionsToday);
            Assert.True(summary.AverageSessionDurationSeconds>0);
            var page=await activity.ListAsync(new AccessActivityQuery {UserId=user.Id,SessionStatus="Revoked",PageSize=1},null,default);
            Assert.Equal(1,page.TotalCount);Assert.Single(page.Items);Assert.Equal(second.Id,page.Items[0].SessionId);
            var failures=await activity.ListAsync(new AccessActivityQuery {Result="Failed"},null,default);
            Assert.Single(failures.Items);Assert.Null(failures.Items[0].SessionId);
            var history=await activity.HistoryAsync(user.Id,new AccessActivityQuery(),default);
            Assert.Equal(6,history.TotalCount);
        }
        finally { if(created) await identity.Database.EnsureDeletedAsync(); }
    }
}
