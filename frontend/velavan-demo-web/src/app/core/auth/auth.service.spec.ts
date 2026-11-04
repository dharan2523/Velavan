import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { environment } from '../../../environments/environment';
import {
  APP_ROLES,
  AUTH_EXPIRY_SAFETY_MARGIN_MS,
  AUTH_STORAGE_KEY,
  LOGIN_ROUTE
} from './auth.constants';
import { AuthResponse, AuthSession, CurrentUser } from './auth.models';
import { AuthService } from './auth.service';
import { AuthStorageService } from './auth-storage.service';

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;
  let router: jasmine.SpyObj<Router>;
  let storage: AuthStorageService;

  const user: CurrentUser = {
    id: 1,
    username: 'superadmin',
    email: 'superadmin@velavandemo.local',
    firstName: 'Super',
    lastName: 'Admin',
    phoneNumber: null,
    status: 'Active',
    roles: [APP_ROLES.superAdmin],
    createdAtUtc: '2026-01-01T00:00:00Z',
    updatedAtUtc: null,
    lastLoginAtUtc: null
  };

  beforeEach(() => {
    router = jasmine.createSpyObj<Router>('Router', ['navigate']);
    router.navigate.and.resolveTo(true);

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Router, useValue: router }
      ]
    });

    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
    storage = TestBed.inject(AuthStorageService);
    sessionStorage.clear();
  });

  afterEach(() => {
    service.logout();
    sessionStorage.clear();
    httpMock.verify();
  });

  it('logs in, stores the session, and exposes the current user', () => {
    const response = createAuthResponse();
    let actual: AuthResponse | undefined;

    service
      .login({ username: '  superadmin ', password: 'secret' })
      .subscribe(result => {
        actual = result;
      });

    const request = httpMock.expectOne(`${environment.apiBaseUrl}/auth/login`);

    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      username: 'superadmin',
      password: 'secret'
    });

    request.flush(response);

    expect(actual?.accessToken).toBe(response.accessToken);
    expect(service.isAuthenticated()).toBeTrue();
    expect(service.currentUser()?.username).toBe(user.username);
    expect(service.displayName()).toBe('Super Admin');
    expect(service.initials()).toBe('SA');
    expect(service.primaryRole()).toBe(APP_ROLES.superAdmin);
    expect(service.isAdmin()).toBeTrue();
    expect(service.hasAnyRole([APP_ROLES.admin, APP_ROLES.superAdmin])).toBeTrue();
    expect(service.getAuthorizationHeader()).toBe(`Bearer ${response.accessToken}`);
    expect(storage.read()?.user.username).toBe(user.username);
  });

  it('does not create a session when login fails', () => {
    service.login({ username: user.username, password: 'wrong' }).subscribe({
      error: () => undefined
    });

    const request = httpMock.expectOne(`${environment.apiBaseUrl}/auth/login`);
    request.flush(
      { title: 'Authentication failed', status: 401 },
      { status: 401, statusText: 'Unauthorized' }
    );

    expect(service.isAuthenticated()).toBeFalse();
    expect(storage.read()).toBeNull();
    expect(sessionStorage.getItem(AUTH_STORAGE_KEY)).toBeNull();
  });

  it('loads the current user when a valid stored session exists', () => {
    storage.write(createSession());

    service.initializeSession().subscribe();

    const request = httpMock.expectOne(`${environment.apiBaseUrl}/auth/me`);
    expect(request.request.method).toBe('GET');

    request.flush({ ...user, lastName: 'Updated' });

    expect(service.hasValidSession()).toBeTrue();
    expect(service.currentUser()?.lastName).toBe('Updated');
    expect(storage.read()?.user.lastName).toBe('Updated');
  });

  it('clears a stored session when loading the current user returns 401', () => {
    storage.write(createSession());

    service.initializeSession().subscribe();

    httpMock
      .expectOne(`${environment.apiBaseUrl}/auth/me`)
      .flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(service.hasValidSession()).toBeFalse();
    expect(storage.read()).toBeNull();
  });

  it('does not call the API when no stored session exists', () => {
    let completed = false;

    service.initializeSession().subscribe(() => {
      completed = true;
    });

    expect(completed).toBeTrue();
    expect(service.isAuthenticated()).toBeFalse();
    httpMock.expectNone(`${environment.apiBaseUrl}/auth/me`);
  });

  it('rejects a stored session that is inside the expiry safety margin', () => {
    storage.write(
      createSession(
        new Date(Date.now() + AUTH_EXPIRY_SAFETY_MARGIN_MS - 1000).toISOString()
      )
    );

    expect(service.hasValidSession()).toBeFalse();
    expect(storage.read()).toBeNull();
  });

  it('clears the session and redirects when an unauthorized response is handled', () => {
    storage.write(createSession());

    service.handleUnauthorized();

    expect(service.isAuthenticated()).toBeFalse();
    expect(storage.read()).toBeNull();
    expect(router.navigate).toHaveBeenCalledWith([LOGIN_ROUTE], {
      queryParams: { reason: 'expired' },
      replaceUrl: true
    });
  });

  it('clears the session and redirects on logout', () => {
    storage.write(createSession());

    service.logout();

    expect(service.isAuthenticated()).toBeFalse();
    expect(storage.read()).toBeNull();
    expect(router.navigate).toHaveBeenCalledWith([LOGIN_ROUTE], {
      replaceUrl: true
    });
  });

  function createSession(expiresAtUtc = futureExpiry()): AuthSession {
    return {
      accessToken: 'test-access-token',
      tokenType: 'Bearer',
      expiresAtUtc,
      user: { ...user, roles: [...user.roles] }
    };
  }

  function createAuthResponse(): AuthResponse {
    const session = createSession();

    return {
      accessToken: session.accessToken,
      tokenType: session.tokenType,
      expiresAtUtc: session.expiresAtUtc,
      user: session.user
    };
  }

  function futureExpiry(): string {
    return new Date(Date.now() + 60 * 60 * 1000).toISOString();
  }
});
