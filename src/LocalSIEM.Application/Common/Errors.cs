namespace LocalSIEM.Application.Common;

public abstract class AppException(string message) : Exception(message);

public sealed class NotFoundException(string message) : AppException(message);

public sealed class ConflictException(string message) : AppException(message);

public sealed class ForbiddenException(string message) : AppException(message);

public sealed class AuthenticationFailedException(string message) : AppException(message);

public sealed class RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
    : AppException("Запрос не прошёл проверку")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    public static RequestValidationException Single(string field, string error) =>
        new(new Dictionary<string, string[]> { [field] = [error] });
}

public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);

    public bool HasErrors => errors.Count > 0;

    public void Add(string field, string error)
    {
        if (!errors.TryGetValue(field, out var list))
        {
            list = [];
            errors[field] = list;
        }
        list.Add(error);
    }

    public void ThrowIfAny()
    {
        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
        }
    }

    public IReadOnlyDictionary<string, string[]> ToDictionary() =>
        errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
}
