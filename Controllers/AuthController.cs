using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(IConfiguration config, AppDatabase database) : ControllerBase
{
    [HttpPost("session")]
    public async Task<IActionResult> CreateSession([FromBody] AdminLoginRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return await ValidateExistingSession(cancellationToken);
        }

        var user = await database.AuthenticateAdminUserAsync(request.Username, request.Password, cancellationToken);

        if (user is null)
        {
            return Unauthorized(new { message = "Tên đăng nhập hoặc mật khẩu không đúng." });
        }

        var createdSession = await database.CreateAdminSessionAsync(
            user,
            GetSessionLifetime(),
            cancellationToken);

        return Ok(await CreateSessionResponse(
            createdSession.AccessToken,
            createdSession.Session,
            cancellationToken));
    }

    [HttpGet("session")]
    public async Task<IActionResult> ValidateExistingSession(CancellationToken cancellationToken)
    {
        var session = await AdminAuth.GetSessionAsync(Request, database, cancellationToken);

        if (session is null)
        {
            return Unauthorized(new { message = "Phiên đăng nhập không hợp lệ hoặc đã hết hạn." });
        }

        return Ok(await CreateSessionResponse(null, session, cancellationToken));
    }

    [HttpDelete("session")]
    public async Task<IActionResult> RevokeSession(CancellationToken cancellationToken)
    {
        await database.RevokeAdminSessionAsync(AdminAuth.GetToken(Request), cancellationToken);
        return NoContent();
    }

    [HttpPatch("password")]
    public async Task<IActionResult> ChangePassword([FromBody] AdminPasswordChangeRequest request, CancellationToken cancellationToken)
    {
        if (request.NewPassword != request.ConfirmPassword)
        {
            return BadRequest(new { message = "Mật khẩu mới không khớp." });
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
        {
            return BadRequest(new { message = "Mật khẩu mới phải có ít nhất 8 ký tự." });
        }

        var changed = await database.ChangeAdminPasswordAsync(
            AdminAuth.GetToken(Request),
            request.CurrentPassword,
            request.NewPassword,
            cancellationToken);

        if (!changed)
        {
            return Unauthorized(new { message = "Mật khẩu hiện tại không đúng hoặc phiên đã hết hạn." });
        }

        return Ok(new
        {
            changed = true,
            message = "Đã đổi mật khẩu. Vui lòng đăng nhập lại."
        });
    }

    private async Task<object> CreateSessionResponse(
        string? accessToken,
        AdminSession session,
        CancellationToken cancellationToken)
    {
        var hasAdminUsers = await database.HasAdminUsersAsync(cancellationToken);

        return new
        {
            authenticated = true,
            accessToken,
            username = session.Username,
            expiresAt = session.ExpiresAt,
            adminTokenConfigured = true,
            adminUsernameConfigured = hasAdminUsers,
            adminPasswordConfigured = hasAdminUsers
        };
    }

    private TimeSpan GetSessionLifetime()
    {
        var configuredHours = config.GetValue<int?>("App:AdminSessionHours");
        var hours = configuredHours is > 0 and <= 168 ? configuredHours.Value : 8;
        return TimeSpan.FromHours(hours);
    }
}

public sealed record AdminLoginRequest(string? Username, string? Password);

public sealed record AdminPasswordChangeRequest(
    string? CurrentPassword,
    string? NewPassword,
    string? ConfirmPassword);
