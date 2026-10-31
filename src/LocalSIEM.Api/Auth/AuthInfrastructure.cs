using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LocalSIEM.Application.Common;
using LocalSIEM.Domain;
using LocalSIEM.Domain.Entities;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LocalSIEM.Api.Auth;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "LocalSIEM";
    public string Audience { get; set; } = "LocalSIEM";
    public string Key { get; set; } = string.Empty;
    public int LifetimeMinutes { get; set; } = 60;

    public SymmetricSecurityKey SigningKey { get; private set; } = null!;

    public void Prepare(ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(Key) || Encoding.UTF8.GetByteCount(Key) < 32)
        {
            logger.LogWarning("Jwt:Key не задан или короче 32 байт — сгенерирован временный ключ, токены станут недействительны после перезапуска");
            SigningKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64));
        }
        else
        {
            SigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key));
        }
    }
}

public static class Claims
{
    public const string Subject = "sub";
    public const string Username = "unique_name";
    public const string DisplayName = "name";
    public const string Role = "role";
}

public static class Policies
{
    public const string Analyst = "Analyst";
    public const string Administrator = "Administrator";
}

public sealed class JwtTokenService(JwtOptions options, TimeProvider clock) : ITokenService
{
    private readonly JsonWebTokenHandler handler = new();

    public (string Token, DateTimeOffset ExpiresAt) CreateToken(User user, string roleName)
    {
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(options.LifetimeMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(options.SigningKey, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [Claims.Subject] = user.Id.ToString(),
                [Claims.Username] = user.Username,
                [Claims.DisplayName] = user.DisplayName,
                [Claims.Role] = roleName
            }
        };
        return (handler.CreateToken(descriptor), expires);
    }
}

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(Claims.Subject), out var id) ? id : null;

    public string? Username => Principal?.FindFirstValue(Claims.Username);

    public bool IsAdministrator => Principal?.IsInRole(RoleNames.Administrator) == true;

    public IPAddress? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress;
}

public static class ClaimsPrincipalExtensions
{
    public static Guid RequireUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(Claims.Subject), out var id)
            ? id
            : throw new ForbiddenException("Требуется аутентификация");
}
