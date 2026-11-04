import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { AttachmentService } from '../../../core/attachments/attachment.service';
import { AttachmentThumbnailComponent } from './attachment-thumbnail.component';

describe('AttachmentThumbnailComponent', () => {
  let fixture: ComponentFixture<AttachmentThumbnailComponent>;
  let attachmentService: jasmine.SpyObj<AttachmentService>;

  beforeEach(async () => {
    attachmentService = jasmine.createSpyObj<AttachmentService>(
      'AttachmentService',
      ['getAttachmentContent']
    );
    attachmentService.getAttachmentContent.and.returnValue(
      of({
        blob: new Blob(['image'], { type: 'image/png' }),
        fileName: 'photo.png',
        contentType: 'image/png',
        etag: null
      })
    );
    spyOn(URL, 'createObjectURL').and.returnValue('blob:thumbnail');
    spyOn(URL, 'revokeObjectURL');

    await TestBed.configureTestingModule({
      imports: [AttachmentThumbnailComponent],
      providers: [
        { provide: AttachmentService, useValue: attachmentService }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(AttachmentThumbnailComponent);
  });

  it('loads a protected image and revokes the object url on destroy', () => {
    fixture.componentRef.setInput('attachmentId', 3);
    fixture.detectChanges();

    expect(attachmentService.getAttachmentContent).toHaveBeenCalledWith(3);
    expect(fixture.nativeElement.querySelector('img').getAttribute('src')).toBe(
      'blob:thumbnail'
    );

    fixture.destroy();

    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:thumbnail');
  });
});
