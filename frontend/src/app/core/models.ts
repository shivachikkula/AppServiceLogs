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
  rangeMinutes: number;
  search?: string;
  problemId?: string;
  roleName?: string;
  operationId?: string;
  take?: number;
}

export interface StatusResponse {
  configured: boolean;
  applicationId: string | null;
  authenticationMode: string;
  queryEndpoint: string;
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
