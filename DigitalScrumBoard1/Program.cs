using DigitalScrumBoard1.Data;
using DigitalScrumBoard1.Data.Seed;
using DigitalScrumBoard1.Hubs;
using DigitalScrumBoard1.Repositories;
using DigitalScrumBoard1.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

builder.Services.AddOpenApi(options =>
{
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
});

builder.Services.AddDbContext<DigitalScrumBoardContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddAuthentication("MyCookieAuth")
    .AddCookie("MyCookieAuth", options =>
    {
        options.Cookie.Name = "DigitalScrumBoardAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.IsEssential = true;

        options.LoginPath = "/api/auth/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;

        // REST API: do not redirect; return proper HTTP codes.
        options.Events = new Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationEvents
        {
            OnRedirectToLogin = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";

        int retryAfterSeconds = 60;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) &&
            retryAfter > TimeSpan.Zero)
        {
            retryAfterSeconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
            context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
        }

        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            message = "Too many requests. Please try again later.",
            code = "RATE_LIMITED",
            retryAfterSeconds
        }, cancellationToken: ct);
    };

    // Broad endpoint abuse protection only.
    // The stepped login cooldown/lockout is now handled inside AuthController.
    options.AddFixedWindowLimiter("LoginLimiter", limiter =>
    {
        limiter.PermitLimit = 20;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("DSB", policy =>
        policy.WithOrigins(
                "http://192.168.19.18:7120"
            )
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
    );
});

// SignalR
builder.Services
    .AddSignalR()
    .AddJsonProtocol(options =>
    {
        // Keep hub payloads consistent with frontend expectations.
        options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PayloadSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    });

// Email options + sender
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();

builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IAuthEmailService, AuthEmailService>();
builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddScoped<ITeamService, TeamService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<IWorkItemRepository, WorkItemRepository>();
builder.Services.AddScoped<ILookupRepository, LookupRepository>();
builder.Services.AddScoped<ILookupService, LookupService>();
builder.Services.AddScoped<ISprintRepository, SprintRepository>();
builder.Services.AddScoped<IBoardRepository, BoardRepository>();
builder.Services.AddScoped<IBoardService, BoardService>();

// Notifications module
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<INotificationService, NotificationService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseRateLimiter();
app.UseHttpsRedirection();

app.UseRouting();

app.UseCors("DSB");

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/auth"))
    {
        context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["Expires"] = "0";
    }

    await next();
});

app.UseAuthentication();

app.Use(async (context, next) =>
{
    var path = (context.Request.Path.Value ?? "").ToLowerInvariant();

    // Public auth endpoints do not require session validation.
    if (path.StartsWith("/api/auth/login") ||
        path.StartsWith("/api/auth/me") ||
        path.StartsWith("/api/auth/forgot-password") ||
        path.StartsWith("/api/auth/reset-password") ||
        path.StartsWith("/api/auth/verify-reset-code") ||
        path.StartsWith("/api/auth/verify-email") ||
        path.StartsWith("/api/auth/verify-email-redirect") ||
        path.StartsWith("/api/auth/confirm-email"))
    {
        await next();
        return;
    }

    if (context.User?.Identity?.IsAuthenticated != true)
    {
        await next();
        return;
    }

    if (path.StartsWith("/api/auth/logout") ||
        path.StartsWith("/api/auth/resend-verification") ||
        path.StartsWith("/api/auth/change-password"))
    {
        await next();
        return;
    }

    using var scope = context.RequestServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<DigitalScrumBoardContext>();

    var idStr = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (!int.TryParse(idStr, out var userId))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    var state = await db.Users
        .IgnoreQueryFilters()
        .AsNoTracking()
        .Where(u => u.UserID == userId)
        .Select(u => new
        {
            u.UserID,
            u.RoleID,
            u.TeamID,
            u.EmailVerified,
            u.MustChangePassword,
            u.Disabled
        })
        .SingleOrDefaultAsync();

    if (state is null)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    static async Task RejectInvalidatedSessionAsync(HttpContext httpContext, int statusCode, string reason, string message)
    {
        await httpContext.SignOutAsync("MyCookieAuth");
        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(new
        {
            error = "SESSION_INVALIDATED",
            reason,
            message
        });
    }

    var claimRoleId = context.User.FindFirst("RoleID")?.Value;
    var claimTeamId = context.User.FindFirst("TeamID")?.Value;

    if (!int.TryParse(claimRoleId, out var currentRoleId) || currentRoleId != state.RoleID)
    {
        await RejectInvalidatedSessionAsync(
            context,
            StatusCodes.Status401Unauthorized,
            "ROLE_CHANGED",
            "Your role has changed. You will be logged out.");
        return;
    }

    var currentTeamId = int.TryParse(claimTeamId, out var parsedTeamIdValue)
        ? parsedTeamIdValue
        : (int?)null;
    if (currentTeamId != state.TeamID)
    {
        await RejectInvalidatedSessionAsync(
            context,
            StatusCodes.Status401Unauthorized,
            "TEAM_CHANGED",
            "Your team assignment has changed. You will be logged out.");
        return;
    }

    const int accountLockoutFailedAttempt = 8;
    var latestAuditEvents = await db.AuditLogs
        .AsNoTracking()
        .Where(a =>
            (a.Action == "LOGIN" && a.UserID == userId) ||
            (a.Action == "UNLOCK_ACCOUNT" && a.TargetType == "User" && a.TargetID == userId && a.Success))
        .OrderByDescending(a => a.Timestamp)
        .Select(a => new
        {
            a.Action,
            a.Success,
            a.Timestamp,
            IsUnlockEvent = a.Action == "UNLOCK_ACCOUNT"
        })
        .ToListAsync();

    var consecutiveFailures = 0;
    DateTime? latestFailureUtc = null;
    foreach (var attempt in latestAuditEvents)
    {
        if (attempt.IsUnlockEvent || attempt.Success)
            break;

        consecutiveFailures++;
        if (latestFailureUtc is null)
            latestFailureUtc = attempt.Timestamp;
    }

    if (consecutiveFailures >= accountLockoutFailedAttempt &&
        latestFailureUtc.HasValue &&
        DateTime.UtcNow < latestFailureUtc.Value.AddHours(24))
    {
        await RejectInvalidatedSessionAsync(
            context,
            StatusCodes.Status403Forbidden,
            "ACCOUNT_LOCKED",
            "Your account has been locked. You will be logged out.");
        return;
    }

    // Immediately invalidate session if account is disabled.
    if (state.Disabled)
    {
        await RejectInvalidatedSessionAsync(
            context,
            StatusCodes.Status403Forbidden,
            "ACCOUNT_DISABLED",
            "Your account has been disabled. You will be logged out.");
        return;
    }

    if (state.MustChangePassword)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            message = "Password change required.",
            code = "PASSWORD_CHANGE_REQUIRED"
        });
        return;
    }

    if (!state.EmailVerified)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            message = "Email verification required.",
            code = "EMAIL_VERIFICATION_REQUIRED"
        });
        return;
    }

    await next();
});

app.UseAuthorization();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<DigitalScrumBoardContext>();

    await RoleSeeder.SeedRolesAsync(context);
    await AdminUserSeeder.SeedAdminAsync(context);
}

// SignalR Hubs
app.MapHub<BoardHub>("/hubs/boards");
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();