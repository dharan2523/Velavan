import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { finalize, Observable } from 'rxjs';
import { APP_ROLES } from '../../../core/auth/auth.constants';
import { getApiErrorMessage } from '../../../core/http/api-problem.util';
import { ManagedUser } from '../../../core/users/user.models';
import { UserManagementService } from '../../../core/users/user-management.service';
import {
  PASSWORD_POLICY_DESCRIPTION,
  passwordPolicyValidator
} from '../../../core/validation/password-policy';

export interface UserFormDialogData {
  mode: 'create' | 'edit';
  user?: ManagedUser;
  availableRoles: readonly string[];
}

export interface UserFormDialogResult {
  message: string;
}

@Component({
  selector: 'app-user-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule
  ],
  templateUrl: './user-form-dialog.component.html',
  styleUrl: './user-form-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class UserFormDialogComponent {
  private readonly userService = inject(UserManagementService);
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(
    MatDialogRef<UserFormDialogComponent, UserFormDialogResult>
  );

  readonly data = inject<UserFormDialogData>(MAT_DIALOG_DATA);
  readonly isCreate = this.data.mode === 'create';
  readonly passwordPolicyDescription = PASSWORD_POLICY_DESCRIPTION;
  readonly submitting = signal(false);
  readonly showPassword = signal(false);
  readonly errorMessage = signal('');

  readonly form = this.formBuilder.nonNullable.group({
    username: ['', [Validators.required, Validators.maxLength(256)]],
    email: ['', [Validators.email, Validators.maxLength(256)]],
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    lastName: ['', [Validators.required, Validators.maxLength(100)]],
    phoneNumber: ['', [Validators.maxLength(30)]],
    password: [
      '',
      this.isCreate
        ? [Validators.required, Validators.maxLength(200), passwordPolicyValidator]
        : []
    ],
    role: [
      this.getDefaultRole(),
      this.isCreate ? [Validators.required] : []
    ]
  });

  constructor() {
    const user = this.data.user;

    if (!this.isCreate && user) {
      this.form.patchValue({
        username: user.username,
        email: user.email ?? '',
        firstName: user.firstName,
        lastName: user.lastName,
        phoneNumber: user.phoneNumber ?? ''
      });
    }
  }

  submit(): void {
    if (this.submitting()) {
      return;
    }

    this.trimTextFields();

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const values = this.form.getRawValue();
    const email = values.email.trim() || null;
    const phoneNumber = values.phoneNumber.trim() || null;
    let request$: Observable<ManagedUser>;

    if (this.isCreate) {
      request$ = this.userService.createUser({
        username: values.username,
        email,
        firstName: values.firstName,
        lastName: values.lastName,
        phoneNumber,
        password: values.password,
        roles: [values.role]
      });
    } else {
      const user = this.data.user;

      if (!user) {
        this.errorMessage.set('The selected user could not be loaded.');
        return;
      }

      request$ = this.userService.updateUser(user.id, {
        username: values.username,
        email,
        firstName: values.firstName,
        lastName: values.lastName,
        phoneNumber
      });
    }

    this.submitting.set(true);
    this.errorMessage.set('');

    request$
      .pipe(finalize(() => this.submitting.set(false)))
      .subscribe({
        next: user => {
          this.dialogRef.close({
            message: this.isCreate
              ? `${user.firstName} ${user.lastName} was created.`
              : `${user.firstName} ${user.lastName} was updated.`
          });
        },
        error: (error: unknown) => {
          this.errorMessage.set(
            getApiErrorMessage(error, 'The user could not be saved. Please try again.')
          );
        }
      });
  }

  togglePasswordVisibility(): void {
    this.showPassword.update(visible => !visible);
  }

  cancel(): void {
    this.dialogRef.close();
  }

  private getDefaultRole(): string {
    return this.data.availableRoles.includes(APP_ROLES.user)
      ? APP_ROLES.user
      : this.data.availableRoles[0] ?? APP_ROLES.user;
  }

  private trimTextFields(): void {
    const controls = this.form.controls;

    controls.username.setValue(controls.username.value.trim());
    controls.email.setValue(controls.email.value.trim());
    controls.firstName.setValue(controls.firstName.value.trim());
    controls.lastName.setValue(controls.lastName.value.trim());
    controls.phoneNumber.setValue(controls.phoneNumber.value.trim());
  }
}
