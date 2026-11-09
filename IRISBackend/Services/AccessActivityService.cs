using IRISBackend.DTOs;
using IRISBackend.Models;
using Microsoft.EntityFrameworkCore;
namespace IRISBackend.Services;

public sealed class AccessActivityService(IRISBackendContext db)
{
    public Task<bool> UserExistsAsync(string userId, CancellationToken ct) => db.AspNetUsers.AsNoTracking().AnyAsync(u => u.Id == userId, ct);
    internal IQueryable<AccessActivityRow> Rows(DateTime now)
    {
        var sessions = db.AuthSessions.AsNoTracking().Select(s => new AccessActivityRow
        {
            SessionId = s.Id, AuditEventId = null, UserId = s.UserId, FullName = s.User.FullName, Username = s.User.UserName,
            Email = s.User.Email, Role = s.User.Roles.Any(r => r.Name == "Admin") ? "Admin" : s.User.Position,
            Department = s.User.Department,
            AccountStatus = s.User.LockoutEnabled && s.User.LockoutEnd > now ? (s.User.LockoutEnd > now.AddYears(10) ? "Disabled" : "Locked") : "Enabled",
            LoginAtUtc = s.LoginAtUtc, LastActivityAtUtc = s.LastActivityAtUtc, LogoutAtUtc = s.LogoutAtUtc,
            ExpiresAtUtc = s.ExpiresAtUtc, RevokedAtUtc = s.RevokedAtUtc,
            SessionStatus = s.Status == "Active" && s.ExpiresAtUtc <= now ? "Expired" : s.Status,
            IpAddress = s.IpAddress, UserAgent = s.UserAgent, Browser = s.Browser, OperatingSystem = s.OperatingSystem,
            DeviceType = s.DeviceType, RevocationReason = s.RevocationReason, RevokedByUserId = s.RevokedByUserId,
            Result = "Successful", EventType = "LoginSuccessful"
        });
        // Failed attempts have no session and may have no known user. Include them in the same paged view.
        var failures = db.UserLoginAudits.AsNoTracking().Where(a => a.EventType == "LoginFailed" || a.EventType == "AccountLocked" || a.EventType == "AccountUnlocked").Select(a => new AccessActivityRow
        {
            SessionId = null, AuditEventId = a.Id, UserId = a.UserId, FullName = a.User == null ? "Unknown user" : a.User.FullName,
            Username = a.EmailOrUsername, Email = a.User == null ? "" : a.User.Email,
            Role = a.User == null ? "" : a.User.Roles.Any(r => r.Name == "Admin") ? "Admin" : a.User.Position,
            Department = a.User == null ? "" : a.User.Department,
            AccountStatus = a.User == null ? "Unknown" : a.User.LockoutEnabled && a.User.LockoutEnd > now ? (a.User.LockoutEnd > now.AddYears(10) ? "Disabled" : "Locked") : "Enabled",
            LoginAtUtc = a.CreatedAtUtc, LastActivityAtUtc = null, LogoutAtUtc = null, ExpiresAtUtc = null, RevokedAtUtc = null,
            SessionStatus = null, IpAddress = a.IpAddress, UserAgent = a.UserAgent, Browser = null, OperatingSystem = null,
            DeviceType = null, RevocationReason = null, RevokedByUserId = null, Result = a.EventType == "LoginFailed" ? "Failed" : "Successful", EventType = a.EventType
        });
        return sessions.Concat(failures);
    }

    public async Task<ActivityPage<AccessActivityRow>> ListAsync(AccessActivityQuery q, string? currentSid, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var rows = Rows(now);
        if (!string.IsNullOrWhiteSpace(q.Search)) rows = rows.Where(r => r.FullName.Contains(q.Search) || r.Username.Contains(q.Search) || r.Email.Contains(q.Search));
        if (q.UserId != null) rows = rows.Where(r => r.UserId == q.UserId);
        if (q.Role != null) rows = rows.Where(r => r.Role == q.Role);
        if (q.Department != null) rows = rows.Where(r => r.Department == q.Department);
        if (q.AccountStatus != null) rows = rows.Where(r => r.AccountStatus == q.AccountStatus);
        if (q.SessionStatus != null) rows = rows.Where(r => r.SessionStatus == q.SessionStatus);
        if (q.Result != null) rows = rows.Where(r => r.Result == q.Result);
        if (q.EventType != null && new[] { "Logout", "SessionExpired", "SessionRevoked" }.Contains(q.EventType))
            rows = rows.Where(r => db.UserLoginAudits.Any(a => a.EventType == q.EventType &&
                (r.SessionId != null ? a.SessionId == r.SessionId : a.UserId == r.UserId) &&
                (!q.DateFrom.HasValue || a.CreatedAtUtc >= q.DateFrom) && (!q.DateTo.HasValue || a.CreatedAtUtc <= q.DateTo)));
        else if (q.EventType != null) rows = rows.Where(r => r.EventType == q.EventType);
        if (q.EventType == null || !new[] { "Logout", "SessionExpired", "SessionRevoked" }.Contains(q.EventType))
        {
            if (q.DateFrom.HasValue) rows = rows.Where(r => r.LoginAtUtc >= q.DateFrom);
            if (q.DateTo.HasValue) rows = rows.Where(r => r.LoginAtUtc <= q.DateTo);
        }
        var total = await rows.CountAsync(ct);
        var asc = q.SortDirection == "asc";
        var sorted = q.SortBy switch
        {
            "username" => asc ? rows.OrderBy(r => r.Username) : rows.OrderByDescending(r => r.Username),
            "lastActivityAtUtc" => asc ? rows.OrderBy(r => r.LastActivityAtUtc) : rows.OrderByDescending(r => r.LastActivityAtUtc),
            "sessionStatus" => asc ? rows.OrderBy(r => r.SessionStatus) : rows.OrderByDescending(r => r.SessionStatus),
            _ => asc ? rows.OrderBy(r => r.LoginAtUtc) : rows.OrderByDescending(r => r.LoginAtUtc)
        };
        var items = await sorted.ThenBy(r => r.SessionId).ThenBy(r => r.AuditEventId).Skip((q.PageNumber - 1) * q.PageSize).Take(q.PageSize).ToListAsync(ct);
        var currentId = currentSid == null ? (Guid?)null : await db.AuthSessions.Where(s => s.SessionIdentifier == currentSid).Select(s => (Guid?)s.Id).SingleOrDefaultAsync(ct);
        foreach (var row in items) Complete(row, now, currentId);
        return new(items, total, q.PageNumber, q.PageSize);
    }

    public async Task<AccessActivityRow?> DetailAsync(Guid id, string? sid, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var row = await Rows(now).SingleOrDefaultAsync(r => r.SessionId == id, ct);
        if (row == null) return null;
        var current = await db.AuthSessions.AnyAsync(s => s.Id == id && s.SessionIdentifier == sid, ct);
        Complete(row, now, current ? id : null);
        return row;
    }
    public static double Duration(DateTime login, DateTime end) => Math.Max(0, (end - login).TotalSeconds);
    private static void Complete(AccessActivityRow row, DateTime now, Guid? currentId)
    {
        if (row.SessionId == null) return;
        row.DurationSeconds = Duration(row.LoginAtUtc, row.SessionStatus switch
        {
            "LoggedOut" => row.LogoutAtUtc ?? now, "Revoked" => row.RevokedAtUtc ?? now,
            "Expired" => row.ExpiresAtUtc ?? now, _ => now
        });
        row.IsCurrentSession = row.SessionId == currentId;
    }
    public async Task<ActivityPage<AccessAuditEvent>> HistoryAsync(string userId, AccessActivityQuery q, CancellationToken ct)
    {
        var query = db.UserLoginAudits.AsNoTracking().Where(a => a.UserId == userId);
        if (q.EventType != null) query = query.Where(a => a.EventType == q.EventType);
        if (q.Result != null) query = query.Where(a => a.Succeeded == (q.Result == "Successful"));
        if (q.DateFrom.HasValue) query = query.Where(a => a.CreatedAtUtc >= q.DateFrom);
        if (q.DateTo.HasValue) query = query.Where(a => a.CreatedAtUtc <= q.DateTo);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.Id)
            .Skip((q.PageNumber - 1) * q.PageSize).Take(q.PageSize)
            .Select(a => new AccessAuditEvent(a.Id, a.EventType, a.CreatedAtUtc, a.EmailOrUsername,
                a.FailureReason, a.IpAddress, a.UserAgent, a.SessionId, a.ActorUserId)).ToListAsync(ct);
        return new(items, total, q.PageNumber, q.PageSize);
    }
    public async Task<AccessActivitySummary> SummaryAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow; var today = now.Date; var since = now.AddHours(-24);
        var active = await db.AuthSessions.CountAsync(s => s.Status == "Active" && s.ExpiresAtUtc > now, ct);
        var success = await db.UserLoginAudits.CountAsync(a => a.EventType == "LoginSuccessful" && a.CreatedAtUtc >= today, ct);
        var failed = await db.UserLoginAudits.CountAsync(a => a.EventType == "LoginFailed" && a.CreatedAtUtc >= since, ct);
        var locked = await db.AspNetUsers.CountAsync(u => u.LockoutEnabled && u.LockoutEnd > now, ct);
        var revoked = await db.AuthSessions.CountAsync(s => s.RevokedAtUtc >= today, ct);
        var average = await db.AuthSessions.Select(s => (double?)EF.Functions.DateDiffSecond(s.LoginAtUtc,
            s.Status == "LoggedOut" ? s.LogoutAtUtc!.Value : s.Status == "Revoked" ? s.RevokedAtUtc!.Value :
            s.Status == "Expired" || s.ExpiresAtUtc <= now ? s.ExpiresAtUtc : now)).AverageAsync(ct) ?? 0;
        return new(active, success, failed, locked, revoked, Math.Max(0, average));
    }
}
