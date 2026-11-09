using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using IRISBackend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace IRISBackend.Services;

public sealed class AccessActivityOptions
{
    public int RetentionDays { get; set; } = 365;
    public int ValidationCacheSeconds { get; set; } = 15;
}

public sealed class AuthSessionService(IRISBackendContext db, ILoginAuditService audit, IHttpContextAccessor accessor,
    IConfiguration configuration, IDistributedCache cache)
{
    private static string CacheKey(string sid) => "iris:session:" + sid;
    public async Task<AuthSession> StartAsync(ApplicationUser user, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var agent = LoginAuditService.SafeText(accessor.HttpContext?.Request.Headers.UserAgent.ToString(), 512);
        var session = new AuthSession
        {
            UserId = user.Id, SessionIdentifier = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            LoginAtUtc = now, LastActivityAtUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(configuration.GetValue("Jwt:ExpirationMinutes", 60)),
            IpAddress = accessor.HttpContext?.Connection.RemoteIpAddress?.ToString(), UserAgent = agent,
            Browser = agent?.Contains("Edg/") == true ? "Edge" : agent?.Contains("Firefox/") == true ? "Firefox" : agent?.Contains("Chrome/") == true ? "Chrome" : agent?.Contains("Safari/") == true ? "Safari" : "Unknown",
            OperatingSystem = agent?.Contains("Windows") == true ? "Windows" : agent?.Contains("Android") == true ? "Android" : agent?.Contains("iPhone") == true ? "iOS" : agent?.Contains("Mac OS") == true ? "macOS" : agent?.Contains("Linux") == true ? "Linux" : "Unknown",
            DeviceType = agent?.Contains("Mobile") == true ? "Mobile" : "Desktop / Unknown"
        };
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.AuthSessions.Add(session);
        await audit.RecordEventAsync(user.Id, user.UserName ?? "", "LoginSuccessful", session.Id, cancellationToken: ct);
        await tx.CommitAsync(ct);
        return session;
    }

    public async Task<bool> ValidateAsync(string? sid, string userId, bool logout, CancellationToken ct)
    {
        if (sid is null || sid.Length != 64) return false;
        var terminal = await cache.GetStringAsync(CacheKey(sid) + ":terminal", ct);
        if (terminal != null) return logout && terminal == "LoggedOut";
        var cached = await cache.GetStringAsync(CacheKey(sid), ct);
        if (cached == "Revoked" || cached == "Expired") return false;
        if (cached == "LoggedOut") return logout;
        if (cached == userId) return true;
        var session = await db.AuthSessions.AsNoTracking().SingleOrDefaultAsync(s => s.SessionIdentifier == sid && s.UserId == userId, ct);
        if (session is null || session.ExpiresAtUtc <= DateTime.UtcNow) return false;
        if (session.Status != "Active") return logout && session.Status == "LoggedOut";
        await cache.SetStringAsync(CacheKey(sid), userId, new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = new DateTimeOffset(DateTime.SpecifyKind(new[] { session.ExpiresAtUtc, DateTime.UtcNow.AddSeconds(configuration.GetValue("AccessActivity:ValidationCacheSeconds", 15)) }.Min(), DateTimeKind.Utc))
        }, ct);
        return true;
    }

    public async Task TouchAsync(string sid, CancellationToken ct)
    {
        // The distributed throttle avoids a write per request; SQL predicate also protects concurrent instances.
        var key = CacheKey(sid) + ":activity";
        if (await cache.GetStringAsync(key, ct) != null) return;
        var now = DateTime.UtcNow;
        await db.AuthSessions.Where(s => s.SessionIdentifier == sid && s.Status == "Active" && s.LastActivityAtUtc <= now.AddMinutes(-5))
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.LastActivityAtUtc, now).SetProperty(s => s.UpdatedAtUtc, now), ct);
        await cache.SetStringAsync(key, "1", new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) }, ct);
    }

    public async Task LogoutAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var sid = principal.FindFirstValue("sid");
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        await TransitionAsync(db.AuthSessions.Where(s => s.SessionIdentifier == sid && s.UserId == userId), "LoggedOut", null, null, ct);
    }

    public async Task<int> RevokeAsync(Guid? id, string? userId, string actorId, string reason, CancellationToken ct) =>
        await TransitionAsync(db.AuthSessions.Where(s => id.HasValue ? s.Id == id.Value : s.UserId == userId), "Revoked", actorId, reason, ct);

    private async Task<int> TransitionAsync(IQueryable<AuthSession> query, string status, string? actor, string? reason, CancellationToken ct)
    {
        // Serializable transaction makes the state change and audit insert atomic, including across instances.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var now = DateTime.UtcNow;
        var sessions = await query.Where(s => s.Status == "Active").ToListAsync(ct);
        foreach (var session in sessions)
        {
            var next = session.ExpiresAtUtc <= now ? "Expired" : status;
            session.Status = next; session.UpdatedAtUtc = now;
            if (next == "LoggedOut") session.LogoutAtUtc = now;
            if (next == "Revoked") { session.RevokedAtUtc = now; session.RevokedByUserId = actor; session.RevocationReason = reason; }
            await audit.RecordEventAsync(session.UserId, "", next == "LoggedOut" ? "Logout" : next == "Expired" ? "SessionExpired" : "SessionRevoked",
                session.Id, actor, reason, ct);
        }
        await db.SaveChangesAsync(ct);
        foreach (var session in sessions)
            await cache.SetStringAsync(CacheKey(session.SessionIdentifier) + ":terminal", session.Status,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes( Math.Max(1, (session.ExpiresAtUtc - now).TotalMinutes + 1)) }, ct);
        await tx.CommitAsync(ct);
        return sessions.Count(s => s.Status == status);
    }

    public async Task MaintainAsync(int retentionDays, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await TransitionAsync(db.AuthSessions.Where(s => s.Status == "Active" && s.ExpiresAtUtc <= now).OrderBy(s => s.ExpiresAtUtc).Take(200), "Expired", null, null, ct);
        var cutoff = now.AddDays(-retentionDays);
        // Bounded, repeatable cleanup. Events precede sessions because of the restrictive foreign key.
        await db.UserLoginAudits.Where(a => a.CreatedAtUtc < cutoff).OrderBy(a => a.Id).Take(1000).ExecuteDeleteAsync(ct);
        await db.AuthSessions.Where(s => s.Status != "Active" && s.ExpiresAtUtc < cutoff && !db.UserLoginAudits.Any(a => a.SessionId == s.Id))
            .OrderBy(s => s.ExpiresAtUtc).Take(200).ExecuteDeleteAsync(ct);
    }
}

public sealed class AccessActivityWorker(IServiceScopeFactory scopes, ILogger<AccessActivityWorker> logger,
    Microsoft.Extensions.Options.IOptions<AccessActivityOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<AuthSessionService>().MaintainAsync(options.Value.RetentionDays, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Session maintenance failed; it will retry on the next interval."); }
        }
    }
}
