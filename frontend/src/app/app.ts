import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { catchError, of } from 'rxjs';
import { TelemetryApiService } from './core/telemetry-api.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
})
export class App {
  private readonly api = inject(TelemetryApiService);

  protected readonly status = toSignal(this.api.getStatus().pipe(catchError(() => of(null))), {
    initialValue: undefined,
  });
}
