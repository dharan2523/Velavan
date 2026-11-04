import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of } from 'rxjs';
import { ProductImageResponse } from '../../../core/attachments/attachment.models';
import { AttachmentService } from '../../../core/attachments/attachment.service';
import { ImageGalleryComponent } from './image-gallery.component';

describe('ImageGalleryComponent', () => {
  let fixture: ComponentFixture<ImageGalleryComponent>;
  let component: ImageGalleryComponent;

  const primaryImage: ProductImageResponse = {
    id: 1,
    originalFileName: 'front.png',
    contentType: 'image/png',
    fileExtension: '.png',
    sizeBytes: 1024,
    width: 800,
    height: 600,
    isPrimary: true,
    sortOrder: 0,
    createdAtUtc: '2026-01-01T00:00:00Z'
  };
  const secondaryImage: ProductImageResponse = {
    ...primaryImage,
    id: 2,
    originalFileName: 'back.png',
    isPrimary: false,
    sortOrder: 1
  };

  beforeEach(async () => {
    const attachmentService = jasmine.createSpyObj<AttachmentService>(
      'AttachmentService',
      ['getAttachmentContent']
    );
    attachmentService.getAttachmentContent.and.returnValue(
      of({
        blob: new Blob(['image'], { type: 'image/png' }),
        fileName: 'front.png',
        contentType: 'image/png',
        etag: null
      })
    );
    spyOn(URL, 'createObjectURL').and.returnValue('blob:preview');
    spyOn(URL, 'revokeObjectURL');

    await TestBed.configureTestingModule({
      imports: [ImageGalleryComponent],
      providers: [
        provideNoopAnimations(),
        { provide: AttachmentService, useValue: attachmentService }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(ImageGalleryComponent);
    component = fixture.componentInstance;
  });

  it('renders an empty state when there are no images', () => {
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      'No product images have been uploaded yet.'
    );
  });

  it('emits the reordered attachment ids', () => {
    const reorderSpy = spyOn(component.reorder, 'emit');

    fixture.componentRef.setInput('images', [primaryImage, secondaryImage]);
    component.move(0, 1);

    expect(reorderSpy).toHaveBeenCalledWith([2, 1]);
  });

  it('marks the primary image and requests a new primary', () => {
    const setPrimarySpy = spyOn(component.setPrimary, 'emit');

    fixture.componentRef.setInput('images', [primaryImage, secondaryImage]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Primary');

    const buttons = fixture.nativeElement.querySelectorAll(
      'button[aria-label="Set as primary"]'
    ) as NodeListOf<HTMLButtonElement>;

    buttons[1].click();

    expect(setPrimarySpy).toHaveBeenCalledWith(secondaryImage);
  });
});
