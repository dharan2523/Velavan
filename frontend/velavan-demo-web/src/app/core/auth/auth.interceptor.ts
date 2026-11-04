import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SKIP_AUTH_REDIRECT } from './auth.constants';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const isApiRequest = request.url.startsWith(environment.apiBaseUrl);
  const isLoginRequest = request.url.endsWith('/auth/login');

  if (!isApiRequest || isLoginRequest) {
    return next(request);
  }

  const authorization = auth.getAuthorizationHeader();
  const authenticatedRequest = authorization
    ? request.clone({ setHeaders: { Authorization: authorization } })
    : request;

  return next(authenticatedRequest).pipe(
    catchError((error: unknown) => {
      if (
        error instanceof HttpErrorResponse &&
        error.status === 401 &&
        !request.context.get(SKIP_AUTH_REDIRECT)
      ) {
        auth.handleUnauthorized();
      }

      return throwError(() => error);
    })
  );
};
