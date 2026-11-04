import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { AttachmentResponse } from '../../../core/attachments/attachment.models';
import { AttachmentListComponent } from './attachment-list.component';

describe('AttachmentListComponent', () => {
  let fixture: ComponentFixture<AttachmentListComponent>;
  let component: AttachmentListComponent;

  const image: AttachmentResponse = {
    id: 1,
    kind: 'CustomerDocument',
    originalFileName: 'photo.jpg',
    contentType: 'image/jpeg',
    fileExtension: '.jpg',
    sizeBytes: 2048,
    width: 800,
    height: 600,
    createdAtUtc: '2026-01-01T00:00:00Z'
  };
  const pdf: AttachmentResponse = {
    ...image,
    id: 2,
    originalFileName: 'terms.pdf',
    contentType: 'application/pdf',
    fileExtension: '.pdf',
    width: null,
    height: null
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AttachmentListComponent],
      providers: [provideNoopAnimations()]
    }).compileComponents();

    fixture = TestBed.createComponent(AttachmentListComponent);
    component = fixture.componentInstance;
  });

  it('renders attachment metadata and only previews images', () => {
    fixture.componentRef.setInput('attachments', [image, pdf]);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('photo.jpg');
    expect(text).toContain('terms.pdf');
    expect(text).toContain('2.0 KB');
    expect(
      fixture.nativeElement.querySelectorAll(
        'button[aria-label="Preview attachment"]'
      ).length
    ).toBe(1);
    expect(
      fixture.nativeElement.querySelectorAll(
        'button[aria-label="Download attachment"]'
      ).length
    ).toBe(2);
  });

  it('emits download and delete actions', () => {
    const downloadSpy = spyOn(component.download, 'emit');
    const deleteSpy = spyOn(component.deleteAttachment, 'emit');

    fixture.componentRef.setInput('attachments', [pdf]);
    fixture.detectChanges();

    const downloadButton = fixture.nativeElement.querySelector(
      'button[aria-label="Download attachment"]'
    ) as HTMLButtonElement;
    const deleteButton = fixture.nativeElement.querySelector(
      'button[aria-label="Delete attachment"]'
    ) as HTMLButtonElement;

    downloadButton.click();
    deleteButton.click();

    expect(downloadSpy).toHaveBeenCalledWith(pdf);
    expect(deleteSpy).toHaveBeenCalledWith(pdf);
  });

  it('renders an empty state', () => {
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      'No attachments have been uploaded yet.'
    );
  });
});
