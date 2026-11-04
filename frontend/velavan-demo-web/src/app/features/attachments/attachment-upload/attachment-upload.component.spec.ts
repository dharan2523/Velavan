import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { AttachmentUploadComponent } from './attachment-upload.component';

describe('AttachmentUploadComponent', () => {
  let fixture: ComponentFixture<AttachmentUploadComponent>;
  let component: AttachmentUploadComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AttachmentUploadComponent],
      providers: [provideNoopAnimations()]
    }).compileComponents();

    fixture = TestBed.createComponent(AttachmentUploadComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('allowedExtensions', ['.png']);
    fixture.componentRef.setInput('maxBytes', 10);
    fixture.detectChanges();
  });

  it('emits an allowed file that is within the size limit', () => {
    const emitSpy = spyOn(component.fileSelected, 'emit');
    const file = new File(['abc'], 'photo.png', { type: 'image/png' });

    component.onFileInput({ target: { files: [file] } } as unknown as Event);

    expect(emitSpy).toHaveBeenCalledWith(file);
    expect(component.validationError()).toBe('');
  });

  it('rejects a disallowed extension', () => {
    const emitSpy = spyOn(component.fileSelected, 'emit');
    const file = new File(['abc'], 'notes.txt', { type: 'text/plain' });

    component.onFileInput({ target: { files: [file] } } as unknown as Event);

    expect(emitSpy).not.toHaveBeenCalled();
    expect(component.validationError()).toContain('extensions');
  });

  it('rejects a file above the size limit', () => {
    const emitSpy = spyOn(component.fileSelected, 'emit');
    const file = new File(['this file is too large'], 'photo.png', {
      type: 'image/png'
    });

    component.onFileInput({ target: { files: [file] } } as unknown as Event);

    expect(emitSpy).not.toHaveBeenCalled();
    expect(component.validationError()).toContain('10 B');
  });

  it('shows upload progress', () => {
    fixture.componentRef.setInput('uploading', true);
    fixture.componentRef.setInput('progress', 42);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('10 B');
    expect(
      fixture.nativeElement.querySelector('mat-progress-bar')
    ).not.toBeNull();
  });
});
