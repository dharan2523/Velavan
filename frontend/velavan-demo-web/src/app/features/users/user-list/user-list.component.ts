import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { debounceTime, distinctUntilChanged, finalize } from 'rxjs';
import { APP_ROLES, PRIVILEGED_ROLES, ROLE_PRIORITY } from '../../../core/auth/auth.constants';
import { AuthService } from '../../../core/auth/auth.service';
import { getApiErrorMessage } from '../../../core/http/api-problem.util';
import { ManagedUser, UserStatus } from '../../../core/users/user.models';
import { UserManagementService } from '../../../core/users/user-management.service';
import {
  ConfirmDialogComponent,
  ConfirmDialogData
} from '../confirm-dialog/confirm-dialog.component';
import {
  UserFormDialogComponent,
  UserFormDialogData,
  UserFormDialogResult
} from '../user-form-dialog/user-form-dialog.component';
import {
  UserRolesDialogComponent,
  UserRolesDialogData,
  UserRolesDialogResult
} from '../user-roles-dialog/user-roles-dialog.component';

const PRIVILEGED_ROLE_NAMES: readonly string[] = PRIVILEGED_ROLES;

@Component({
  selector: 'app-user-list',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatSelectModule,
    MatTableModule,
    MatTooltipModule
  ],
  templateUrl: './user-list.component.html',
  styleUrl: './user-list.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class UserListComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly userService = inject(UserManagementService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly displayedColumns = ['name', 'username', 'roles', 'status', 'created', 'actions'];
  readonly statusOptions: readonly UserStatus[] = ['Active', 'Inactive', 'Suspended'];
  readonly roleOptions = ROLE_PRIORITY;
  readonly pageSizeOptions = [10, 20, 50];

  readonly users = signal<ManagedUser[]>([]);
  readonly totalCount = signal(0);
  readonly loading = signal(false);
  readonly loadError = signal('');
  readonly busyUserId = signal<number | null>(null);
  readonly pageIndex = signal(0);
  readonly pageSize = signal(10);

  readonly searchControl = new FormControl('', { nonNullable: true });
  readonly statusControl = new FormControl<UserStatus | ''>('', { nonNullable: true });
  readonly roleControl = new FormControl('', { nonNullable: true });

  readonly currentUserId = computed(() => this.auth.currentUser()?.id ?? null);
  readonly isSuperAdmin = computed(() => this.auth.hasRole(APP_ROLES.superAdmin));

  private loadSequence = 0;

  constructor() {
    this.searchControl.valueChanges
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe(() => this.reloadFromFirstPage());

    this.statusControl.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.reloadFromFirstPage());

    this.roleControl.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.reloadFromFirstPage());
  }

  ngOnInit(): void {
    this.loadUsers();
  }

  loadUsers(): void {
    const sequence = ++this.loadSequence;
    const status = this.statusControl.value || undefined;
    const role = this.roleControl.value || undefined;

    this.loading.set(true);
    this.loadError.set('');

    this.userService
      .getUsers({
        page: this.pageIndex() + 1,
        pageSize: this.pageSize(),
        search: this.searchControl.value,
        status,
        role
      })
      .pipe(
        finalize(() => {
          if (sequence === this.loadSequence) {
            this.loading.set(false);
          }
        })
      )
      .subscribe({
        next: result => {
          if (sequence !== this.loadSequence) {
            return;
          }

          this.users.set(result.items);
          this.totalCount.set(result.totalCount);
        },
        error: (error: unknown) => {
          if (sequence !== this.loadSequence) {
            return;
          }

          this.users.set([]);
          this.totalCount.set(0);
          this.loadError.set(
            getApiErrorMessage(error, 'Unable to load users. Please try again.')
          );
        }
      });
  }

  onPageChanged(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    this.loadUsers();
  }

  resetFilters(): void {
    this.searchControl.setValue('', { emitEvent: false });
    this.statusControl.setValue('', { emitEvent: false });
    this.roleControl.setValue('', { emitEvent: false });
    this.reloadFromFirstPage();
  }

  hasActiveFilters(): boolean {
    return (
      this.searchControl.value.trim().length > 0 ||
      this.statusControl.value !== '' ||
      this.roleControl.value !== ''
    );
  }

  openCreateDialog(): void {
    if (!this.auth.isAdmin()) {
      return;
    }

    const availableRoles = this.isSuperAdmin()
      ? [...ROLE_PRIORITY]
      : [APP_ROLES.user];

    this.dialog
      .open<UserFormDialogComponent, UserFormDialogData, UserFormDialogResult>(
        UserFormDialogComponent,
        {
          data: {
            mode: 'create',
            availableRoles
          } satisfies UserFormDialogData,
          width: '560px',
          maxWidth: 'calc(100vw - 32px)',
          autoFocus: 'first-tabbable'
        }
      )
      .afterClosed()
      .subscribe(result => {
        if (result) {
          this.notify(result.message);
          this.pageIndex.set(0);
          this.loadUsers();
        }
      });
  }

  openEditDialog(user: ManagedUser): void {
    if (!this.canEditUser(user)) {
      return;
    }

    this.dialog
      .open<UserFormDialogComponent, UserFormDialogData, UserFormDialogResult>(
        UserFormDialogComponent,
        {
          data: {
            mode: 'edit',
            user,
            availableRoles: []
          } satisfies UserFormDialogData,
          width: '560px',
          maxWidth: 'calc(100vw - 32px)',
          autoFocus: 'first-tabbable'
        }
      )
      .afterClosed()
      .subscribe(result => {
        if (result) {
          this.notify(result.message);
          this.loadUsers();
        }
      });
  }

  openRolesDialog(user: ManagedUser): void {
    if (!this.canManageRoles()) {
      return;
    }

    this.dialog
      .open<UserRolesDialogComponent, UserRolesDialogData, UserRolesDialogResult>(
        UserRolesDialogComponent,
        {
          data: {
            user,
            availableRoles: ROLE_PRIORITY
          } satisfies UserRolesDialogData,
          width: '500px',
          maxWidth: 'calc(100vw - 32px)',
          autoFocus: 'first-tabbable'
        }
      )
      .afterClosed()
      .subscribe(result => {
        if (result) {
          this.notify(result.message);
          this.loadUsers();
        }
      });
  }

  openStatusDialog(user: ManagedUser): void {
    if (!this.canChangeStatus(user)) {
      return;
    }

    const nextStatus: UserStatus = user.status === 'Active' ? 'Inactive' : 'Active';
    const action = nextStatus === 'Active' ? 'Activate' : 'Deactivate';

    this.dialog
      .open<ConfirmDialogComponent, ConfirmDialogData, boolean>(ConfirmDialogComponent, {
        data: {
          title: `${action} user`,
          message:
            nextStatus === 'Active'
              ? `Activate ${user.firstName} ${user.lastName}? They will be able to sign in again.`
              : `Deactivate ${user.firstName} ${user.lastName}? They will be signed out and blocked from signing in.`,
          confirmLabel: action,
          destructive: nextStatus === 'Inactive'
        },
        width: '440px',
        maxWidth: 'calc(100vw - 32px)'
      })
      .afterClosed()
      .subscribe(confirmed => {
        if (confirmed) {
          this.updateStatus(user, nextStatus);
        }
      });
  }

  canEditUser(user: ManagedUser): boolean {
    return this.auth.isAdmin() && (this.isSuperAdmin() || !this.isPrivilegedUser(user));
  }

  canManageRoles(): boolean {
    return this.auth.isAdmin() && this.isSuperAdmin();
  }

  canChangeStatus(user: ManagedUser): boolean {
    if (!this.auth.isAdmin() || user.id === this.currentUserId()) {
      return false;
    }

    return this.isSuperAdmin() || !this.isPrivilegedUser(user);
  }

  hasRowActions(user: ManagedUser): boolean {
    return (
      this.canEditUser(user) ||
      this.canManageRoles() ||
      this.canChangeStatus(user)
    );
  }

  isPrivilegedUser(user: ManagedUser): boolean {
    return user.roles.some(role => PRIVILEGED_ROLE_NAMES.includes(role));
  }

  isPrivilegedRole(role: string): boolean {
    return PRIVILEGED_ROLE_NAMES.includes(role);
  }

  statusActionLabel(user: ManagedUser): string {
    return user.status === 'Active' ? 'Deactivate user' : 'Activate user';
  }

  statusActionIcon(user: ManagedUser): string {
    return user.status === 'Active' ? 'person_off' : 'how_to_reg';
  }

  userInitials(user: ManagedUser): string {
    const initials = `${user.firstName.trim().charAt(0)}${user.lastName.trim().charAt(0)}`;

    return (initials || user.username.slice(0, 2)).toUpperCase();
  }

  private updateStatus(user: ManagedUser, status: UserStatus): void {
    if (this.busyUserId() !== null) {
      return;
    }

    this.busyUserId.set(user.id);

    this.userService
      .updateUserStatus(user.id, status)
      .pipe(finalize(() => this.busyUserId.set(null)))
      .subscribe({
        next: updated => {
          this.notify(
            `${updated.firstName} ${updated.lastName} was ${status === 'Active' ? 'activated' : 'deactivated'}.`
          );
          this.loadUsers();
        },
        error: (error: unknown) => {
          this.notify(
            getApiErrorMessage(error, 'The account status could not be changed.')
          );
        }
      });
  }

  private reloadFromFirstPage(): void {
    this.pageIndex.set(0);
    this.loadUsers();
  }

  private notify(message: string): void {
    this.snackBar.open(message, 'Dismiss', { duration: 4000 });
  }
}

