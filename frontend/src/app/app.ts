import { Component, OnInit, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ApplicationContextService } from './core/application-context.service';
import { AuthService } from './core/auth';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
})
export class App implements OnInit {
  protected readonly auth = inject(AuthService);
  protected readonly appContext = inject(ApplicationContextService);

  ngOnInit(): void {
    if (this.auth.isSignedIn()) {
      this.appContext.load();
    }
  }

  protected onSelect(appKey: string): void {
    this.appContext.select(appKey);
  }
}
