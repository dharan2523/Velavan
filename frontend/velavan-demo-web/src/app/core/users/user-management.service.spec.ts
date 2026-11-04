import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { APP_ROLES } from '../auth/auth.constants';
import { ManagedUser, PagedResult } from './user.models';
import { UserManagementService } from './user-management.service';

describe('UserManagementService', () => {
  let service: UserManagementService;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/admin/users`;
  const user: ManagedUser = {
    id: 5,
    username: 'jane',
    email: 'jane@example.com',
    firstName: 'Jane',
    lastName: 'Doe',
    phoneNumber: null,
    status: 'Active',
    roles: [APP_ROLES.user],
    createdAtUtc: '2026-01-01T00:00:00Z',
    updatedAtUtc: null,
    lastLoginAtUtc: null
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });

    service = TestBed.inject(UserManagementService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('gets users with pagination, search, status, and role filters', () => {
    const page: PagedResult<ManagedUser> = {
      items: [user],
      page: 2,
      pageSize: 20,
      totalCount: 21,
      totalPages: 2
    };
    let actual: PagedResult<ManagedUser> | undefined;

    service
      .getUsers({
        page: 2,
        pageSize: 20,
        search: '  jane ',
        status: 'Active',
        role: APP_ROLES.user
      })
      .subscribe(result => {
        actual = result;
      });

    const request = httpMock.expectOne(candidate => candidate.url === baseUrl);

    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('20');
    expect(request.request.params.get('search')).toBe('jane');
    expect(request.request.params.get('status')).toBe('Active');
    expect(request.request.params.get('role')).toBe(APP_ROLES.user);

    request.flush(page);

    expect(actual).toEqual(page);
  });

  it('omits optional filters when they are empty', () => {
    service.getUsers({ page: 1, pageSize: 10, search: '  ' }).subscribe();

    const request = httpMock.expectOne(candidate => candidate.url === baseUrl);

    expect(request.request.params.has('search')).toBeFalse();
    expect(request.request.params.has('status')).toBeFalse();
    expect(request.request.params.has('role')).toBeFalse();

    request.flush({
      items: [],
      page: 1,
      pageSize: 10,
      totalCount: 0,
      totalPages: 0
    });
  });

  it('creates a user with the selected role', () => {
    service
      .createUser({
        username: user.username,
        email: user.email,
        firstName: user.firstName,
        lastName: user.lastName,
        phoneNumber: null,
        password: 'TestPass!2345',
        roles: [APP_ROLES.user]
      })
      .subscribe();

    const request = httpMock.expectOne(baseUrl);

    expect(request.request.method).toBe('POST');
    expect(request.request.body.roles).toEqual([APP_ROLES.user]);

    request.flush(user);
  });

  it('updates profile, roles, and status through admin endpoints', () => {
    service
      .updateUser(user.id, {
        username: user.username,
        email: user.email,
        firstName: 'Janet',
        lastName: user.lastName,
        phoneNumber: '+91 90000 00000'
      })
      .subscribe();

    const updateRequest = httpMock.expectOne(`${baseUrl}/${user.id}`);

    expect(updateRequest.request.method).toBe('PUT');
    expect(updateRequest.request.body.firstName).toBe('Janet');
    updateRequest.flush(user);

    service.updateUserRoles(user.id, [APP_ROLES.admin]).subscribe();

    const rolesRequest = httpMock.expectOne(`${baseUrl}/${user.id}/roles`);

    expect(rolesRequest.request.method).toBe('PUT');
    expect(rolesRequest.request.body).toEqual({ roles: [APP_ROLES.admin] });
    rolesRequest.flush(user);

    service.updateUserStatus(user.id, 'Inactive').subscribe();

    const statusRequest = httpMock.expectOne(`${baseUrl}/${user.id}/status`);

    expect(statusRequest.request.method).toBe('PATCH');
    expect(statusRequest.request.body).toEqual({ status: 'Inactive' });
    statusRequest.flush(user);
  });
});
