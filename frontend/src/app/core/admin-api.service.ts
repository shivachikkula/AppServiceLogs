import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { AdminApplication, AdminGrant, ApplicationCheck, AuditEntry, DiagnosticCheck } from './models';

@Injectable({ providedIn: 'root' })
export class AdminApiService {
  private readonly http = inject(HttpClient);

  getGrants(): Observable<AdminGrant[]> {
    return this.http.get<AdminGrant[]>('/api/admin/grants');
  }

  createGrant(email: string, applicationName: string, appKey: string): Observable<AdminGrant> {
    return this.http.post<AdminGrant>('/api/admin/grants', { email, applicationName, appKey });
  }

  deleteGrant(id: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/grants/${id}`);
  }

  getApplications(): Observable<AdminApplication[]> {
    return this.http.get<AdminApplication[]>('/api/admin/applications');
  }

  renameApplication(appKey: string, applicationName: string): Observable<number> {
    return this.http.put<number>(`/api/admin/applications/${encodeURIComponent(appKey)}`, { applicationName });
  }

  checkApplication(appKey: string): Observable<ApplicationCheck> {
    return this.http.post<ApplicationCheck>(`/api/admin/applications/${encodeURIComponent(appKey)}/check`, {});
  }

  getAudit(filter: { email?: string; appKey?: string; failedOnly?: boolean; beforeId?: number; take?: number }): Observable<AuditEntry[]> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value !== undefined && value !== null && value !== '' && value !== false) {
        params = params.set(key, String(value));
      }
    }
    return this.http.get<AuditEntry[]>('/api/admin/audit', { params });
  }

  getDiagnostics(): Observable<DiagnosticCheck[]> {
    return this.http.get<DiagnosticCheck[]>('/api/admin/diagnostics');
  }
}
