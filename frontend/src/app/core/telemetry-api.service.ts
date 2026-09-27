import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  ApplicationSummary,
  ExceptionDetail,
  ExceptionEntry,
  ExceptionGroup,
  ExceptionsFilter,
  LiveLogsFilter,
  LiveLogsResponse,
  UserInfo,
} from './models';

@Injectable({ providedIn: 'root' })
export class TelemetryApiService {
  private readonly http = inject(HttpClient);

  getMe(): Observable<UserInfo> {
    return this.http.get<UserInfo>('/api/me');
  }

  getApplications(): Observable<ApplicationSummary[]> {
    return this.http.get<ApplicationSummary[]>('/api/applications');
  }

  getLiveLogs(filter: LiveLogsFilter): Observable<LiveLogsResponse> {
    return this.http.get<LiveLogsResponse>('/api/logs/live', {
      params: toParams({ ...filter, types: filter.types.join(',') }),
    });
  }

  getExceptions(filter: ExceptionsFilter): Observable<ExceptionEntry[]> {
    return this.http.get<ExceptionEntry[]>('/api/exceptions', { params: toParams(filter) });
  }

  getExceptionSummary(filter: ExceptionsFilter): Observable<ExceptionGroup[]> {
    const { appKey, rangeMinutes, search, roleName } = filter;
    return this.http.get<ExceptionGroup[]>('/api/exceptions/summary', {
      params: toParams({ appKey, rangeMinutes, search, roleName }),
    });
  }

  getException(appKey: string, itemId: string, rangeMinutes: number): Observable<ExceptionDetail> {
    return this.http.get<ExceptionDetail>(`/api/exceptions/${encodeURIComponent(itemId)}`, {
      params: toParams({ appKey, rangeMinutes }),
    });
  }
}

function toParams(values: object): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(values)) {
    if (value !== undefined && value !== null && value !== '') {
      params = params.set(key, String(value));
    }
  }
  return params;
}

/** Extracts a readable message from an API error (ProblemDetails or network failure). */
export function describeError(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) {
      return 'Cannot reach the API. Is the backend running?';
    }
    const body = error.error as { detail?: string; title?: string } | null;
    return body?.detail || body?.title || `${error.status} ${error.statusText}`;
  }
  return error instanceof Error ? error.message : String(error);
}
