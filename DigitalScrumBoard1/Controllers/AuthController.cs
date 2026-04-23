using DigitalScrumBoard1.Utilities;
using DigitalScrumBoard1.Data;
using DigitalScrumBoard1.DTOs.Authentication;
using DigitalScrumBoard1.Hubs;
using DigitalScrumBoard1.Models;
using DigitalScrumBoard1.Security;
using DigitalScrumBoard1.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DigitalScrumBoard1.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public sealed class AuthController : ControllerBase
    {
        private readonly DigitalScrumBoardContext _db;
        private readonly IAuditService _audit;
        private readonly IAuthEmailService _authEmail;
        private readonly IHubContext<NotificationHub> _notifyHub;
        private readonly IConfiguration _configuration;

        private const int RateLimitStartsAtFailedAttempt = 5;
        private const int AccountLockoutFailedAttempt = 8;

        private static readonly TimeSpan FirstCooldownDuration = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan CooldownStepDuration = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan LockoutDuration = TimeSpan.FromHours(24);

        private static readonly TimeSpan PasswordResetCodeLifetime = TimeSpan.FromMinutes(5);

        public AuthController(
            DigitalScrumBoardContext db,
            IAuditService audit,
            IAuthEmailService authEmail,
            IHubContext<NotificationHub> notifyHub,
            IConfiguration configuration)
        {
            _db = db;
            _audit = audit;
            _authEmail = authEmail;
            _notifyHub = notifyHub;
            _configuration = configuration;
        }

        [HttpPost("login")]
        [AllowAnonymous]
        [EnableRateLimiting("LoginLimiter")]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto req, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            var email = req.EmailAddress.Trim().ToLowerInvariant();
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var user = await _db.Users
                .Include(u => u.Role)
                .Include(u => u.Team)
                .AsTracking()
                .SingleOrDefaultAsync(u => u.EmailAddress.ToLower() == email, ct);

            if (user is null)
                return Unauthorized(new { message = "Invalid credentials." });

            if (user.Disabled)
            {
                await _audit.LogAsync(user.UserID, "LOGIN", "User", user.UserID, false, "Account disabled.", ip, ct);
                return Unauthorized(new { message = "Invalid credentials." });
            }

            var authState = await GetAuthAttemptStateAsync(user.UserID, ct);

            if (authState.IsLocked)
            {
                var retryAfterSeconds = (int)Math.Ceiling(authState.RetryAfter.TotalSeconds);
                Response.Headers["Retry-After"] = retryAfterSeconds.ToString();

                await _audit.LogAsync(user.UserID, "LOGIN", "User", user.UserID, false, "Account locked out.", ip, ct);

                return StatusCode(StatusCodes.Status423Locked, new
                {
                    message = "Account locked due to multiple failed login attempts.",
                    code = "ACCOUNT_LOCKED",
                    retryAfterSeconds
                });
            }

            if (authState.IsRateLimited)
            {
                var retryAfterSeconds = (int)Math.Ceiling(authState.RetryAfter.TotalSeconds);
                Response.Headers["Retry-After"] = retryAfterSeconds.ToString();

                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    message = "Too many failed login attempts. Please try again later.",
                    code = "AUTH_RATE_LIMITED",
                    retryAfterSeconds
                });
            }

            var passwordOk = PasswordHasher.Verify(req.Password, user.PasswordHash);
            if (!passwordOk)
            {
                await _audit.LogAsync(user.UserID, "LOGIN", "User", user.UserID, false, "Invalid credentials.", ip, ct);

                var updatedAuthState = await GetAuthAttemptStateAsync(user.UserID, ct);

                if (updatedAuthState.IsLocked)
                {
                    var retryAfterSeconds = (int)Math.Ceiling(updatedAuthState.RetryAfter.TotalSeconds);
                    Response.Headers["Retry-After"] = retryAfterSeconds.ToString();

                    return StatusCode(StatusCodes.Status423Locked, new
                    {
                        message = "Account locked due to multiple failed login attempts.",
                        code = "ACCOUNT_LOCKED",
                        retryAfterSeconds
                    });
                }

                if (updatedAuthState.IsRateLimited)
                {
                    var retryAfterSeconds = (int)Math.Ceiling(updatedAuthState.RetryAfter.TotalSeconds);
                    Response.Headers["Retry-After"] = retryAfterSeconds.ToString();

                    return StatusCode(StatusCodes.Status429TooManyRequests, new
                    {
                        message = "Too many failed login attempts. Please try again later.",
                        code = "AUTH_RATE_LIMITED",
                        retryAfterSeconds
                    });
                }

                return Unauthorized(new { message = "Invalid credentials." });
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.UserID.ToString()),
                new(ClaimTypes.Email, user.EmailAddress),
                new(ClaimTypes.Name, BuildDisplayName(user.FirstName, user.MiddleName, user.LastName, user.NameExtension)),
                new(ClaimTypes.Role, user.Role?.RoleName ?? user.RoleID.ToString()),
                new("RoleID", user.RoleID.ToString())
            };

            if (user.TeamID.HasValue)
            {
                claims.Add(new Claim("TeamID", user.TeamID.Value.ToString()));
            }

            var identity = new ClaimsIdentity(claims, "MyCookieAuth");
            var principal = new ClaimsPrincipal(identity);

            // FR-011: Only create persistent cookie if email is verified
            // Unverified accounts get session-only cookie (cleared on browser close)
            await HttpContext.SignInAsync("MyCookieAuth", principal, new AuthenticationProperties
            {
                IsPersistent = user.EmailVerified,
                AllowRefresh = true,
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            });

            user.LastLogin = DateTimeHelper.Now;
            user.UpdatedAt = DateTimeHelper.Now;
            await _db.SaveChangesAsync(ct);

            await _audit.LogAsync(user.UserID, "LOGIN", "User", user.UserID, true, "Login success.", ip, ct);

            return Ok(new
            {
                message = "Login successful.",
                mustChangePassword = user.MustChangePassword,
                emailVerified = user.EmailVerified,
                user = ToAuthUserDto(user)
            });
        }

        [HttpPost("logout")]
        [Authorize(AuthenticationSchemes = "MyCookieAuth")]
        public async Task<IActionResult> Logout(CancellationToken ct)
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var userId = GetUserId();

            await HttpContext.SignOutAsync("MyCookieAuth");

            if (userId is not null)
                await _audit.LogAsync(userId.Value, "LOGOUT", "User", userId.Value, true, "Logout.", ip, ct);

            return Ok(new { message = "Logged out." });
        }

        [HttpGet("me")]
        [Authorize(AuthenticationSchemes = "MyCookieAuth")]
        public async Task<IActionResult> Me(CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId is null)
                return Unauthorized(new { message = "Not authenticated." });

            var user = await _db.Users
                .Include(u => u.Role)
                .Include(u => u.Team)
                .AsNoTracking()
                .SingleOrDefaultAsync(u => u.UserID == userId.Value, ct);

            if (user is null)
                return Unauthorized(new { message = "Not authenticated." });

            return Ok(ToAuthUserDto(user));
        }

        [HttpPatch("profile")]
        [Authorize(AuthenticationSchemes = "MyCookieAuth")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequestDto req, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            var userId = GetUserId();
            if (userId is null)
                return Unauthorized(new { message = "Not authenticated." });

            var user = await _db.Users
                .Include(u => u.Role)
                .Include(u => u.Team)
                .AsTracking()
                .SingleOrDefaultAsync(u => u.UserID == userId.Value, ct);

            if (user is null)
                return Unauthorized(new { message = "Not authenticated." });

            if (user.Disabled)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Account is disabled." });

            var firstName = req.FirstName.Trim();
            var lastName = req.LastName.Trim();
            var middleName = string.IsNullOrWhiteSpace(req.MiddleName) ? null : req.MiddleName.Trim();
            var nameExtension = string.IsNullOrWhiteSpace(req.NameExtension) ? null : req.NameExtension.Trim();

            if (firstName.Length == 0 || lastName.Length == 0)
                return BadRequest(new { message = "First name and last name are required." });

            user.FirstName = firstName;
            user.MiddleName = middleName;
            user.LastName = lastName;
            user.NameExtension = nameExtension;
            user.UpdatedAt = DateTimeHelper.Now;

            await _db.SaveChangesAsync(ct);

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            await _audit.LogAsync(user.UserID, "UPDATE_PROFILE", "User", user.UserID, true, "Profile name updated.", ip, ct);

            var displayName = BuildDisplayName(user.FirstName, user.MiddleName, user.LastName, user.NameExtension);
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.UserID.ToString()),
                new(ClaimTypes.Email, user.EmailAddress),
                new(ClaimTypes.Name, displayName),
                new(ClaimTypes.Role, user.Role?.RoleName ?? user.RoleID.ToString()),
                new("RoleID", user.RoleID.ToString())
            };
            var identity = new ClaimsIdentity(claims, "MyCookieAuth");
            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync("MyCookieAuth", principal, new AuthenticationProperties
            {
                IsPersistent = true,
                AllowRefresh = true,
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            });

            var dto = ToAuthUserDto(user);
            await _notifyHub.Clients.Group($"user-{user.UserID}").SendAsync("UserProfileChanged", dto, ct);
            await _notifyHub.Clients.All.SendAsync("AdminDirectoryChanged", new { reason = "users" }, ct);

            return Ok(dto);
        }

        [HttpPost("change-password")]
        [Authorize(AuthenticationSchemes = "MyCookieAuth")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequestDto req, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            var userId = GetUserId();
            if (userId is null)
                return Unauthorized(new { message = "Not authenticated." });

            var user = await _db.Users.AsTracking().SingleOrDefaultAsync(u => u.UserID == userId.Value, ct);
            if (user is null)
                return Unauthorized(new { message = "Not authenticated." });

            if (user.Disabled)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Account is disabled." });

            if (!PasswordPolicy.IsValid(req.NewPassword))
            {
                return BadRequest(new
                {
                    message = "Password does not meet requirements.",
                    requirements = "At least 8 characters, with uppercase, lowercase, number, and symbol."
                });
            }

            user.PasswordHash = PasswordHasher.Hash(req.NewPassword);
            user.MustChangePassword = false;
            user.UpdatedAt = DateTimeHelper.Now;

            await _db.SaveChangesAsync(ct);

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            await _audit.LogAsync(user.UserID, "CHANGE_PASSWORD", "User", user.UserID, true, "Password changed.", ip, ct);

            return Ok(new { message = "Password updated." });
        }

        [HttpGet("verify-email")]
        [AllowAnonymous]
        public async Task<IActionResult> VerifyEmail([FromQuery] string token, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(token))
                return BadRequest(new { message = "Token is required." });

            var tokenHash = EmailVerificationTokenFactory.HashToken(token);

            var row = await _db.EmailVerificationTokens
                .Include(t => t.User)
                .Include(t => t.User.Role)
                .AsTracking()
                .Where(t => t.TokenHash == tokenHash)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (row is null)
                return BadRequest(new { message = "Invalid token." });

            if (row.UsedAt is not null)
                return BadRequest(new { message = "Token already used." });

            if (DateTimeHelper.Now > row.ExpiresAt)
                return BadRequest(new { message = "Token expired." });

            row.UsedAt = DateTimeHelper.Now;
            row.User.EmailVerified = true;
            row.User.UpdatedAt = DateTimeHelper.Now;

            await _db.SaveChangesAsync(ct);

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            await _audit.LogAsync(row.User.UserID, "VERIFY_EMAIL", "User", row.User.UserID, true, "Email verified.", ip, ct);

            // FR-011: Re-issue cookie as persistent now that email is verified
            var user = row.User;
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.UserID.ToString()),
                new(ClaimTypes.Email, user.EmailAddress),
                new(ClaimTypes.Name, BuildDisplayName(user.FirstName, user.MiddleName, user.LastName, user.NameExtension)),
                new(ClaimTypes.Role, user.Role?.RoleName ?? user.RoleID.ToString()),
                new("RoleID", user.RoleID.ToString())
            };
            if (user.TeamID.HasValue)
            {
                claims.Add(new Claim("TeamID", user.TeamID.Value.ToString()));
            }
            var identity = new ClaimsIdentity(claims, "MyCookieAuth");
            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync("MyCookieAuth", principal, new AuthenticationProperties
            {
                IsPersistent = true, // Now persistent since email is verified
                AllowRefresh = true,
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            });

            return Ok(new { message = "Email verified successfully." });
        }

        [HttpGet("verify-email-redirect")]
        [AllowAnonymous]
        public async Task<IActionResult> VerifyEmailAndRedirect([FromQuery] string token, CancellationToken ct)
        {
            var frontendUrl = (_configuration.GetValue<string>("Email:FrontendBaseUrl") ?? "http://192.168.19.18:7120").TrimEnd('/');

            if (string.IsNullOrWhiteSpace(token))
                return Redirect($"{frontendUrl}/verify-email?error=invalid_token");

            var tokenHash = EmailVerificationTokenFactory.HashToken(token);

            var row = await _db.EmailVerificationTokens
                .Include(t => t.User)
                .AsNoTracking()
                .Where(t => t.TokenHash == tokenHash)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (row is null)
                return Redirect($"{frontendUrl}/verify-email?error=invalid_token");

            if (row.UsedAt is not null)
                return Redirect($"{frontendUrl}/verify-email?error=already_used");

            if (DateTimeHelper.Now > row.ExpiresAt)
                return Redirect($"{frontendUrl}/verify-email?error=expired_token");

            // Redirect to confirmation page with token - actual verification happens when user clicks button
            return Redirect($"{frontendUrl}/email-confirmed?token={Uri.EscapeDataString(token)}");
        }

        [HttpPost("resend-verification")]
        [Authorize(AuthenticationSchemes = "MyCookieAuth")]
        [EnableRateLimiting("LoginLimiter")]
        public async Task<IActionResult> ResendVerification(CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId is null)
                return Unauthorized(new { message = "Not authenticated." });

            var user = await _db.Users.AsTracking().SingleOrDefaultAsync(u => u.UserID == userId.Value, ct);
            if (user is null)
                return Unauthorized(new { message = "Not authenticated." });

            if (user.EmailVerified)
                return Ok(new { message = "Email is already verified." });

            await _authEmail.SendVerificationAsync(user, ct);

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            await _audit.LogAsync(user.UserID, "SEND_VERIFY_EMAIL", "User", user.UserID, true, "Resent verification email.", ip, ct);

            return Ok(new { message = "Verification email sent." });
        }

        [HttpPost("confirm-email")]
        [AllowAnonymous]
        public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req.Token))
                return BadRequest(new { message = "Token is required." });

            var tokenHash = EmailVerificationTokenFactory.HashToken(req.Token);

            var row = await _db.EmailVerificationTokens
                .Include(t => t.User)
                .Include(t => t.User.Role)
                .AsTracking()
                .Where(t => t.TokenHash == tokenHash)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (row is null)
                return BadRequest(new { message = "Invalid token." });

            if (row.UsedAt is not null)
                return BadRequest(new { message = "Token already used." });

            if (DateTimeHelper.Now > row.ExpiresAt)
                return BadRequest(new { message = "Token expired." });

            row.UsedAt = DateTimeHelper.Now;
            row.User.EmailVerified = true;
            row.User.UpdatedAt = DateTimeHelper.Now;

            await _db.SaveChangesAsync(ct);

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            await _audit.LogAsync(row.User.UserID, "VERIFY_EMAIL", "User", row.User.UserID, true, "Email verified.", ip, ct);

            // FR-011: Re-issue cookie as persistent now that email is verified
            var user = row.User;
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.UserID.ToString()),
                new(ClaimTypes.Email, user.EmailAddress),
                new(ClaimTypes.Name, BuildDisplayName(user.FirstName, user.MiddleName, user.LastName, user.NameExtension)),
                new(ClaimTypes.Role, user.Role?.RoleName ?? user.RoleID.ToString()),
                new("RoleID", user.RoleID.ToString())
            };
            if (user.TeamID.HasValue)
            {
                claims.Add(new Claim("TeamID", user.TeamID.Value.ToString()));
            }
            var identity = new ClaimsIdentity(claims, "MyCookieAuth");
            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync("MyCookieAuth", principal, new AuthenticationProperties
            {
                IsPersistent = true, // Now persistent since email is verified
                AllowRefresh = true,
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            });

            return Ok(new { message = "Email verified successfully." });
        }

        [HttpPost("forgot-password")]
        [AllowAnonymous]
        [EnableRateLimiting("LoginLimiter")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestDto req, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            var email = req.EmailAddress.Trim().ToLowerInvariant();

            var user = await _db.Users
                .AsTracking()
                .SingleOrDefaultAsync(u => u.EmailAddress.ToLower() == email, ct);

            if (user is null || user.Disabled)
            {
                return Ok(new
                {
                    message = "If an account exists for that email, a password reset code has been sent. Please also double-check that you entered the correct email address.",
                    codeExpiresInSeconds = (int)PasswordResetCodeLifetime.TotalSeconds
                });
            }

            var now = DateTimeHelper.Now;

            var rawCode = EmailVerificationTokenFactory.CreateSixDigitCode();
            var tokenHash = EmailVerificationTokenFactory.HashToken(rawCode);

            var tokenRow = new PasswordResetToken
            {
                UserID = user.UserID,
                TokenHash = tokenHash,
                CreatedAt = now,
                ExpiresAt = now.Add(PasswordResetCodeLifetime),
                UsedAt = null
            };

            _db.PasswordResetTokens.Add(tokenRow);
            await _db.SaveChangesAsync(ct);

            await _authEmail.SendPasswordResetCodeAsync(user, rawCode, (int)PasswordResetCodeLifetime.TotalSeconds, ct);

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            await _audit.LogAsync(user.UserID, "FORGOT_PASSWORD", "User", user.UserID, true, "Password reset code sent.", ip, ct);

            return Ok(new
            {
                message = "If an account exists for that email, a password reset code has been sent. Please also double-check that you entered the correct email address.",
                codeExpiresInSeconds = (int)PasswordResetCodeLifetime.TotalSeconds
            });
        }

        [HttpPost("verify-reset-code")]
        [AllowAnonymous]
        [EnableRateLimiting("LoginLimiter")]
        public async Task<IActionResult> VerifyResetCode([FromBody] VerifyResetCodeRequestDto req, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            var email = req.EmailAddress.Trim().ToLowerInvariant();
            var tokenHash = EmailVerificationTokenFactory.HashToken(req.Token);
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var user = await _db.Users
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleOrDefaultAsync(u => u.EmailAddress.ToLower() == email, ct);

            if (user is not null)
            {
                var otpAuthState = await GetResetCodeAttemptStateAsync(user.UserID, ct);

                if (otpAuthState.IsLocked)
                {
                    var retryAfterSeconds = (int)Math.Ceiling(otpAuthState.RetryAfter.TotalSeconds);
                    Response.Headers["Retry-After"] = retryAfterSeconds.ToString();

                    return StatusCode(StatusCodes.Status423Locked, new
                    {
                        message = "Too many invalid reset code attempts. Please try again later.",
                        code = "ACCOUNT_LOCKED",
                        retryAfterSeconds
                    });
                }

                if (otpAuthState.IsRateLimited)
                {
                    var retryAfterSeconds = (int)Math.Ceiling(otpAuthState.RetryAfter.TotalSeconds);
                    Response.Headers["Retry-After"] = retryAfterSeconds.ToString();

                    return StatusCode(StatusCodes.Status429TooManyRequests, new
                    {
                        message = "Too many invalid reset code attempts. Please wait before trying again.",
                        code = "AUTH_RATE_LIMITED",
                        retryAfterSeconds
                    });
                }
            }

            var row = await _db.PasswordResetTokens
                .Include(t => t.User)
                .AsNoTracking()
                .Where(t =>
                    t.TokenHash == tokenHash &&
                    t.User.EmailAddress.ToLower() == email)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (row is null)
            {
                if (user is not null)
                {
                    await _audit.LogAsync(user.UserID, "VERIFY_RESET_CODE", "User", user.UserID, false, "Invalid reset code.", ip, ct);

                    var updatedOtpAuthState = await GetResetCodeAttemptStateAsync(user.UserID, ct);
                    if (updatedOtpAuthState.IsLocked)
                    {
                        var retryAfterSeconds = (int)Math.Ceiling(updatedOtpAuthState.RetryAfter.TotalSeconds);
                        Response.Headers["Retry-After"] = retryAfterSeconds.ToString();

                        return StatusCode(StatusCodes.Status423Locked, new
                        {
                            message = "Too many invalid reset code attempts. Please try again later.",
                            code = "ACCOUNT_LOCKED",
                            retryAfterSeconds
                        });
                    }

                    if (updatedOtpAuthState.IsRateLimited)
                    {
                        var retryAfterSeconds = (int)Math.Ceiling(updatedOtpAuthState.RetryAfter.TotalSeconds);
                        Response.Headers["Retry-After"] = retryAfterSeconds.ToString();

                        return StatusCode(StatusCodes.Status429TooManyRequests, new
                        {
                            message = "Too many invalid reset code attempts. Please wait before trying again.",
                            code = "AUTH_RATE_LIMITED",
                            retryAfterSeconds
                        });
                    }
                }

                return BadRequest(new { message = "Invalid code." });
            }

            if (row.UsedAt is not null)
                return BadRequest(new { message = "Code already used." });

            if (DateTimeHelper.Now > row.ExpiresAt)
                return BadRequest(new
                {
                    message = "Code expired.",
                    code = "RESET_CODE_EXPIRED"
                });

            if (row.User.Disabled)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Account is disabled." });

            await _audit.LogAsync(row.User.UserID, "VERIFY_RESET_CODE", "User", row.User.UserID, true, "Reset code verified.", ip, ct);

            return Ok(new
            {
                message = "Code verified.",
                expiresInSeconds = (int)Math.Max(0, Math.Ceiling((row.ExpiresAt - DateTimeHelper.Now).TotalSeconds))
            });
        }

        [HttpPost("reset-password")]
        [AllowAnonymous]
        [EnableRateLimiting("LoginLimiter")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequestDto req, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            if (string.IsNullOrWhiteSpace(req.Token))
                return BadRequest(new { message = "Code is required." });

            var email = req.EmailAddress.Trim().ToLowerInvariant();
            var tokenHash = EmailVerificationTokenFactory.HashToken(req.Token);

            var user = await _db.Users
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleOrDefaultAsync(u => u.EmailAddress.ToLower() == email, ct);

            if (user is not null)
            {
                var otpAuthState = await GetResetCodeAttemptStateAsync(user.UserID, ct);
                if (otpAuthState.IsLocked)
                {
                    var retryAfterSeconds = (int)Math.Ceiling(otpAuthState.RetryAfter.TotalSeconds);
                    Response.Headers["Retry-After"] = retryAfterSeconds.ToString();

                    return StatusCode(StatusCodes.Status423Locked, new
                    {
                        message = "Too many invalid reset code attempts. Please try again later.",
                        code = "ACCOUNT_LOCKED",
                        retryAfterSeconds
                    });
                }
            }

            var row = await _db.PasswordResetTokens
                .Include(t => t.User)
                .AsTracking()
                .Where(t =>
                    t.TokenHash == tokenHash &&
                    t.User.EmailAddress.ToLower() == email)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (row is null)
                return BadRequest(new { message = "Invalid code." });

            if (row.UsedAt is not null)
                return BadRequest(new { message = "Code already used." });

            if (DateTimeHelper.Now > row.ExpiresAt)
                return BadRequest(new
                {
                    message = "Code expired.",
                    code = "RESET_CODE_EXPIRED"
                });

            if (row.User.Disabled)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Account is disabled." });

            if (!PasswordPolicy.IsValid(req.NewPassword))
            {
                return BadRequest(new
                {
                    message = "Password does not meet requirements.",
                    requirements = "At least 8 characters, with uppercase, lowercase, number, and symbol."
                });
            }

            row.User.PasswordHash = PasswordHasher.Hash(req.NewPassword);
            row.User.MustChangePassword = false;
            row.User.UpdatedAt = DateTimeHelper.Now;
            row.UsedAt = DateTimeHelper.Now;

            await _db.SaveChangesAsync(ct);

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            await _audit.LogAsync(row.User.UserID, "RESET_PASSWORD", "User", row.User.UserID, true, "Password reset successful.", ip, ct);

            return Ok(new { message = "Password has been reset successfully." });
        }

        [HttpPost("unlock/{userId:int}")]
        [Authorize(AuthenticationSchemes = "MyCookieAuth", Roles = "Administrator")]
        public async Task<IActionResult> UnlockAccount([FromRoute] int userId, CancellationToken ct)
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var adminId = GetUserId();
            if (adminId is null)
                return Unauthorized(new { message = "Missing/invalid user identity." });

            var userExists = await _db.Users
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(u => u.UserID == userId, ct);

            if (!userExists)
                return NotFound(new { message = "User not found." });

            await _audit.LogAsync(
                adminId.Value,
                "UNLOCK_ACCOUNT",
                "User",
                userId,
                true,
                adminId is null
                    ? "Account unlocked by admin."
                    : $"Account unlocked by admin (AdminUserID={adminId.Value}).",
                ip,
                ct
            );

            return Ok(new { message = "Account unlocked." });
        }

        private int? GetUserId()
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(id, out var parsed) ? parsed : null;
        }

        private static string BuildDisplayName(string firstName, string? middleName, string lastName, string? nameExtension)
        {
            var parts = new List<string>();
            var f = firstName.Trim();
            var l = lastName.Trim();
            if (f.Length > 0) parts.Add(f);
            if (!string.IsNullOrWhiteSpace(middleName)) parts.Add(middleName!.Trim());
            if (l.Length > 0) parts.Add(l);
            if (!string.IsNullOrWhiteSpace(nameExtension)) parts.Add(nameExtension!.Trim());
            return parts.Count > 0 ? string.Join(' ', parts) : string.Empty;
        }

        private static AuthUserDto ToAuthUserDto(User user)
        {
            var teamName = user.Team?.TeamName;
            return new AuthUserDto
            {
                UserID = user.UserID,
                EmailAddress = user.EmailAddress,
                FirstName = user.FirstName,
                MiddleName = user.MiddleName,
                LastName = user.LastName,
                NameExtension = user.NameExtension,
                FullName = BuildDisplayName(user.FirstName, user.MiddleName, user.LastName, user.NameExtension),
                RoleID = user.RoleID,
                RoleName = user.Role?.RoleName ?? string.Empty,
                TeamID = user.TeamID,
                TeamName = string.IsNullOrWhiteSpace(teamName) ? null : teamName
            };
        }

        private static TimeSpan GetCooldownDuration(int consecutiveFailedAttempts)
        {
            if (consecutiveFailedAttempts < RateLimitStartsAtFailedAttempt)
                return TimeSpan.Zero;

            var extraSteps = consecutiveFailedAttempts - RateLimitStartsAtFailedAttempt;
            return FirstCooldownDuration + TimeSpan.FromTicks(CooldownStepDuration.Ticks * extraSteps);
        }

        private async Task<(int ConsecutiveFailures, DateTime? LatestFailureUtc)> GetConsecutiveFailedLoginInfoAsync(int userId, CancellationToken ct)
        {
            var attempts = await _db.AuditLogs
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
                .ToListAsync(ct);

            var consecutiveFailures = 0;
            DateTime? latestFailureUtc = null;

            foreach (var attempt in attempts)
            {
                if (attempt.IsUnlockEvent)
                    break;

                if (attempt.Success)
                    break;

                consecutiveFailures++;

                if (latestFailureUtc is null)
                    latestFailureUtc = attempt.Timestamp;
            }

            return (consecutiveFailures, latestFailureUtc);
        }

        private async Task<(bool IsRateLimited, bool IsLocked, TimeSpan RetryAfter)> GetAuthAttemptStateAsync(int userId, CancellationToken ct)
        {
            var info = await GetConsecutiveFailedLoginInfoAsync(userId, ct);

            if (info.ConsecutiveFailures <= 0 || info.LatestFailureUtc is null)
                return (false, false, TimeSpan.Zero);

            var now = DateTimeHelper.Now;

            if (info.ConsecutiveFailures >= AccountLockoutFailedAttempt)
            {
                var until = info.LatestFailureUtc.Value.Add(LockoutDuration);
                if (now < until)
                    return (false, true, until - now);

                return (false, false, TimeSpan.Zero);
            }

            if (info.ConsecutiveFailures >= RateLimitStartsAtFailedAttempt)
            {
                var cooldown = GetCooldownDuration(info.ConsecutiveFailures);
                var until = info.LatestFailureUtc.Value.Add(cooldown);

                if (now < until)
                    return (true, false, until - now);
            }

            return (false, false, TimeSpan.Zero);
        }

        private async Task<(int ConsecutiveFailures, DateTime? LatestFailureUtc)> GetConsecutiveFailedResetCodeInfoAsync(int userId, CancellationToken ct)
        {
            var attempts = await _db.AuditLogs
                .AsNoTracking()
                .Where(a =>
                    (a.Action == "VERIFY_RESET_CODE" && a.UserID == userId) ||
                    (a.Action == "UNLOCK_ACCOUNT" && a.TargetType == "User" && a.TargetID == userId && a.Success))
                .OrderByDescending(a => a.Timestamp)
                .Select(a => new
                {
                    a.Action,
                    a.Success,
                    a.Timestamp,
                    IsUnlockEvent = a.Action == "UNLOCK_ACCOUNT"
                })
                .ToListAsync(ct);

            var consecutiveFailures = 0;
            DateTime? latestFailureUtc = null;

            foreach (var attempt in attempts)
            {
                if (attempt.IsUnlockEvent)
                    break;

                if (attempt.Success)
                    break;

                consecutiveFailures++;

                if (latestFailureUtc is null)
                    latestFailureUtc = attempt.Timestamp;
            }

            return (consecutiveFailures, latestFailureUtc);
        }

        private async Task<(bool IsRateLimited, bool IsLocked, TimeSpan RetryAfter)> GetResetCodeAttemptStateAsync(int userId, CancellationToken ct)
        {
            var info = await GetConsecutiveFailedResetCodeInfoAsync(userId, ct);

            if (info.ConsecutiveFailures <= 0 || info.LatestFailureUtc is null)
                return (false, false, TimeSpan.Zero);

            var now = DateTimeHelper.Now;

            if (info.ConsecutiveFailures >= AccountLockoutFailedAttempt)
            {
                var until = info.LatestFailureUtc.Value.Add(LockoutDuration);
                if (now < until)
                    return (false, true, until - now);

                return (false, false, TimeSpan.Zero);
            }

            if (info.ConsecutiveFailures >= RateLimitStartsAtFailedAttempt)
            {
                var cooldown = GetCooldownDuration(info.ConsecutiveFailures);
                var until = info.LatestFailureUtc.Value.Add(cooldown);
                if (now < until)
                    return (true, false, until - now);
            }

            return (false, false, TimeSpan.Zero);
        }
    }
}