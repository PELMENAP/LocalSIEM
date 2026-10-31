namespace LocalSIEM.Domain;

public static class EventTypes
{
    public const string LoginSuccess = "LoginSuccess";
    public const string LoginFailed = "LoginFailed";
    public const string Logout = "Logout";
    public const string RoleChanged = "RoleChanged";
    public const string AccountCreated = "AccountCreated";
    public const string PrivilegedLogon = "PrivilegedLogon";
    public const string ResourceAccess = "ResourceAccess";
    public const string WindowsEvent = "WindowsEvent";
    public const string Generic = "Generic";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        LoginSuccess, LoginFailed, Logout, RoleChanged, AccountCreated, PrivilegedLogon, ResourceAccess, WindowsEvent, Generic
    };
}

public static class RoleNames
{
    public const string Administrator = "Administrator";
    public const string SecurityAnalyst = "SecurityAnalyst";
}

public static class GroupFields
{
    public const string SourceIp = "SourceIp";
    public const string TargetIp = "TargetIp";
    public const string Username = "Username";
    public const string Hostname = "Hostname";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        SourceIp, TargetIp, Username, Hostname
    };
}

public static class AttributeNames
{
    public const string Protected = "protected";
    public const string NewRole = "newRole";
    public const string Resource = "resource";
    public const string WindowsEventId = "windowsEventId";
}
