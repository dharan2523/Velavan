import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { FORBIDDEN_ROUTE, LOGIN_ROUTE } from './auth.constants';
import { AuthService } from './auth.service';

export const roleGuard: CanActivateFn = (route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const requiredRoles = route.data?.['roles'];

  if (!Array.isArray(requiredRoles) || requiredRoles.length === 0) {
    return true;
  }

  if (!auth.hasValidSession()) {
    return router.createUrlTree([LOGIN_ROUTE], {
      queryParams: { returnUrl: state.url }
    });
  }

  const roles = requiredRoles.filter((role): role is string => typeof role === 'string');

  return auth.hasAnyRole(roles) ? true : router.createUrlTree([FORBIDDEN_ROUTE]);
};
