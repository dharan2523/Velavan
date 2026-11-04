import { AbstractControl, ValidationErrors } from '@angular/forms';

export const PASSWORD_MIN_LENGTH = 10;

export const PASSWORD_POLICY_DESCRIPTION =
  'Use at least 10 characters with an uppercase letter, lowercase letter, number, and special character.';

export function passwordPolicyValidator(
  control: AbstractControl
): ValidationErrors | null {
  const value = typeof control.value === 'string' ? control.value : '';

  if (!value) {
    return null;
  }

  const valid =
    value.length >= PASSWORD_MIN_LENGTH &&
    /[A-Z]/.test(value) &&
    /[a-z]/.test(value) &&
    /\d/.test(value) &&
    /[^A-Za-z0-9]/.test(value);

  return valid ? null : { passwordPolicy: true };
}
