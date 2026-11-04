import { TestBed } from '@angular/core/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { ChangePasswordDialogComponent } from './change-password-dialog.component';

describe('ChangePasswordDialogComponent', () => {
  let component: ChangePasswordDialogComponent;
  let auth: jasmine.SpyObj<AuthService>;
  let dialogRef: jasmine.SpyObj<MatDialogRef<ChangePasswordDialogComponent, boolean>>;

  beforeEach(() => {
    auth = jasmine.createSpyObj<AuthService>('AuthService', ['changePassword']);
    auth.changePassword.and.returnValue(of(void 0));

    dialogRef = jasmine.createSpyObj('MatDialogRef', ['close']);

    TestBed.configureTestingModule({
      imports: [ChangePasswordDialogComponent],
      providers: [
        provideNoopAnimations(),
        { provide: AuthService, useValue: auth },
        { provide: MatDialogRef, useValue: dialogRef }
      ]
    });

    const fixture = TestBed.createComponent(ChangePasswordDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('blocks submission when the new password is weak', () => {
    component.form.setValue({
      currentPassword: 'CurrentPass!2345',
      newPassword: 'weak',
      confirmPassword: 'weak'
    });

    component.submit();

    expect(component.form.controls.newPassword.hasError('passwordPolicy')).toBeTrue();
    expect(auth.changePassword).not.toHaveBeenCalled();
    expect(component.submitting()).toBeFalse();
  });

  it('blocks submission when the confirmation does not match', () => {
    component.form.setValue({
      currentPassword: 'CurrentPass!2345',
      newPassword: 'ChangedPass!2345',
      confirmPassword: 'DifferentPass!2345'
    });

    component.submit();

    expect(
      component.form.controls.confirmPassword.hasError('passwordMismatch')
    ).toBeTrue();
    expect(auth.changePassword).not.toHaveBeenCalled();
  });

  it('changes the password and closes on success', () => {
    component.form.setValue({
      currentPassword: 'CurrentPass!2345',
      newPassword: 'ChangedPass!2345',
      confirmPassword: 'ChangedPass!2345'
    });

    component.submit();

    expect(auth.changePassword).toHaveBeenCalledTimes(1);
    expect(auth.changePassword).toHaveBeenCalledWith({
      currentPassword: 'CurrentPass!2345',
      newPassword: 'ChangedPass!2345'
    });
    expect(component.submitting()).toBeFalse();
    expect(dialogRef.close).toHaveBeenCalledTimes(1);
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });
});
