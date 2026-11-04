import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  CreateUserRequest,
  ManagedUser,
  PagedResult,
  UpdateUserRequest,
  UpdateUserStatusRequest,
  UserListQuery,
  UserStatus
} from './user.models';

@Injectable({
  providedIn: 'root'
})
export class UserManagementService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/admin/users`;

  getUsers(query: UserListQuery): Observable<PagedResult<ManagedUser>> {
    let params = new HttpParams()
      .set('page', query.page.toString())
      .set('pageSize', query.pageSize.toString());

    const search = query.search?.trim();

    if (search) {
      params = params.set('search', search);
    }

    if (query.status) {
      params = params.set('status', query.status);
    }

    if (query.role) {
      params = params.set('role', query.role);
    }

    return this.http.get<PagedResult<ManagedUser>>(this.baseUrl, { params });
  }

  getUser(id: number): Observable<ManagedUser> {
    return this.http.get<ManagedUser>(`${this.baseUrl}/${id}`);
  }

  createUser(request: CreateUserRequest): Observable<ManagedUser> {
    return this.http.post<ManagedUser>(this.baseUrl, request);
  }

  updateUser(id: number, request: UpdateUserRequest): Observable<ManagedUser> {
    return this.http.put<ManagedUser>(`${this.baseUrl}/${id}`, request);
  }

  updateUserRoles(id: number, roles: string[]): Observable<ManagedUser> {
    return this.http.put<ManagedUser>(`${this.baseUrl}/${id}/roles`, { roles });
  }

  updateUserStatus(id: number, status: UserStatus): Observable<ManagedUser> {
    const request: UpdateUserStatusRequest = { status };

    return this.http.patch<ManagedUser>(`${this.baseUrl}/${id}/status`, request);
  }
}
