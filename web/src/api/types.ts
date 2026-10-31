export type Severity = 'Info' | 'Low' | 'Medium' | 'High' | 'Critical'
export type AlertStatus = 'New' | 'Acknowledged' | 'Resolved'
export type IncidentStatus = 'Open' | 'Investigating' | 'Resolved' | 'Closed'
export type SourceKind = 'Simulator' | 'WindowsCollector' | 'HttpApi'
export type SourceStatus = 'Online' | 'Idle' | 'Disabled'
export type RuleType = 'Threshold' | 'DistinctCount' | 'Sequence' | 'SingleEvent'
export type IncidentAction = 'Created' | 'StatusChanged' | 'Assigned' | 'AlertLinked' | 'CommentAdded'
export type RoleName = 'Administrator' | 'SecurityAnalyst'

export const SEVERITIES: Severity[] = ['Critical', 'High', 'Medium', 'Low', 'Info']
export const ALERT_STATUSES: AlertStatus[] = ['New', 'Acknowledged', 'Resolved']
export const INCIDENT_STATUSES: IncidentStatus[] = ['Open', 'Investigating', 'Resolved', 'Closed']
export const GROUP_FIELDS = ['SourceIp', 'TargetIp', 'Username', 'Hostname'] as const

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
}

export interface User {
  id: string
  username: string
  displayName: string
  email: string | null
  role: RoleName
  isActive: boolean
  createdAt: string
  lastLoginAt: string | null
}

export interface UserOption {
  id: string
  displayName: string
}

export interface LoginResponse {
  accessToken: string
  expiresAt: string
  user: User
}

export interface EventListItem {
  id: string
  timestamp: string
  sourceId: string
  sourceName: string
  eventType: string
  severity: Severity
  sourceIp: string | null
  targetIp: string | null
  username: string | null
  hostname: string | null
  message: string | null
  isDemo: boolean
}

export interface EventDetails extends EventListItem {
  receivedAt: string
  sourceKind: SourceKind
  correlationId: string | null
  rawData: unknown
  alertIds: string[]
}

export interface AlertListItem {
  id: string
  title: string
  severity: Severity
  status: AlertStatus
  ruleId: string
  ruleCode: string
  ruleName: string
  detectedAt: string
  firstEventAt: string
  lastEventAt: string
  eventCount: number
  incidentId: string | null
  incidentNumber: number | null
  version: number
}

export interface AlertDetails {
  id: string
  title: string
  description: string
  severity: Severity
  status: AlertStatus
  ruleId: string
  ruleCode: string
  ruleName: string
  ruleType: RuleType
  ruleEventType: string
  threshold: number
  windowSeconds: number
  groupValues: Record<string, string>
  detectedAt: string
  firstEventAt: string
  lastEventAt: string
  eventCount: number
  linkedEventCount: number
  statusChangedAt: string | null
  statusChangedBy: string | null
  incidentId: string | null
  incidentNumber: number | null
  isDemo: boolean
  version: number
}

export interface IncidentListItem {
  id: string
  number: number
  title: string
  severity: Severity
  status: IncidentStatus
  assigneeId: string | null
  assigneeName: string | null
  createdAt: string
  updatedAt: string
  detectedAt: string | null
  alertCount: number
  version: number
}

export interface IncidentDetails {
  id: string
  number: number
  title: string
  description: string
  severity: Severity
  status: IncidentStatus
  assigneeId: string | null
  assigneeName: string | null
  createdByName: string
  createdAt: string
  updatedAt: string
  resolvedAt: string | null
  closedAt: string | null
  isDemo: boolean
  version: number
  alerts: AlertListItem[]
}

export interface Comment {
  id: string
  authorId: string
  authorName: string
  body: string
  createdAt: string
}

export interface HistoryEntry {
  id: string
  action: IncidentAction
  oldValue: string | null
  newValue: string | null
  actorName: string | null
  createdAt: string
}

export interface RuleParameters {
  distinctField?: string | null
  thenEventType?: string | null
  field?: string | null
  values?: string[] | null
}

export interface Rule {
  id: string
  code: string
  name: string
  description: string
  ruleType: RuleType
  eventType: string
  threshold: number
  windowSeconds: number
  cooldownSeconds: number | null
  groupBy: string[]
  parameters: RuleParameters
  severity: Severity
  isEnabled: boolean
  updatedAt: string
  version: number
  alertsTotal: number
  alerts24h: number
  lastDetectedAt: string | null
}

export interface RuleInput {
  code?: string
  name: string
  description: string
  ruleType: RuleType
  eventType: string
  threshold: number
  windowSeconds: number
  cooldownSeconds: number | null
  groupBy: string[]
  parameters: RuleParameters
  severity: Severity
  isEnabled: boolean
  version?: number
}

export interface Source {
  id: string
  name: string
  kind: SourceKind
  isEnabled: boolean
  lastSeenAt: string | null
  status: SourceStatus
  hasApiKey: boolean
  createdAt: string
}

export interface SourceWithKey {
  source: Source
  apiKey: string
}

export interface SeverityCount {
  severity: Severity
  count: number
}

export interface DashboardSummary {
  totalEvents: number
  events24h: number
  alertsTotal: number
  alertsNew: number
  alertsAcknowledged: number
  activeIncidents: number
  severityDistribution24h: SeverityCount[]
  recentAlerts: AlertListItem[]
  sources: Source[]
  detectionQueue: number
  generatedAt: string
}

export interface TimelinePoint {
  bucket: string
  severity: Severity
  count: number
}

export interface TopIp {
  ip: string
  count: number
  lastSeen: string
}

export interface ScenarioInfo {
  key: string
  title: string
  description: string
  defaultCount: number
  defaultRate: number
  defaultSourceIp: string | null
  defaultUsername: string | null
  expectedAlerts: string
}

export interface SimulationState {
  runId: string | null
  state: 'Idle' | 'Running' | 'Completed' | 'Stopped' | 'Failed'
  scenario: string | null
  sent: number
  total: number
  ratePerSecond: number
  startedAt: string | null
  finishedAt: string | null
  error: string | null
}

export interface PurgeResult {
  incidents: number
  alerts: number
  events: number
}

export interface SystemSettings {
  maxLinkedEventsPerAlert: number
  sourceIdleSeconds: number
  simulatorMaxRatePerSecond: number
}

export interface AuditEntry {
  id: string
  createdAt: string
  username: string | null
  action: string
  entityType: string | null
  entityId: string | null
  details: string | null
  ipAddress: string | null
}
