import { signal } from '@angular/core';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of } from 'rxjs';
import { APP_ROLES } from '../../../core/auth/auth.constants';
import { AuthService } from '../../../core/auth/auth.service';
import { CurrentUser } from '../../../core/auth/auth.models';
import { ManagedUser, PagedResult } from '../../../core/users/user.models';
import { UserManagementService } from '../../../core/users/user-management.service';
import { UserListComponent } from './user-list.component';

describe('UserListComponent', () => {
  let fixture: ComponentFixture<UserListComponent>;
  let component: UserListComponent;
  let userService: jasmine.SpyObj<UserManagementService>;
  let dialog: jasmine.SpyObj<MatDialog>;
  let snackBar: jasmine.SpyObj<MatSnackBar>;

  const superAdmin: CurrentUser = {
    id: 1,
    username: 'super',
    email: 'super@example.com',
    firstName: 'Super',
    lastName: 'Admin',
    phoneNumber: null,
    status: 'Active',
    roles: [APP_ROLES.superAdmin],
    createdAtUtc: '2026-01-01T00:00:00Z',
    updatedAtUtc: null,
    lastLoginAtUtc: null
  };
  const admin: CurrentUser = {
    ...superAdmin,
    id: 9,
    username: 'admin',
    email: 'admin@example.com',
    firstName: 'Site',
    lastName: 'Admin',
    roles: [APP_ROLES.admin]
  };
  const standardUser: ManagedUser = {
    ...superAdmin,
    id: 2,
    username: 'jane.doe',
    email: 'jane@example.com',
    firstName: 'Jane',
    lastName: 'Doe',
    roles: [APP_ROLES.user]
  };
  const privilegedUser: ManagedUser = {
    ...standardUser,
    id: 3,
    username: 'other-admin',
    email: 'other-admin@example.com',
    roles: [APP_ROLES.admin]
  };

  beforeEach(() => {
    userService = jasmine.createSpyObj<UserManagementService>(
      'UserManagementService',
      [
        'getUsers',
        'getUser',
        'createUser',
        'updateUser',
        'updateUserRoles',
        'updateUserStatus'
      ]
    );
    userService.getUsers.and.returnValue(of(createPage()));
    userService.updateUserStatus.and.callFake((_id, status) =>
      of({ ...standardUser, status })
    );

    dialog = jasmine.createSpyObj<MatDialog>('MatDialog', ['open']);
    dialog.open.and.returnValue({
      afterClosed: () => of(true)
    } as unknown as MatDialogRef<unknown, unknown>);

    snackBar = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);
  });

  it('loads and renders users on init', () => {
    createComponent(superAdmin);

    expect(userService.getUsers).toHaveBeenCalledTimes(1);
    expect(userService.getUsers).toHaveBeenCalledWith({
      page: 1,
      pageSize: 10,
      search: '',
      status: undefined,
      role: undefined
    });
    expect(component.users()).toEqual([standardUser]);
    expect(component.totalCount()).toBe(1);

    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('Jane Doe');
    expect(text).toContain('jane.doe');
    expect(text).toContain('Active');
  });

  it('reloads with search, status, and role filters', fakeAsync(() => {
    createComponent(superAdmin);

    component.searchControl.setValue('jane');
    tick(300);

    expect(userService.getUsers.calls.mostRecent().args[0].search).toBe('jane');

    component.statusControl.setValue('Inactive');

    expect(userService.getUsers.calls.mostRecent().args[0].status).toBe('Inactive');

    component.roleControl.setValue(APP_ROLES.admin);

    expect(userService.getUsers.calls.mostRecent().args[0].role).toBe(APP_ROLES.admin);

    component.resetFilters();

    const query = userService.getUsers.calls.mostRecent().args[0];

    expect(query.search).toBe('');
    expect(query.status).toBeUndefined();
    expect(query.role).toBeUndefined();
    expect(query.page).toBe(1);
  }));

  it('deactivates a standard user after confirmation', () => {
    createComponent(superAdmin);

    component.openStatusDialog(standardUser);

    expect(dialog.open).toHaveBeenCalled();
    expect(userService.updateUserStatus).toHaveBeenCalledTimes(1);
    expect(userService.updateUserStatus).toHaveBeenCalledWith(
      standardUser.id,
      'Inactive'
    );
    expect(snackBar.open).toHaveBeenCalled();
    expect(userService.getUsers).toHaveBeenCalledTimes(2);
  });

  it('limits admin actions to standard users', () => {
    createComponent(admin);

    expect(component.canEditUser(standardUser)).toBeTrue();
    expect(component.canChangeStatus(standardUser)).toBeTrue();
    expect(component.canEditUser(privilegedUser)).toBeFalse();
    expect(component.canChangeStatus(privilegedUser)).toBeFalse();
    expect(component.canManageRoles()).toBeFalse();
    expect(component.canChangeStatus(superAdmin)).toBeFalse();
  });

  it('allows a Super Admin to manage privileged users but not their own status', () => {
    createComponent(superAdmin);

    expect(component.canEditUser(privilegedUser)).toBeTrue();
    expect(component.canChangeStatus(privilegedUser)).toBeTrue();
    expect(component.canManageRoles()).toBeTrue();
    expect(component.canChangeStatus(superAdmin)).toBeFalse();
  });

  function createComponent(currentUser: CurrentUser): void {
    const authStub = {
      currentUser: signal(currentUser),
      isAdmin: signal(true),
      hasRole: (role: string) => currentUser.roles.includes(role)
    } as unknown as AuthService;

    TestBed.configureTestingModule({
      imports: [UserListComponent],
      providers: [
        provideNoopAnimations(),
        { provide: AuthService, useValue: authStub },
        { provide: UserManagementService, useValue: userService },
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar }
      ]
    });

    fixture = TestBed.createComponent(UserListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function createPage(): PagedResult<ManagedUser> {
    return {
      items: [standardUser],
      page: 1,
      pageSize: 10,
      totalCount: 1,
      totalPages: 1
    };
  }
});
