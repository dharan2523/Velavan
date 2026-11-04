import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  convertToParamMap,
  Router,
  RouterStateSnapshot,
  UrlTree
} from '@angular/router';
import { authGuard } from './auth.guard';
import { APP_ROLES, FORBIDDEN_ROUTE, LOGIN_ROUTE } from './auth.constants';
import { AuthService } from './auth.service';
import { guestGuard } from './guest.guard';
import { roleGuard } from './role.guard';

describe('auth guards', () => {
  let auth: jasmine.SpyObj<AuthService>;
  let router: jasmine.SpyObj<Router>;

  const urlTree = {} as UrlTree;

  beforeEach(() => {
    auth = jasmine.createSpyObj<AuthService>('AuthService', [
      'hasValidSession',
      'hasAnyRole'
    ]);
    router = jasmine.createSpyObj<Router>('Router', ['createUrlTree', 'parseUrl']);
    router.createUrlTree.and.returnValue(urlTree);
    router.parseUrl.and.returnValue(urlTree);

    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: auth },
        { provide: Router, useValue: router }
      ]
    });
  });

  it('allows authenticated users through the auth guard', () => {
    auth.hasValidSession.and.returnValue(true);

    const result = TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, { url: '/dashboard' } as RouterStateSnapshot)
    );

    expect(result).toBeTrue();
    expect(router.createUrlTree).not.toHaveBeenCalled();
  });

  it('redirects unauthenticated users to login with a return URL', () => {
    auth.hasValidSession.and.returnValue(false);

    const result = TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, { url: '/orders?page=2' } as RouterStateSnapshot)
    );

    expect(result).toBe(urlTree);
    expect(router.createUrlTree).toHaveBeenCalledWith([LOGIN_ROUTE], {
      queryParams: { returnUrl: '/orders?page=2' }
    });
  });

  it('allows unauthenticated users to access the login route', () => {
    auth.hasValidSession.and.returnValue(false);

    const result = TestBed.runInInjectionContext(() =>
      guestGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot)
    );

    expect(result).toBeTrue();
  });

  it('redirects authenticated users away from login using a safe return URL', () => {
    auth.hasValidSession.and.returnValue(true);

    const route = {
      queryParamMap: convertToParamMap({ returnUrl: '/orders' })
    } as unknown as ActivatedRouteSnapshot;

    const result = TestBed.runInInjectionContext(() =>
      guestGuard(route, {} as RouterStateSnapshot)
    );

    expect(result).toBe(urlTree);
    expect(router.parseUrl).toHaveBeenCalledWith('/orders');
  });

  it('rejects external return URLs', () => {
    auth.hasValidSession.and.returnValue(true);

    const route = {
      queryParamMap: convertToParamMap({ returnUrl: 'https://example.com' })
    } as unknown as ActivatedRouteSnapshot;

    TestBed.runInInjectionContext(() => guestGuard(route, {} as RouterStateSnapshot));

    expect(router.parseUrl).toHaveBeenCalledWith('/');
  });

  it('allows users with a matching role', () => {
    auth.hasValidSession.and.returnValue(true);
    auth.hasAnyRole.and.returnValue(true);

    const result = TestBed.runInInjectionContext(() =>
      roleGuard(routeWithRoles([APP_ROLES.admin]), {} as RouterStateSnapshot)
    );

    expect(result).toBeTrue();
    expect(auth.hasAnyRole).toHaveBeenCalledWith([APP_ROLES.admin]);
  });

  it('redirects users without a matching role to forbidden', () => {
    auth.hasValidSession.and.returnValue(true);
    auth.hasAnyRole.and.returnValue(false);

    const result = TestBed.runInInjectionContext(() =>
      roleGuard(routeWithRoles([APP_ROLES.admin]), {} as RouterStateSnapshot)
    );

    expect(result).toBe(urlTree);
    expect(router.createUrlTree).toHaveBeenCalledWith([FORBIDDEN_ROUTE]);
  });

  it('allows routes that do not declare roles', () => {
    const result = TestBed.runInInjectionContext(() =>
      roleGuard(routeWithRoles(undefined), {} as RouterStateSnapshot)
    );

    expect(result).toBeTrue();
    expect(auth.hasValidSession).not.toHaveBeenCalled();
  });

  it('redirects unauthenticated users to login from a role-protected route', () => {
    auth.hasValidSession.and.returnValue(false);

    const result = TestBed.runInInjectionContext(() =>
      roleGuard(routeWithRoles([APP_ROLES.superAdmin]), {
        url: '/admin'
      } as RouterStateSnapshot)
    );

    expect(result).toBe(urlTree);
    expect(router.createUrlTree).toHaveBeenCalledWith([LOGIN_ROUTE], {
      queryParams: { returnUrl: '/admin' }
    });
  });

  function routeWithRoles(roles: unknown): ActivatedRouteSnapshot {
    return { data: { roles } } as unknown as ActivatedRouteSnapshot;
  }
});
