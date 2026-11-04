import type { CurrentUser, UserStatus } from '../auth/auth.models';

export type ManagedUser = CurrentUser;

export type { UserStatus };

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface UserListQuery {
  page: number;
  pageSize: number;
  search?: string;
  status?: UserStatus;
  role?: string;
}

export interface CreateUserRequest {
  username: string;
  email: string | null;
  firstName: string;
  lastName: string;
  phoneNumber: string | null;
  password: string;
  roles: string[];
}

export interface UpdateUserRequest {
  username: string;
  email: string | null;
  firstName: string;
  lastName: string;
  phoneNumber: string | null;
}

export interface UpdateUserRolesRequest {
  roles: string[];
}

export interface UpdateUserStatusRequest {
  status: UserStatus;
}
