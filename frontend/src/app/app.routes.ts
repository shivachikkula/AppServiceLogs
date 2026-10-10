import { Routes } from '@angular/router';
import { adminGuard, authGuard } from './core/auth';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'overview' },
  {
    path: 'overview',
    canActivate: [authGuard],
    title: 'Overview · OSSE Log Viewer',
    loadComponent: () => import('./pages/overview/overview').then((m) => m.Overview),
  },
  {
    path: 'live',
    canActivate: [authGuard],
    title: 'Live logs · OSSE Log Viewer',
    loadComponent: () => import('./pages/live-logs/live-logs').then((m) => m.LiveLogs),
  },
  {
    path: 'exceptions',
    canActivate: [authGuard],
    title: 'Exceptions · OSSE Log Viewer',
    loadComponent: () => import('./pages/exceptions/exceptions').then((m) => m.Exceptions),
  },
  {
    path: 'query',
    canActivate: [authGuard],
    title: 'Query · OSSE Log Viewer',
    loadComponent: () => import('./pages/query/query').then((m) => m.Query),
  },
  {
    path: 'admin',
    canActivate: [authGuard, adminGuard],
    title: 'Admin · OSSE Log Viewer',
    loadComponent: () => import('./pages/admin/admin').then((m) => m.Admin),
  },
  {
    path: 'operations/:operationId',
    canActivate: [authGuard],
    title: 'Operation timeline · OSSE Log Viewer',
    loadComponent: () => import('./pages/operation/operation').then((m) => m.Operation),
  },
  { path: '**', redirectTo: 'overview' },
];
