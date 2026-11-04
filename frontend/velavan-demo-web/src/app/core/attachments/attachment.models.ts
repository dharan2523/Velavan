export type AttachmentKind = 'ProductImage' | 'CustomerDocument' | 'OrderAttachment';

export type AttachmentOwnerType = 'customer' | 'order';

export interface AttachmentResponse {
  id: number;
  kind: AttachmentKind;
  originalFileName: string;
  contentType: string;
  fileExtension: string;
  sizeBytes: number;
  width: number | null;
  height: number | null;
  createdAtUtc: string;
}

export interface ProductImageResponse {
  id: number;
  originalFileName: string;
  contentType: string;
  fileExtension: string;
  sizeBytes: number;
  width: number | null;
  height: number | null;
  isPrimary: boolean;
  sortOrder: number;
  createdAtUtc: string;
}

export interface AttachmentContent {
  blob: Blob;
  fileName: string;
  contentType: string;
  etag: string | null;
}

export interface AttachmentPreviewData {
  attachmentId: number;
  fileName: string;
  contentType?: string;
}

export interface ReorderProductImagesRequest {
  attachmentIds: number[];
}
