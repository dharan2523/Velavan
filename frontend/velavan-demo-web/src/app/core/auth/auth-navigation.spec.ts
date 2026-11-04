import { getSafeReturnUrl } from './auth-navigation';

describe('getSafeReturnUrl', () => {
  it('keeps an internal route', () => {
    expect(getSafeReturnUrl('/orders?page=2')).toBe('/orders?page=2');
  });

  it('falls back for a missing value', () => {
    expect(getSafeReturnUrl(null)).toBe('/');
  });

  it('falls back for absolute URLs', () => {
    expect(getSafeReturnUrl('https://example.com')).toBe('/');
  });

  it('falls back for protocol-relative URLs', () => {
    expect(getSafeReturnUrl('//example.com')).toBe('/');
  });

  it('falls back for backslash paths', () => {
    expect(getSafeReturnUrl('/\\example.com')).toBe('/');
  });

  it('falls back for the login route', () => {
    expect(getSafeReturnUrl('/login?returnUrl=/orders')).toBe('/');
  });
});
