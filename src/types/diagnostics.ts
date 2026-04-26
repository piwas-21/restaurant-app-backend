/**
 * SSE Diagnostics Types
 *
 * TypeScript interfaces for the /api/events/diagnostics endpoint response.
 * Used to display server-side SSE connection status in the diagnostics modal.
 */

export interface ClientError {
  timestamp: string;
  errorType: string;
  message: string;
  eventType?: string;
}

export interface ClientDetail {
  clientId: string;
  ipAddress: string;
  country: string;
  connectedAt: string;
  connectedDuration: string;
  successfulSends: number;
  failedSends: number;
  lastEventSentAt?: string;
  errors: ClientError[];
  hasErrors: boolean;
  errorCount: number;
}

export interface RecentError {
  clientId: string;
  clientType: string;
  ipAddress: string;
  country: string;
  timestamp: string;
  errorType: string;
  message: string;
  eventType?: string;
}

export interface LogEntry {
  timestamp: string;
  level: string;
  message: string;
  eventType?: string;
  clientId?: string;
}

export interface SseDiagnostics {
  totalClients: number;
  kitchenClients: number;
  serviceClients: number;
  managerClients: number;
  stockClients: number;
  clientsWithErrors: number;
  totalErrors: number;
  totalSuccessfulSends: number;
  totalFailedSends: number;
  clientDetails: Record<string, ClientDetail[]>;
  recentErrors: RecentError[];
  recentLogs: LogEntry[];
  timestamp: string;
}
