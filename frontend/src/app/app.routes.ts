import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'live' },
  {
    path: 'live',
    title: 'Live logs · OSSE Application Log Viewer',
    loadComponent: () => import('./pages/live-logs/live-logs').then((m) => m.LiveLogs),
  },
  {
    path: 'exceptions',
    title: 'Exceptions · OSSE Application Log Viewer',
    loadComponent: () => import('./pages/exceptions/exceptions').then((m) => m.Exceptions),
  },
  { path: '**', redirectTo: 'live' },
];
