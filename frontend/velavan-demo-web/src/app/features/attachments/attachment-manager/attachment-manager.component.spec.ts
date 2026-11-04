import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of } from 'rxjs';
import { AttachmentResponse } from '../../../core/attachments/attachment.models';
import { AttachmentService } from '../../../core/attachments/attachment.service';
import { AttachmentManagerComponent } from './attachment-manager.component';

describe('AttachmentManagerComponent', () => {
  let fixture: ComponentFixture<AttachmentManagerComponent>;
  let component: AttachmentManagerComponent;
  let attachmentService: jasmine.SpyObj<AttachmentService>;
  let dialog: jasmine.SpyObj<MatDialog>;
  let snackBar: jasmine.SpyObj<MatSnackBar>;

  const attachment: AttachmentResponse = {
    id: 3,
    kind: 'CustomerDocument',
    originalFileName: 'terms.pdf',
    contentType: 'application/pdf',
    fileExtension: '.pdf',
    sizeBytes: 4096,
    width: null,
    height: null,
    createdAtUtc: '2026-01-01T00:00:00Z'
  };

  beforeEach(async () => {
    attachmentService = jasmine.createSpyObj<AttachmentService>(
      'AttachmentService',
      [
        'getOwnerAttachments',
        'uploadOwnerAttachment',
        'deleteOwnerAttachment',
        'getAttachmentContent'
      ]
    );
    attachmentService.getOwnerAttachments.and.returnValue(of([attachment]));
    attachmentService.uploadOwnerAttachment.and.returnValue(of(attachment));
    attachmentService.deleteOwnerAttachment.and.returnValue(of(void 0));
    attachmentService.getAttachmentContent.and.returnValue(
      of({
        blob: new Blob(['pdf'], { type: 'application/pdf' }),
        fileName: attachment.originalFileName,
        contentType: attachment.contentType,
        etag: null
      })
    );
    spyOn(URL, 'createObjectURL').and.returnValue('blob:download');
    spyOn(URL, 'revokeObjectURL');

    dialog = jasmine.createSpyObj<MatDialog>('MatDialog', ['open']);
    dialog.open.and.returnValue({
      afterClosed: () => of(true)
    } as unknown as MatDialogRef<unknown, unknown>);
    snackBar = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);

    await TestBed.configureTestingModule({
      imports: [AttachmentManagerComponent],
      providers: [
        provideNoopAnimations(),
        { provide: AttachmentService, useValue: attachmentService },
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(AttachmentManagerComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('ownerType', 'customer');
    fixture.componentRef.setInput('ownerId', 7);
    fixture.detectChanges();
  });

  it('loads customer documents from the owner route', () => {
    expect(attachmentService.getOwnerAttachments).toHaveBeenCalledWith(
      'customer',
      7
    );
    expect(component.attachments()).toEqual([attachment]);
  });

  it('uploads a customer document', () => {
    const file = new File(['pdf'], 'terms.pdf', { type: 'application/pdf' });

    component.upload(file);

    expect(attachmentService.uploadOwnerAttachment).toHaveBeenCalledWith(
      'customer',
      7,
      file,
      jasmine.any(Function)
    );
    expect(attachmentService.getOwnerAttachments).toHaveBeenCalledTimes(2);
  });

  it('deletes an attachment after confirmation', () => {
    component.confirmDelete(attachment);

    expect(dialog.open).toHaveBeenCalled();
    expect(attachmentService.deleteOwnerAttachment).toHaveBeenCalledWith(
      'customer',
      7,
      3
    );
  });

  it('downloads attachment content as a file', () => {
    component.download(attachment);

    expect(attachmentService.getAttachmentContent).toHaveBeenCalledWith(3, true);
    expect(URL.createObjectURL).toHaveBeenCalled();
  });
});
