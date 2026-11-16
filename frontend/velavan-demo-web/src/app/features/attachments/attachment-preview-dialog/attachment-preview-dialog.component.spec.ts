import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';
import { AttachmentContent } from '../../../core/attachments/attachment.models';
import { AttachmentService } from '../../../core/attachments/attachment.service';
import { AttachmentPreviewDialogComponent } from './attachment-preview-dialog.component';

describe('AttachmentPreviewDialogComponent', () => {
  let fixture: ComponentFixture<AttachmentPreviewDialogComponent>;
  let component: AttachmentPreviewDialogComponent;
  let attachmentService: jasmine.SpyObj<AttachmentService>;
  let dialogRef: jasmine.SpyObj<MatDialogRef<AttachmentPreviewDialogComponent>>;

  const imageContent: AttachmentContent = {
    blob: new Blob(['image'], { type: 'image/png' }),
    fileName: 'photo.png',
    contentType: 'image/png',
    etag: '"abc"'
  };

  const pdfContent: AttachmentContent = {
    blob: new Blob(['pdf'], { type: 'application/pdf' }),
    fileName: 'terms.pdf',
    contentType: 'application/pdf',
    etag: '"def"'
  };

  async function createComponent(): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [AttachmentPreviewDialogComponent],
      providers: [
        provideNoopAnimations(),
        { provide: AttachmentService, useValue: attachmentService },
        { provide: MatDialogRef, useValue: dialogRef },
        {
          provide: MAT_DIALOG_DATA,
          useValue: {
            attachmentId: 5,
            fileName: 'photo.png',
            contentType: 'image/png'
          }
        }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(AttachmentPreviewDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(() => {
    attachmentService = jasmine.createSpyObj<AttachmentService>(
      'AttachmentService',
      ['getAttachmentContent']
    );
    dialogRef = jasmine.createSpyObj<MatDialogRef<AttachmentPreviewDialogComponent>>(
      'MatDialogRef',
      ['close']
    );

    spyOn(URL, 'createObjectURL').and.returnValue('blob:preview');
    spyOn(URL, 'revokeObjectURL');
    spyOn(HTMLAnchorElement.prototype, 'click');
  });

  it('loads and displays a protected image preview', async () => {
    attachmentService.getAttachmentContent.and.returnValue(of(imageContent));

    await createComponent();

    expect(attachmentService.getAttachmentContent).toHaveBeenCalledWith(5);
    expect(fixture.nativeElement.querySelector('img').getAttribute('src')).toBe(
      'blob:preview'
    );

    fixture.destroy();

    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:preview');
  });

  it('shows an error when content cannot be loaded', async () => {
    attachmentService.getAttachmentContent.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 404,
            error: { detail: 'Attachment was not found.' }
          })
      )
    );

    await createComponent();

    expect(component.error()).toBe('Attachment was not found.');
    expect(fixture.nativeElement.textContent).toContain(
      'Attachment was not found.'
    );
  });

  it('does not create an image url for non-image content and downloads it', async () => {
    attachmentService.getAttachmentContent.and.returnValue(of(pdfContent));

    await createComponent();

    expect(URL.createObjectURL).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain(
      'This file type cannot be previewed here.'
    );

    component.download();

    expect(URL.createObjectURL).toHaveBeenCalled();
    expect(HTMLAnchorElement.prototype.click).toHaveBeenCalled();
  });

  it('closes the dialog', async () => {
    attachmentService.getAttachmentContent.and.returnValue(of(pdfContent));

    await createComponent();

    component.close();

    expect(dialogRef.close).toHaveBeenCalled();
  });
});
