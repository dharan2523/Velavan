import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { getSafeReturnUrl } from './auth-navigation';
import { AuthService } from './auth.service';

export const guestGuard: CanActivateFn = route => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!auth.hasValidSession()) {
    return true;
  }

  const returnUrl = getSafeReturnUrl(route.queryParamMap.get('returnUrl'));

  return router.parseUrl(returnUrl);
};
