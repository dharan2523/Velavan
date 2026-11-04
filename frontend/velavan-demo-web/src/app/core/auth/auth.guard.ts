import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { LOGIN_ROUTE } from './auth.constants';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.hasValidSession()) {
    return true;
  }

  return router.createUrlTree([LOGIN_ROUTE], {
    queryParams: { returnUrl: state.url }
  });
};
