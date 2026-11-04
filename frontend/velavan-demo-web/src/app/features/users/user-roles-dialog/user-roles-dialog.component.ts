import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { finalize } from 'rxjs';
import { APP_ROLES, ROLE_PRIORITY } from '../../../core/auth/auth.constants';
import { getApiErrorMessage } from '../../../core/http/api-problem.util';
import { ManagedUser } from '../../../core/users/user.models';
import { UserManagementService } from '../../../core/users/user-management.service';

export interface UserRolesDialogData {
  user: ManagedUser;
  availableRoles: readonly string[];
}

export interface UserRolesDialogResult {
  message: string;
}

@Component({
  selector: 'app-user-roles-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatSelectModule
  ],
  templateUrl: './user-roles-dialog.component.html',
  styleUrl: './user-roles-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class UserRolesDialogComponent {
  private readonly userService = inject(UserManagementService);
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(
    MatDialogRef<UserRolesDialogComponent, UserRolesDialogResult>
  );

  readonly data = inject<UserRolesDialogData>(MAT_DIALOG_DATA);
  readonly submitting = signal(false);
  readonly errorMessage = signal('');
  readonly form = this.formBuilder.nonNullable.group({
    role: [this.getCurrentRole(), [Validators.required]]
  });

  submit(): void {
    if (this.submitting()) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const role = this.form.controls.role.value;

    this.submitting.set(true);
    this.errorMessage.set('');

    this.userService
      .updateUserRoles(this.data.user.id, [role])
      .pipe(finalize(() => this.submitting.set(false)))
      .subscribe({
        next: user => {
          this.dialogRef.close({
            message: `${user.firstName} ${user.lastName} now has the ${role} role.`
          });
        },
        error: (error: unknown) => {
          this.errorMessage.set(
            getApiErrorMessage(error, 'The role could not be updated. Please try again.')
          );
        }
      });
  }

  cancel(): void {
    this.dialogRef.close();
  }

  private getCurrentRole(): string {
    const currentRole = ROLE_PRIORITY.find(role => this.data.user.roles.includes(role));

    return currentRole ?? this.data.availableRoles[0] ?? APP_ROLES.user;
  }
}
