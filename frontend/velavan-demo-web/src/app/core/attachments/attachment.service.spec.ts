import { HttpEventType, HttpHeaders } from '@angular/common/http';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { AttachmentService } from './attachment.service';
import { ProductImageResponse } from './attachment.models';

describe('AttachmentService', () => {
  let service: AttachmentService;
  let httpMock: HttpTestingController;

  const adminUrl = `${environment.apiBaseUrl}/admin`;
  const attachmentsUrl = `${environment.apiBaseUrl}/attachments`;
  const image: ProductImageResponse = {
    id: 12,
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

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });

    service = TestBed.inject(AttachmentService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('loads product images and updates primary, order, and deletion', () => {
    service.getProductImages(4).subscribe(images => {
      expect(images).toEqual([image]);
    });

    const listRequest = httpMock.expectOne(
      `${adminUrl}/products/4/images`
    );

    expect(listRequest.request.method).toBe('GET');
    listRequest.flush([image]);

    service.setPrimaryProductImage(4, image.id).subscribe();
    const primaryRequest = httpMock.expectOne(
      `${adminUrl}/products/4/images/${image.id}/primary`
    );

    expect(primaryRequest.request.method).toBe('PATCH');
    primaryRequest.flush(null);

    service.reorderProductImages(4, [12, 9]).subscribe();
    const orderRequest = httpMock.expectOne(
      `${adminUrl}/products/4/images/order`
    );

    expect(orderRequest.request.method).toBe('PUT');
    expect(orderRequest.request.body).toEqual({ attachmentIds: [12, 9] });
    orderRequest.flush(null);

    service.deleteProductImage(4, image.id).subscribe();
    const deleteRequest = httpMock.expectOne(
      `${adminUrl}/products/4/images/${image.id}`
    );

    expect(deleteRequest.request.method).toBe('DELETE');
    deleteRequest.flush(null);
  });

  it('uploads a product image and reports progress', () => {
    const file = new File(['image'], 'front.png', { type: 'image/png' });
    let progress = 0;
    let uploaded: ProductImageResponse | undefined;

    service.uploadProductImage(4, file, value => {
      progress = value;
    }).subscribe(result => {
      uploaded = result;
    });

    const request = httpMock.expectOne(`${adminUrl}/products/4/images`);

    expect(request.request.method).toBe('POST');
    expect(request.request.body instanceof FormData).toBeTrue();
    expect((request.request.body as FormData).get('file')).toBeInstanceOf(File);

    request.event({
      type: HttpEventType.UploadProgress,
      loaded: 30,
      total: 100
    });
    expect(progress).toBe(30);

    request.flush(image);
    expect(uploaded).toEqual(image);
  });

  it('uses customer and order owner routes', () => {
    service.getOwnerAttachments('customer', 7).subscribe();
    const customerRequest = httpMock.expectOne(
      `${adminUrl}/customers/7/documents`
    );

    expect(customerRequest.request.method).toBe('GET');
    customerRequest.flush([]);

    const file = new File(['pdf'], 'terms.pdf', { type: 'application/pdf' });

    service.uploadOwnerAttachment('order', 9, file).subscribe();
    const orderUpload = httpMock.expectOne(
      `${adminUrl}/orders/9/attachments`
    );

    expect(orderUpload.request.method).toBe('POST');
    orderUpload.flush({});

    service.deleteOwnerAttachment('customer', 7, 3).subscribe();
    const customerDelete = httpMock.expectOne(
      `${adminUrl}/customers/7/documents/3`
    );

    expect(customerDelete.request.method).toBe('DELETE');
    customerDelete.flush(null);
  });

  it('parses attachment content metadata from response headers', () => {
    let received:
      | { fileName: string; contentType: string; etag: string | null }
      | undefined;

    service.getAttachmentContent(5, true).subscribe(content => {
      received = {
        fileName: content.fileName,
        contentType: content.contentType,
        etag: content.etag
      };
    });

    const request = httpMock.expectOne(
      candidate =>
        candidate.url === `${attachmentsUrl}/5/content` &&
        candidate.params.get('download') === 'true'
    );
    const headers = new HttpHeaders({
      'Content-Type': 'application/pdf',
      'Content-Disposition': 'attachment; filename="terms.pdf"',
      ETag: '"abc123"'
    });

    request.flush(new Blob(['pdf']), { headers });

    expect(received?.fileName).toBe('terms.pdf');
    expect(received?.contentType).toBe('application/pdf');
    expect(received?.etag).toBe('"abc123"');
  });
});
