import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { APP_ROLES } from '../../../core/auth/auth.constants';
import { AuthResponse } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import { LoginComponent } from './login.component';

describe('LoginComponent', () => {
  let fixture: ComponentFixture<LoginComponent>;
  let component: LoginComponent;
  let auth: AuthService;
  let httpMock: HttpTestingController;
  let router: jasmine.SpyObj<Router>;
  let queryParams: Record<string, string>;

  beforeEach(() => {
    queryParams = {};

    const routeStub = {
      get snapshot() {
        return { queryParamMap: convertToParamMap(queryParams) };
      }
    } as unknown as ActivatedRoute;

    router = jasmine.createSpyObj<Router>('Router', ['navigate', 'navigateByUrl']);
    router.navigate.and.resolveTo(true);
    router.navigateByUrl.and.resolveTo(true);

    TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        { provide: Router, useValue: router },
        { provide: ActivatedRoute, useValue: routeStub }
      ]
    });

    auth = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
    sessionStorage.clear();
  });

  afterEach(() => {
    auth.logout();
    sessionStorage.clear();
    httpMock.verify();
  });

  it('marks invalid fields as touched without sending a request', () => {
    createComponent();

    component.form.setValue({ username: '   ', password: '' });
    component.submit();

    expect(component.form.touched).toBeTrue();
    expect(component.form.controls.username.hasError('required')).toBeTrue();
    expect(component.form.controls.password.hasError('required')).toBeTrue();
    httpMock.expectNone(`${environment.apiBaseUrl}/auth/login`);
  });

  it('signs in and navigates to the return URL', () => {
    createComponent({ returnUrl: '/orders' });

    component.form.setValue({
      username: '  superadmin ',
      password: 'secret'
    });
    component.submit();

    const request = httpMock.expectOne(`${environment.apiBaseUrl}/auth/login`);

    expect(request.request.body).toEqual({
      username: 'superadmin',
      password: 'secret'
    });

    request.flush(createAuthResponse());

    expect(component.submitting()).toBeFalse();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/orders', {
      replaceUrl: true
    });
  });

  it('shows a generic message for invalid credentials', () => {
    createComponent();
    submitValidCredentials();

    httpMock
      .expectOne(`${environment.apiBaseUrl}/auth/login`)
      .flush(
        { title: 'Authentication failed' },
        { status: 401, statusText: 'Unauthorized' }
      );

    expect(component.errorMessage()).toBe('Invalid username or password.');
    expect(component.submitting()).toBeFalse();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('shows the inactive-account message when login returns 403', () => {
    createComponent();
    submitValidCredentials();

    httpMock
      .expectOne(`${environment.apiBaseUrl}/auth/login`)
      .flush(
        { detail: 'The user account is inactive or suspended.' },
        { status: 403, statusText: 'Forbidden' }
      );

    expect(component.errorMessage()).toBe(
      'The user account is inactive or suspended.'
    );
  });

  it('shows the session-expired message from the query string', () => {
    createComponent({ reason: 'expired' });

    expect(component.errorMessage()).toContain('session has expired');
  });

  it('falls back to the dashboard for an unsafe return URL', () => {
    createComponent({ returnUrl: 'https://example.com' });
    submitValidCredentials();

    httpMock
      .expectOne(`${environment.apiBaseUrl}/auth/login`)
      .flush(createAuthResponse());

    expect(router.navigateByUrl).toHaveBeenCalledWith('/', {
      replaceUrl: true
    });
  });

  function createComponent(params: Record<string, string> = {}): void {
    queryParams = params;
    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function submitValidCredentials(): void {
    component.form.setValue({
      username: 'superadmin',
      password: 'secret'
    });
    component.submit();
  }

  function createAuthResponse(): AuthResponse {
    return {
      accessToken: 'test-token',
      tokenType: 'Bearer',
      expiresAtUtc: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
      user: {
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
      }
    };
  }
});
