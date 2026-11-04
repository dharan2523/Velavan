import { HttpContextToken } from '@angular/common/http';

export const APP_ROLES = {
  superAdmin: 'Super Admin',
  admin: 'Admin',
  user: 'User'
} as const;

export type AppRole = (typeof APP_ROLES)[keyof typeof APP_ROLES];

export const PRIVILEGED_ROLES = [
  APP_ROLES.superAdmin,
  APP_ROLES.admin
] satisfies readonly AppRole[];

export const ROLE_PRIORITY = [
  APP_ROLES.superAdmin,
  APP_ROLES.admin,
  APP_ROLES.user
] satisfies readonly AppRole[];

export const AUTH_STORAGE_KEY = 'velavan-demo.auth.session';
export const AUTH_EXPIRY_SAFETY_MARGIN_MS = 30_000;
export const LOGIN_ROUTE = '/login';
export const FORBIDDEN_ROUTE = '/forbidden';
export const SKIP_AUTH_REDIRECT = new HttpContextToken<boolean>(() => false);
