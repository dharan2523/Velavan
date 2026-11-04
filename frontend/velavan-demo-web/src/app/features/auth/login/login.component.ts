import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { getSafeReturnUrl } from '../../../core/auth/auth-navigation';
import { BrandingService } from '../../../core/branding/branding.service';
import {
  getApiProblemDetail,
  getApiValidationMessage
} from '../../../core/http/api-problem.util';

@Component({
  selector: 'app-login',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule
  ],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly formBuilder = inject(FormBuilder);

  readonly branding = inject(BrandingService);
  readonly submitting = signal(false);
  readonly showPassword = signal(false);
  readonly errorMessage = signal(this.getInitialErrorMessage());
  readonly form = this.formBuilder.nonNullable.group({
    username: ['', [Validators.required, Validators.maxLength(256)]],
    password: ['', [Validators.required, Validators.maxLength(200)]]
  });

  submit(): void {
    if (this.submitting()) {
      return;
    }

    this.trimUsername();

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set('');

    const { username, password } = this.form.getRawValue();

    this.auth
      .login({ username, password })
      .pipe(finalize(() => this.submitting.set(false)))
      .subscribe({
        next: () => {
          const returnUrl = getSafeReturnUrl(
            this.route.snapshot.queryParamMap.get('returnUrl')
          );

          void this.router.navigateByUrl(returnUrl, { replaceUrl: true });
        },
        error: (error: unknown) => {
          this.errorMessage.set(this.getErrorMessage(error));
        }
      });
  }

  togglePasswordVisibility(): void {
    this.showPassword.update(visible => !visible);
  }

  private trimUsername(): void {
    const usernameControl = this.form.controls.username;
    const trimmedUsername = usernameControl.value.trim();

    if (trimmedUsername !== usernameControl.value) {
      usernameControl.setValue(trimmedUsername);
    }
  }

  private getInitialErrorMessage(): string {
    return this.route.snapshot.queryParamMap.get('reason') === 'expired'
      ? 'Your session has expired. Please sign in again.'
      : '';
  }

  private getErrorMessage(error: unknown): string {
    if (!(error instanceof HttpErrorResponse)) {
      return 'Sign in failed. Please try again.';
    }

    switch (error.status) {
      case 0:
        return 'Unable to reach the server. Check your connection and try again.';
      case 400:
        return (
          getApiValidationMessage(error.error) ??
          'Please check your details and try again.'
        );
      case 401:
        return 'Invalid username or password.';
      case 403:
        return (
          getApiProblemDetail(error.error) ??
          'This account is inactive or suspended.'
        );
      case 429:
        return 'Too many sign-in attempts. Please wait and try again.';
      default:
        return error.status >= 500
          ? 'The server could not complete the request. Please try again.'
          : 'Sign in failed. Please try again.';
    }
  }
}
