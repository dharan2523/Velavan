import { LOGIN_ROUTE } from './auth.constants';

export function getSafeReturnUrl(value: string | null | undefined, fallback = '/'): string {
  if (!value) {
    return fallback;
  }

  const candidate = value.trim();

  if (!candidate.startsWith('/')) {
    return fallback;
  }

  if (candidate.startsWith('//') || candidate.includes('\\')) {
    return fallback;
  }

  if (candidate.startsWith(LOGIN_ROUTE)) {
    return fallback;
  }

  return candidate;
}
