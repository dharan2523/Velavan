export type UserStatus = 'Active' | 'Inactive' | 'Suspended';

export interface LoginCredentials {
  username: string;
  password: string;
}

export interface CurrentUser {
  id: number;
  username: string;
  email: string | null;
  firstName: string;
  lastName: string;
  phoneNumber: string | null;
  status: UserStatus;
  roles: string[];
  createdAtUtc: string;
  updatedAtUtc: string | null;
  lastLoginAtUtc: string | null;
}

export interface AuthResponse {
  accessToken: string;
  tokenType: string;
  expiresAtUtc: string;
  user: CurrentUser;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

export interface AuthSession {
  accessToken: string;
  tokenType: string;
  expiresAtUtc: string;
  user: CurrentUser;
}
