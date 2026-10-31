using LocalSIEM.Domain.Enums;

namespace LocalSIEM.Domain;

public static class StatusTransitions
{
    public static bool CanChange(AlertStatus from, AlertStatus to) => (from, to) switch
    {
        (AlertStatus.New, AlertStatus.Acknowledged) => true,
        (AlertStatus.New, AlertStatus.Resolved) => true,
        (AlertStatus.Acknowledged, AlertStatus.Resolved) => true,
        (AlertStatus.Resolved, AlertStatus.Acknowledged) => true,
        _ => false
    };

    public static bool CanChange(IncidentStatus from, IncidentStatus to) => (from, to) switch
    {
        (IncidentStatus.Open, IncidentStatus.Investigating) => true,
        (IncidentStatus.Open, IncidentStatus.Resolved) => true,
        (IncidentStatus.Investigating, IncidentStatus.Resolved) => true,
        (IncidentStatus.Resolved, IncidentStatus.Investigating) => true,
        (IncidentStatus.Resolved, IncidentStatus.Closed) => true,
        _ => false
    };

    public static bool IsActive(IncidentStatus status) => status is IncidentStatus.Open or IncidentStatus.Investigating;
}
