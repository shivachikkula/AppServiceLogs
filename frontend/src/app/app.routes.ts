import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'live' },
  {
    path: 'live',
    title: 'Live logs',
    loadComponent: () => import('./pages/live-logs/live-logs').then((m) => m.LiveLogs),
  },
  {
    path: 'exceptions',
    title: 'Exceptions',
    loadComponent: () => import('./pages/exceptions/exceptions').then((m) => m.Exceptions),
  },
  { path: '**', redirectTo: 'live' },
];
