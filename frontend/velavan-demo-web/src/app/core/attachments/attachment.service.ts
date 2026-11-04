import {
  HttpClient,
  HttpEventType,
  HttpParams,
  HttpResponse
} from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable, filter, map, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  AttachmentContent,
  AttachmentOwnerType,
  AttachmentResponse,
  ProductImageResponse,
  ReorderProductImagesRequest
} from './attachment.models';
import { parseAttachmentFileName } from './attachment-file.util';

type UploadProgressCallback = (percent: number) => void;

@Injectable({
  providedIn: 'root'
})
export class AttachmentService {
  private readonly http = inject(HttpClient);
  private readonly adminUrl = `${environment.apiBaseUrl}/admin`;
  private readonly attachmentsUrl = `${environment.apiBaseUrl}/attachments`;

  getProductImages(productId: number): Observable<ProductImageResponse[]> {
    return this.http.get<ProductImageResponse[]>(
      `${this.adminUrl}/products/${productId}/images`
    );
  }

  uploadProductImage(
    productId: number,
    file: File,
    onProgress?: UploadProgressCallback
  ): Observable<ProductImageResponse> {
    return this.upload<ProductImageResponse>(
      `${this.adminUrl}/products/${productId}/images`,
      file,
      onProgress
    );
  }

  setPrimaryProductImage(productId: number, attachmentId: number): Observable<void> {
    return this.http.patch<void>(
      `${this.adminUrl}/products/${productId}/images/${attachmentId}/primary`,
      null
    );
  }

  reorderProductImages(productId: number, attachmentIds: number[]): Observable<void> {
    const request: ReorderProductImagesRequest = { attachmentIds };

    return this.http.put<void>(
      `${this.adminUrl}/products/${productId}/images/order`,
      request
    );
  }

  deleteProductImage(productId: number, attachmentId: number): Observable<void> {
    return this.http.delete<void>(
      `${this.adminUrl}/products/${productId}/images/${attachmentId}`
    );
  }

  getOwnerAttachments(
    ownerType: AttachmentOwnerType,
    ownerId: number
  ): Observable<AttachmentResponse[]> {
    return this.http.get<AttachmentResponse[]>(
      `${this.adminUrl}/${this.getOwnerSegment(ownerType)}/${ownerId}/${
        ownerType === 'customer' ? 'documents' : 'attachments'
      }`
    );
  }

  uploadOwnerAttachment(
    ownerType: AttachmentOwnerType,
    ownerId: number,
    file: File,
    onProgress?: UploadProgressCallback
  ): Observable<AttachmentResponse> {
    return this.upload<AttachmentResponse>(
      `${this.adminUrl}/${this.getOwnerSegment(ownerType)}/${ownerId}/${
        ownerType === 'customer' ? 'documents' : 'attachments'
      }`,
      file,
      onProgress
    );
  }

  deleteOwnerAttachment(
    ownerType: AttachmentOwnerType,
    ownerId: number,
    attachmentId: number
  ): Observable<void> {
    return this.http.delete<void>(
      `${this.adminUrl}/${this.getOwnerSegment(ownerType)}/${ownerId}/${
        ownerType === 'customer' ? 'documents' : 'attachments'
      }/${attachmentId}`
    );
  }

  getAttachment(attachmentId: number): Observable<AttachmentResponse> {
    return this.http.get<AttachmentResponse>(
      `${this.attachmentsUrl}/${attachmentId}`
    );
  }

  getAttachmentContent(
    attachmentId: number,
    download = false
  ): Observable<AttachmentContent> {
    const params = download
      ? new HttpParams().set('download', 'true')
      : undefined;

    return this.http
      .get(`${this.attachmentsUrl}/${attachmentId}/content`, {
        observe: 'response',
        responseType: 'blob',
        params
      })
      .pipe(
        map(response => {
          const contentType = response.headers.get('Content-Type') ?? 'application/octet-stream';
          const fileName = parseAttachmentFileName(
            response.headers.get('Content-Disposition'),
            `attachment-${attachmentId}`
          );

          return {
            blob: response.body ?? new Blob(),
            fileName,
            contentType,
            etag: response.headers.get('ETag')
          };
        })
      );
  }

  private upload<T>(
    url: string,
    file: File,
    onProgress?: UploadProgressCallback
  ): Observable<T> {
    const formData = new FormData();
    formData.append('file', file, file.name);

    return this.http
      .post<T>(url, formData, {
        observe: 'events',
        reportProgress: true
      })
      .pipe(
        tap(event => {
          if (event.type === HttpEventType.UploadProgress && event.total) {
            onProgress?.(Math.round((event.loaded / event.total) * 100));
          }
        }),
        filter((event): event is HttpResponse<T> => event.type === HttpEventType.Response),
        map(event => event.body as T)
      );
  }

  private getOwnerSegment(ownerType: AttachmentOwnerType): 'customers' | 'orders' {
    return ownerType === 'customer' ? 'customers' : 'orders';
  }
}
