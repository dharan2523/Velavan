import {
  HttpClient,
  HttpContext,
  provideHttpClient,
  withInterceptors
} from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { environment } from '../../../environments/environment';
import { APP_ROLES, LOGIN_ROUTE, SKIP_AUTH_REDIRECT } from './auth.constants';
import { authInterceptor } from './auth.interceptor';
import { AuthSession } from './auth.models';
import { AuthService } from './auth.service';
import { AuthStorageService } from './auth-storage.service';

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let auth: AuthService;
  let router: jasmine.SpyObj<Router>;
  let storage: AuthStorageService;

  beforeEach(() => {
    router = jasmine.createSpyObj<Router>('Router', ['navigate']);
    router.navigate.and.resolveTo(true);

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: Router, useValue: router }
      ]
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    storage = TestBed.inject(AuthStorageService);
    sessionStorage.clear();
  });

  afterEach(() => {
    auth.logout();
    sessionStorage.clear();
    httpMock.verify();
  });

  it('attaches the bearer token to API requests', () => {
    storage.write(createSession());

    http.get(`${environment.apiBaseUrl}/users`).subscribe();

    const request = httpMock.expectOne(`${environment.apiBaseUrl}/users`);

    expect(request.request.headers.get('Authorization')).toBe('Bearer test-token');
    request.flush({});
  });

  it('does not attach the bearer token to the login request', () => {
    storage.write(createSession());

    http
      .post(`${environment.apiBaseUrl}/auth/login`, {
        username: 'admin',
        password: 'secret'
      })
      .subscribe();

    const request = httpMock.expectOne(`${environment.apiBaseUrl}/auth/login`);

    expect(request.request.headers.has('Authorization')).toBeFalse();
    request.flush({});
  });

  it('does not attach the bearer token to non-API requests', () => {
    storage.write(createSession());

    http.get('https://example.com/data').subscribe();

    const request = httpMock.expectOne('https://example.com/data');

    expect(request.request.headers.has('Authorization')).toBeFalse();
    request.flush({});
  });

  it('clears the session and redirects on a protected API 401', () => {
    storage.write(createSession());

    http.get(`${environment.apiBaseUrl}/users`).subscribe({
      error: () => undefined
    });

    httpMock
      .expectOne(`${environment.apiBaseUrl}/users`)
      .flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(auth.isAuthenticated()).toBeFalse();
    expect(router.navigate).toHaveBeenCalledWith([LOGIN_ROUTE], {
      queryParams: { reason: 'expired' },
      replaceUrl: true
    });
  });

  it('does not redirect on a 401 when the auth redirect is skipped', () => {
    storage.write(createSession());
    const context = new HttpContext().set(SKIP_AUTH_REDIRECT, true);

    http
      .get(`${environment.apiBaseUrl}/auth/me`, { context })
      .subscribe({ error: () => undefined });

    httpMock
      .expectOne(`${environment.apiBaseUrl}/auth/me`)
      .flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(router.navigate).not.toHaveBeenCalled();
  });

  function createSession(): AuthSession {
    return {
      accessToken: 'test-token',
      tokenType: 'Bearer',
      expiresAtUtc: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
      user: {
        id: 1,
        username: 'admin',
        email: 'admin@example.com',
        firstName: 'Super',
        lastName: 'Admin',
        phoneNumber: null,
        status: 'Active',
        roles: [APP_ROLES.superAdmin],
        createdAtUtc: '2026-01-01T00:00:00Z',
        updatedAtUtc: null,
        lastLoginAtUtc: null
      }
    };
  }
});
