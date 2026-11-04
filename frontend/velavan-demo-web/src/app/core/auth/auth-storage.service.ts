import { Injectable } from '@angular/core';
import { AUTH_STORAGE_KEY } from './auth.constants';
import { AuthSession, CurrentUser } from './auth.models';

@Injectable({
  providedIn: 'root'
})
export class AuthStorageService {
  private inMemorySession: AuthSession | null = null;

  read(): AuthSession | null {
    try {
      const raw = sessionStorage.getItem(AUTH_STORAGE_KEY);

      if (raw === null) {
        return this.inMemorySession;
      }

      const parsed: unknown = JSON.parse(raw);

      if (!isAuthSession(parsed)) {
        this.clear();
        return null;
      }

      this.inMemorySession = parsed;
      return parsed;
    } catch {
      return this.inMemorySession;
    }
  }

  write(session: AuthSession): void {
    this.inMemorySession = session;

    try {
      sessionStorage.setItem(AUTH_STORAGE_KEY, JSON.stringify(session));
    } catch {
      return;
    }
  }

  clear(): void {
    this.inMemorySession = null;

    try {
      sessionStorage.removeItem(AUTH_STORAGE_KEY);
    } catch {
      return;
    }
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function isAuthSession(value: unknown): value is AuthSession {
  if (!isRecord(value)) {
    return false;
  }

  return (
    typeof value['accessToken'] === 'string' &&
    value['accessToken'].length > 0 &&
    typeof value['tokenType'] === 'string' &&
    value['tokenType'].length > 0 &&
    typeof value['expiresAtUtc'] === 'string' &&
    isCurrentUser(value['user'])
  );
}

function isCurrentUser(value: unknown): value is CurrentUser {
  if (!isRecord(value)) {
    return false;
  }

  return (
    typeof value['id'] === 'number' &&
    typeof value['username'] === 'string' &&
    isNullableString(value['email']) &&
    typeof value['firstName'] === 'string' &&
    typeof value['lastName'] === 'string' &&
    isNullableString(value['phoneNumber']) &&
    typeof value['status'] === 'string' &&
    Array.isArray(value['roles']) &&
    value['roles'].every(role => typeof role === 'string') &&
    typeof value['createdAtUtc'] === 'string' &&
    isNullableString(value['updatedAtUtc']) &&
    isNullableString(value['lastLoginAtUtc'])
  );
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string';
}
