import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of } from 'rxjs';
import { ProductImageResponse } from '../../../core/attachments/attachment.models';
import { AttachmentService } from '../../../core/attachments/attachment.service';
import { ProductImageManagerComponent } from './product-image-manager.component';

describe('ProductImageManagerComponent', () => {
  let fixture: ComponentFixture<ProductImageManagerComponent>;
  let component: ProductImageManagerComponent;
  let attachmentService: jasmine.SpyObj<AttachmentService>;
  let dialog: jasmine.SpyObj<MatDialog>;
  let snackBar: jasmine.SpyObj<MatSnackBar>;

  const image: ProductImageResponse = {
    id: 1,
    originalFileName: 'front.png',
    contentType: 'image/png',
    fileExtension: '.png',
    sizeBytes: 2048,
    width: 800,
    height: 600,
    isPrimary: true,
    sortOrder: 0,
    createdAtUtc: '2026-01-01T00:00:00Z'
  };

  beforeEach(async () => {
    attachmentService = jasmine.createSpyObj<AttachmentService>(
      'AttachmentService',
      [
        'getProductImages',
        'uploadProductImage',
        'setPrimaryProductImage',
        'reorderProductImages',
        'deleteProductImage',
        'getAttachmentContent'
      ]
    );
    attachmentService.getProductImages.and.returnValue(of([image]));
    attachmentService.uploadProductImage.and.returnValue(of(image));
    attachmentService.setPrimaryProductImage.and.returnValue(of(void 0));
    attachmentService.reorderProductImages.and.returnValue(of(void 0));
    attachmentService.deleteProductImage.and.returnValue(of(void 0));
    attachmentService.getAttachmentContent.and.returnValue(
      of({
        blob: new Blob(['image'], { type: 'image/png' }),
        fileName: image.originalFileName,
        contentType: image.contentType,
        etag: null
      })
    );
    spyOn(URL, 'createObjectURL').and.returnValue('blob:manager');
    spyOn(URL, 'revokeObjectURL');

    dialog = jasmine.createSpyObj<MatDialog>('MatDialog', ['open']);
    dialog.open.and.returnValue({
      afterClosed: () => of(true)
    } as unknown as MatDialogRef<unknown, unknown>);
    snackBar = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);

    await TestBed.configureTestingModule({
      imports: [ProductImageManagerComponent],
      providers: [
        provideNoopAnimations(),
        { provide: AttachmentService, useValue: attachmentService },
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(ProductImageManagerComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('productId', 4);
    fixture.detectChanges();
  });

  it('loads images for the product', () => {
    expect(attachmentService.getProductImages).toHaveBeenCalledWith(4);
    expect(component.images()).toEqual([image]);
  });

  it('uploads an image and reloads the gallery', () => {
    const file = new File(['image'], 'new.png', { type: 'image/png' });

    component.upload(file);

    expect(attachmentService.uploadProductImage).toHaveBeenCalledWith(
      4,
      file,
      jasmine.any(Function)
    );
    expect(attachmentService.getProductImages).toHaveBeenCalledTimes(2);
    expect(snackBar.open).toHaveBeenCalled();
  });

  it('sets a primary image', () => {
    const secondary = { ...image, id: 2, isPrimary: false, sortOrder: 1 };

    component.setPrimary(secondary);

    expect(attachmentService.setPrimaryProductImage).toHaveBeenCalledWith(4, 2);
    expect(attachmentService.getProductImages).toHaveBeenCalledTimes(2);
  });

  it('deletes an image after confirmation', () => {
    component.confirmDelete(image);

    expect(dialog.open).toHaveBeenCalled();
    expect(attachmentService.deleteProductImage).toHaveBeenCalledWith(4, 1);
  });

  it('prevents uploads when the image limit is reached', () => {
    fixture.componentRef.setInput('maxImages', 1);
    fixture.detectChanges();

    component.upload(new File(['image'], 'new.png', { type: 'image/png' }));

    expect(attachmentService.uploadProductImage).not.toHaveBeenCalled();
    expect(snackBar.open).toHaveBeenCalled();
  });
});
