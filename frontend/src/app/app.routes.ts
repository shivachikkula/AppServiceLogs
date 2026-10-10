import { Routes } from '@angular/router';
import { authGuard } from './core/auth';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'live' },
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
  { path: '**', redirectTo: 'live' },
];
