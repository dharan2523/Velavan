import {
  ChangeDetectionStrategy,
  Component,
  inject,
  output,
  signal
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-header',
  imports: [MatButtonModule, MatIconModule],
  templateUrl: './header.component.html',
  styleUrl: './header.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class HeaderComponent {
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  readonly menuToggle = output<void>();
  readonly isAuthenticated = this.auth.isAuthenticated;
  readonly displayName = this.auth.displayName;
  readonly initials = this.auth.initials;
  readonly primaryRole = this.auth.primaryRole;
  readonly pageTitle = signal(this.resolvePageTitle());

  constructor() {
    this.router.events
      .pipe(
        filter((event): event is NavigationEnd => event instanceof NavigationEnd),
        takeUntilDestroyed()
      )
      .subscribe(() => this.pageTitle.set(this.resolvePageTitle()));
  }

  async openChangePassword(): Promise<void> {
    const { ChangePasswordDialogComponent } = await import(
      '../../features/account/change-password-dialog/change-password-dialog.component'
    );

    this.dialog
      .open(ChangePasswordDialogComponent, {
        width: '500px',
        maxWidth: 'calc(100vw - 32px)',
        autoFocus: 'first-tabbable'
      })
      .afterClosed()
      .subscribe(changed => {
        if (changed) {
          this.snackBar.open('Password changed successfully.', 'Dismiss', {
            duration: 4000
          });
        }
      });
  }

  logout(): void {
    this.auth.logout();
  }

  private resolvePageTitle(): string {
    let route: ActivatedRoute | null = this.router.routerState.root;
    let pageTitle: string | undefined;

    while (route) {
      const title: unknown = route.snapshot?.data['title'];

      if (typeof title === 'string' && title.trim().length > 0) {
        pageTitle = title.trim();
      }

      route = route.firstChild;
    }

    return pageTitle ?? 'Dashboard';
  }
}

