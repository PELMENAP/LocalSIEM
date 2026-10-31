using System.Text.RegularExpressions;
using LocalSIEM.Application.Common;
using LocalSIEM.Domain;
using LocalSIEM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LocalSIEM.Application.Identity;

public sealed record UserDto(
    Guid Id, string Username, string DisplayName, string? Email, string Role, bool IsActive,
    DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt);

public sealed record RoleDto(Guid Id, string Name, string? Description);

public sealed record LoginRequest(string Username, string Password);

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);

public sealed record CreateUserDto(string Username, string DisplayName, string? Email, string Role, string Password);

public sealed record UpdateUserDto(string DisplayName, string? Email, string Role);

public sealed record SetActiveDto(bool IsActive);

public sealed record ResetPasswordDto(string Password);

public sealed record UserOptionDto(Guid Id, string DisplayName);

public static partial class UserRules
{
    public const int MinPasswordLength = 8;

    [GeneratedRegex("^[A-Za-z0-9._-]{3,64}$")]
    public static partial Regex UsernamePattern();

    public static void ValidatePassword(ValidationErrors errors, string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength || password.Length > 128)
        {
            errors.Add("password", $"Пароль — от {MinPasswordLength} до 128 символов");
        }
    }
}

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<UserDto> GetCurrentAsync(Guid userId, CancellationToken ct);
}

public sealed class AuthService(
    ISiemDbContext db,
    IPasswordHasher hasher,
    ITokenService tokens,
    IAuditService audit,
    TimeProvider clock) : IAuthService
{
    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var username = request.Username?.Trim() ?? string.Empty;
        var user = username.Length == 0
            ? null
            : await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower(), ct);

        if (user is null || !user.IsActive || !hasher.Verify(user.PasswordHash, request.Password ?? string.Empty))
        {
            audit.RecordFor(user?.Id, AuditActions.LoginFailed, nameof(User), user?.Id, new { username });
            await db.SaveChangesAsync(ct);
            throw new AuthenticationFailedException("Неверное имя пользователя или пароль");
        }

        user.LastLoginAt = clock.GetUtcNow();
        audit.RecordFor(user.Id, AuditActions.LoginSucceeded, nameof(User), user.Id);
        await db.SaveChangesAsync(ct);
        var (token, expiresAt) = tokens.CreateToken(user, user.Role.Name);
        return new LoginResponse(token, expiresAt, UserService.ToDto(user));
    }

    public async Task<UserDto> GetCurrentAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("Пользователь не найден");
        return UserService.ToDto(user);
    }
}

public interface IUserService
{
    Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct);
    Task<IReadOnlyList<UserOptionDto>> ListActiveAsync(CancellationToken ct);
    Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken ct);
    Task<UserDto> CreateAsync(CreateUserDto dto, CancellationToken ct);
    Task<UserDto> UpdateAsync(Guid id, UpdateUserDto dto, CancellationToken ct);
    Task<UserDto> SetActiveAsync(Guid id, bool isActive, CancellationToken ct);
    Task ResetPasswordAsync(Guid id, string password, CancellationToken ct);
}

public sealed class UserService(
    ISiemDbContext db,
    IPasswordHasher hasher,
    IAuditService audit,
    ICurrentUser currentUser,
    TimeProvider clock) : IUserService
{
    public static UserDto ToDto(User u) =>
        new(u.Id, u.Username, u.DisplayName, u.Email, u.Role.Name, u.IsActive, u.CreatedAt, u.LastLoginAt);

    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().Include(u => u.Role).OrderBy(u => u.Username).ToListAsync(ct);
        return users.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<UserOptionDto>> ListActiveAsync(CancellationToken ct) =>
        await db.Users.AsNoTracking().Where(u => u.IsActive).OrderBy(u => u.DisplayName)
            .Select(u => new UserOptionDto(u.Id, u.DisplayName)).ToListAsync(ct);

    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken ct) =>
        await db.Roles.AsNoTracking().OrderBy(r => r.Name).Select(r => new RoleDto(r.Id, r.Name, r.Description)).ToListAsync(ct);

    public async Task<UserDto> CreateAsync(CreateUserDto dto, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        var username = dto.Username?.Trim() ?? string.Empty;
        if (!UserRules.UsernamePattern().IsMatch(username))
        {
            errors.Add("username", "Логин — 3–64 символа: латиница, цифры, . _ -");
        }
        ValidateProfile(errors, dto.DisplayName, dto.Email);
        UserRules.ValidatePassword(errors, dto.Password);
        var role = await FindRoleAsync(dto.Role, errors, ct);
        errors.ThrowIfAny();
        if (await db.Users.AnyAsync(u => u.Username.ToLower() == username.ToLower(), ct))
        {
            throw new ConflictException("Пользователь с таким логином уже существует");
        }

        var user = new User
        {
            Id = Ids.New(),
            Username = username,
            DisplayName = dto.DisplayName.Trim(),
            Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim(),
            PasswordHash = hasher.Hash(dto.Password),
            RoleId = role!.Id,
            Role = role,
            IsActive = true,
            CreatedAt = clock.GetUtcNow()
        };
        db.Users.Add(user);
        audit.Record(AuditActions.UserCreated, nameof(User), user.Id, new { user.Username, role = role.Name });
        await db.SaveChangesAsync(ct);
        return ToDto(user);
    }

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserDto dto, CancellationToken ct)
    {
        var user = await LoadAsync(id, ct);
        var errors = new ValidationErrors();
        ValidateProfile(errors, dto.DisplayName, dto.Email);
        var role = await FindRoleAsync(dto.Role, errors, ct);
        errors.ThrowIfAny();
        if (user.Id == currentUser.UserId && role!.Name != RoleNames.Administrator)
        {
            throw new ConflictException("Нельзя снять с себя роль администратора");
        }
        user.DisplayName = dto.DisplayName.Trim();
        user.Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();
        user.RoleId = role!.Id;
        user.Role = role;
        audit.Record(AuditActions.UserUpdated, nameof(User), user.Id, new { user.DisplayName, role = role.Name });
        await db.SaveChangesAsync(ct);
        return ToDto(user);
    }

    public async Task<UserDto> SetActiveAsync(Guid id, bool isActive, CancellationToken ct)
    {
        var user = await LoadAsync(id, ct);
        if (user.Id == currentUser.UserId && !isActive)
        {
            throw new ConflictException("Нельзя отключить собственную учётную запись");
        }
        user.IsActive = isActive;
        audit.Record(AuditActions.UserActivation, nameof(User), user.Id, new { isActive });
        await db.SaveChangesAsync(ct);
        return ToDto(user);
    }

    public async Task ResetPasswordAsync(Guid id, string password, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        UserRules.ValidatePassword(errors, password);
        errors.ThrowIfAny();
        var user = await LoadAsync(id, ct);
        user.PasswordHash = hasher.Hash(password);
        audit.Record(AuditActions.UserPasswordReset, nameof(User), user.Id);
        await db.SaveChangesAsync(ct);
    }

    private async Task<User> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id, ct)
        ?? throw new NotFoundException("Пользователь не найден");

    private async Task<Role?> FindRoleAsync(string? name, ValidationErrors errors, CancellationToken ct)
    {
        var role = string.IsNullOrWhiteSpace(name) ? null : await db.Roles.FirstOrDefaultAsync(r => r.Name == name, ct);
        if (role is null)
        {
            errors.Add("role", $"Роль должна быть {RoleNames.Administrator} или {RoleNames.SecurityAnalyst}");
        }
        return role;
    }

    private static void ValidateProfile(ValidationErrors errors, string? displayName, string? email)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 128)
        {
            errors.Add("displayName", "Имя обязательно, до 128 символов");
        }
        if (!string.IsNullOrWhiteSpace(email) && (email.Length > 256 || !email.Contains('@')))
        {
            errors.Add("email", "Некорректный e-mail");
        }
    }
}
