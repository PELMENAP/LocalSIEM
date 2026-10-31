namespace LocalSIEM.Domain.Enums;

public enum Severity : short
{
    Info = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum SourceKind : short
{
    Simulator = 0,
    WindowsCollector = 1,
    HttpApi = 2
}

public enum RuleType : short
{
    Threshold = 0,
    DistinctCount = 1,
    Sequence = 2,
    SingleEvent = 3
}

public enum AlertStatus : short
{
    New = 0,
    Acknowledged = 1,
    Resolved = 2
}

public enum IncidentStatus : short
{
    Open = 0,
    Investigating = 1,
    Resolved = 2,
    Closed = 3
}

public enum IncidentAction : short
{
    Created = 0,
    StatusChanged = 1,
    Assigned = 2,
    AlertLinked = 3,
    CommentAdded = 4
}

public enum SourceStatus
{
    Online,
    Idle,
    Disabled
}
