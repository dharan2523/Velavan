import { HttpClient, HttpContext, HttpErrorResponse } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, map, Observable, of, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  AUTH_EXPIRY_SAFETY_MARGIN_MS,
  LOGIN_ROUTE,
  PRIVILEGED_ROLES,
  ROLE_PRIORITY,
  SKIP_AUTH_REDIRECT
} from './auth.constants';
import {
  AuthResponse,
  AuthSession,
  ChangePasswordRequest,
  CurrentUser,
  LoginCredentials
} from './auth.models';
import { AuthStorageService } from './auth-storage.service';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly storage = inject(AuthStorageService);

  private readonly sessionState = signal<AuthSession | null>(null);
  private expiryTimer: ReturnType<typeof setTimeout> | null = null;
  private redirectingToLogin = false;

  readonly session = this.sessionState.asReadonly();
  readonly currentUser = computed(() => this.sessionState()?.user ?? null);
  readonly isAuthenticated = computed(() => this.sessionState() !== null);
  readonly roles = computed(() => this.currentUser()?.roles ?? []);
  readonly displayName = computed(() => {
    const user = this.currentUser();

    if (!user) {
      return '';
    }

    return `${user.firstName} ${user.lastName}`.trim() || user.username;
  });
  readonly initials = computed(() => {
    const user = this.currentUser();

    if (!user) {
      return '';
    }

    const initials = `${user.firstName.trim().charAt(0)}${user.lastName.trim().charAt(0)}`;

    return (initials || user.username.slice(0, 2)).toUpperCase();
  });
  readonly primaryRole = computed(() => {
    const roles = this.roles();

    return ROLE_PRIORITY.find(role => roles.includes(role)) ?? roles[0] ?? '';
  });
  readonly isAdmin = computed(() => this.hasAnyRole(PRIVILEGED_ROLES));

  login(credentials: LoginCredentials): Observable<AuthResponse> {
    const payload = {
      username: credentials.username.trim(),
      password: credentials.password
    };

    return this.http
      .post<AuthResponse>(`${environment.apiBaseUrl}/auth/login`, payload)
      .pipe(
        tap(response => {
          this.setSession({
            accessToken: response.accessToken,
            tokenType: response.tokenType || 'Bearer',
            expiresAtUtc: response.expiresAtUtc,
            user: response.user
          });
        })
      );
  }

  initializeSession(): Observable<void> {
    const stored = this.storage.read();

    if (!stored || !this.isSessionUsable(stored)) {
      this.clearSession();
      return of(undefined);
    }

    this.setSession(stored);

    return this.fetchCurrentUser(true).pipe(
      map(() => undefined),
      catchError((error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 401) {
          this.clearSession();
        }

        return of(undefined);
      })
    );
  }

  refreshCurrentUser(): Observable<CurrentUser> {
    return this.fetchCurrentUser(false);
  }

  changePassword(request: ChangePasswordRequest): Observable<void> {
    return this.http.post<void>(`${environment.apiBaseUrl}/auth/change-password`, {
      currentPassword: request.currentPassword,
      newPassword: request.newPassword
    });
  }

  logout(): void {
    this.clearSession();
    this.redirectingToLogin = false;
    void this.router.navigate([LOGIN_ROUTE], { replaceUrl: true });
  }

  handleUnauthorized(): void {
    if (this.redirectingToLogin) {
      return;
    }

    this.redirectingToLogin = true;
    this.clearSession();

    void this.router
      .navigate([LOGIN_ROUTE], {
        queryParams: { reason: 'expired' },
        replaceUrl: true
      })
      .finally(() => {
        this.redirectingToLogin = false;
      });
  }

  hasValidSession(): boolean {
    const session = this.sessionState() ?? this.storage.read();

    if (!session) {
      return false;
    }

    if (!this.isSessionUsable(session)) {
      this.clearSession();
      return false;
    }

    if (!this.sessionState()) {
      this.setSession(session);
    }

    return true;
  }

  getAuthorizationHeader(): string | null {
    if (!this.hasValidSession()) {
      return null;
    }

    const session = this.sessionState();

    return session ? `${session.tokenType} ${session.accessToken}` : null;
  }

  hasRole(role: string): boolean {
    return this.roles().includes(role);
  }

  hasAnyRole(requiredRoles: readonly string[]): boolean {
    if (requiredRoles.length === 0) {
      return true;
    }

    const roles = this.roles();

    return requiredRoles.some(role => roles.includes(role));
  }

  private fetchCurrentUser(skipAuthRedirect: boolean): Observable<CurrentUser> {
    const context = new HttpContext().set(SKIP_AUTH_REDIRECT, skipAuthRedirect);

    return this.http
      .get<CurrentUser>(`${environment.apiBaseUrl}/auth/me`, { context })
      .pipe(tap(user => this.updateCurrentUser(user)));
  }

  private setSession(session: AuthSession): void {
    this.redirectingToLogin = false;
    this.storage.write(session);
    this.sessionState.set(session);
    this.scheduleExpiry(session);
  }

  private updateCurrentUser(user: CurrentUser): void {
    const session = this.sessionState();

    if (!session) {
      return;
    }

    this.setSession({ ...session, user });
  }

  private clearSession(): void {
    this.clearExpiryTimer();
    this.storage.clear();
    this.sessionState.set(null);
  }

  private isSessionUsable(session: AuthSession): boolean {
    const expiresAt = Date.parse(session.expiresAtUtc);

    return (
      Number.isFinite(expiresAt) &&
      expiresAt - AUTH_EXPIRY_SAFETY_MARGIN_MS > Date.now()
    );
  }

  private scheduleExpiry(session: AuthSession): void {
    this.clearExpiryTimer();

    const expiresAt = Date.parse(session.expiresAtUtc);

    if (!Number.isFinite(expiresAt)) {
      this.clearSession();
      return;
    }

    const delay = expiresAt - Date.now() - AUTH_EXPIRY_SAFETY_MARGIN_MS;

    if (delay <= 0) {
      this.handleUnauthorized();
      return;
    }

    this.expiryTimer = setTimeout(() => {
      this.expiryTimer = null;
      this.handleUnauthorized();
    }, delay);
  }

  private clearExpiryTimer(): void {
    if (this.expiryTimer !== null) {
      clearTimeout(this.expiryTimer);
      this.expiryTimer = null;
    }
  }
}
