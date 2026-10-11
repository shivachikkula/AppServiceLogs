export type ItemType = 'trace' | 'request' | 'dependency' | 'exception' | 'customEvent';

export interface LogEntry {
  itemId: string;
  timestamp: string;
  ingestedAt: string | null;
  itemType: string;
  severityLevel: number;
  message: string;
  roleName: string | null;
  roleInstance: string | null;
  operationName: string | null;
  operationId: string | null;
  resultCode: string | null;
  durationMs: number | null;
  customDimensions: string | null;
}

export interface LiveLogsResponse {
  items: LogEntry[];
  cursor: string | null;
  serverTime: string;
}

export interface LiveLogsFilter {
  appKey: string;
  since?: string | null;
  lookbackMinutes: number;
  types: ItemType[];
  minSeverity: number;
  search?: string;
  roleName?: string;
  operationId?: string;
  take?: number;
}

export interface ExceptionEntry {
  itemId: string;
  timestamp: string;
  problemId: string | null;
  type: string | null;
  message: string | null;
  innermostMessage: string | null;
  method: string | null;
  assembly: string | null;
  severityLevel: number;
  roleName: string | null;
  roleInstance: string | null;
  operationName: string | null;
  operationId: string | null;
  clientType: string | null;
}

export interface ExceptionDetail {
  exception: ExceptionEntry;
  stackTrace: string;
  customDimensions: string | null;
  rawDetails: string | null;
}

export interface ExceptionGroup {
  problemId: string;
  type: string | null;
  message: string | null;
  count: number;
  affectedOperations: number;
  firstSeen: string | null;
  lastSeen: string | null;
}

export interface ExceptionsFilter {
  appKey: string;
  /** Bypass the server's short cache (the Refresh button). */
  fresh?: boolean;
  rangeMinutes: number;
  search?: string;
  problemId?: string;
  roleName?: string;
  operationId?: string;
  take?: number;
}

export interface ApplicationSummary {
  applicationName: string;
  appKey: string;
}

export interface UserInfo {
  email: string;
  name: string | null;
  isAdmin: boolean;
}

export const SEVERITY_LABELS: Record<number, string> = {
  0: 'Verbose',
  1: 'Information',
  2: 'Warning',
  3: 'Error',
  4: 'Critical',
};

export const TIME_RANGES = [
  { label: 'Last 15 minutes', minutes: 15 },
  { label: 'Last hour', minutes: 60 },
  { label: 'Last 6 hours', minutes: 360 },
  { label: 'Last 24 hours', minutes: 1440 },
  { label: 'Last 3 days', minutes: 4320 },
  { label: 'Last 7 days', minutes: 10080 },
  { label: 'Last 30 days', minutes: 43200 },
];

export interface KqlSchemaColumn {
  name: string;
  type: string;
}

export interface KqlSchemaTable {
  name: string;
  description: string | null;
  columns: KqlSchemaColumn[];
}

export interface KqlSchema {
  tables: KqlSchemaTable[];
}

export interface KqlColumn {
  name: string;
  type: string;
}

export interface KqlResult {
  columns: KqlColumn[];
  rows: unknown[][];
  totalRows: number;
  truncated: boolean;
  elapsedMs: number;
}

export interface KqlSample {
  title: string;
  description: string;
  query: string;
}

export interface GeneratedQuery {
  query: string;
  explanation: string;
}

export interface FeatureFlags {
  aiQueryEnabled: boolean;
}

export interface AdminGrant {
  id: number;
  email: string;
  applicationName: string;
  appKey: string;
  createdAt: string;
}

export interface AdminApplication {
  appKey: string;
  applicationName: string;
  users: number;
}

export interface ApplicationCheck {
  ok: boolean;
  applicationId: string | null;
  recentItems: number | null;
  message: string;
}

export interface AuditEntry {
  id: number;
  timestamp: string;
  email: string;
  appKey: string;
  action: string;
  text: string;
  rangeMinutes: number | null;
  succeeded: boolean;
  rowCount: number | null;
  durationMs: number | null;
  error: string | null;
}

export interface DiagnosticCheck {
  name: string;
  status: 'ok' | 'warning' | 'error';
  detail: string;
}

export interface OverviewKpis {
  requests: number;
  failedRequests: number;
  avgDurationMs: number | null;
  p95DurationMs: number | null;
  exceptions: number;
  failedDependencies: number;
  previousRequests: number;
  previousFailedRequests: number;
  previousExceptions: number;
}

export interface OverviewPoint {
  timestamp: string;
  requests: number;
  failedRequests: number;
  p95DurationMs: number | null;
  exceptions: number;
}

export interface OverviewResponse {
  rangeMinutes: number;
  binMinutes: number;
  kpis: OverviewKpis;
  series: OverviewPoint[];
  topFailing: { operation: string; failed: number; total: number }[];
  slowest: { operation: string; count: number; p95DurationMs: number }[];
  topExceptions: { type: string; message: string | null; problemId: string | null; count: number }[];
}

export interface OperationItem {
  itemId: string;
  kind: 'request' | 'dependency' | 'exception' | 'trace' | 'event';
  timestamp: string;
  id: string | null;
  parentId: string | null;
  name: string;
  detail: string | null;
  target: string | null;
  dependencyType: string | null;
  resultCode: string | null;
  success: boolean | null;
  durationMs: number | null;
  severityLevel: number | null;
  roleName: string | null;
}

export interface OperationTimeline {
  operationId: string;
  items: OperationItem[];
  truncated: boolean;
}
