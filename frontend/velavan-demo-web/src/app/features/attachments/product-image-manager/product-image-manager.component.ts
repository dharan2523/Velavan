import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  signal
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { finalize } from 'rxjs';
import { ProductImageResponse } from '../../../core/attachments/attachment.models';
import { AttachmentService } from '../../../core/attachments/attachment.service';
import {
  PRODUCT_IMAGE_EXTENSIONS,
  PRODUCT_IMAGE_MAX_BYTES,
  PRODUCT_IMAGE_MAX_COUNT
} from '../../../core/attachments/attachment-policy';
import { getApiErrorMessage } from '../../../core/http/api-problem.util';
import {
  ConfirmDialogComponent,
  ConfirmDialogData
} from '../../users/confirm-dialog/confirm-dialog.component';
import { AttachmentPreviewDialogComponent } from '../attachment-preview-dialog/attachment-preview-dialog.component';
import { AttachmentUploadComponent } from '../attachment-upload/attachment-upload.component';
import { ImageGalleryComponent } from '../image-gallery/image-gallery.component';

@Component({
  selector: 'app-product-image-manager',
  imports: [
    MatButtonModule,
    MatIconModule,
    MatProgressBarModule,
    AttachmentUploadComponent,
    ImageGalleryComponent
  ],
  templateUrl: './product-image-manager.component.html',
  styleUrl: './product-image-manager.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ProductImageManagerComponent {
  private readonly attachmentService = inject(AttachmentService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);

  readonly productId = input.required<number>();
  readonly heading = input('Product images');
  readonly maxImages = input(PRODUCT_IMAGE_MAX_COUNT);

  readonly images = signal<ProductImageResponse[]>([]);
  readonly loading = signal(false);
  readonly loadError = signal('');
  readonly uploading = signal(false);
  readonly uploadProgress = signal<number | null>(null);
  readonly busyAttachmentIds = signal<ReadonlySet<number>>(new Set());
  readonly reordering = signal(false);

  private loadSequence = 0;

  readonly imageExtensions = PRODUCT_IMAGE_EXTENSIONS;
  readonly maxImageBytes = PRODUCT_IMAGE_MAX_BYTES;
  readonly atLimit = computed(() => this.images().length >= this.maxImages());
  readonly isBusy = computed(
    () => this.busyAttachmentIds().size > 0 || this.reordering()
  );

  constructor() {
    effect(() => {
      const productId = this.productId();
      this.loadImages(productId);
    });
  }

  loadImages(productId = this.productId()): void {
    const sequence = ++this.loadSequence;
    this.loading.set(true);
    this.loadError.set('');

    this.attachmentService
      .getProductImages(productId)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          if (sequence === this.loadSequence) {
            this.loading.set(false);
          }
        })
      )
      .subscribe({
        next: images => {
          if (sequence === this.loadSequence) {
            this.images.set([...images]);
          }
        },
        error: (error: unknown) => {
          if (sequence !== this.loadSequence) {
            return;
          }

          this.images.set([]);
          this.loadError.set(
            getApiErrorMessage(error, 'Product images could not be loaded.')
          );
        }
      });
  }

  upload(file: File): void {
    if (this.uploading()) {
      return;
    }

    if (this.atLimit()) {
      this.notify(`A product can have at most ${this.maxImages()} images.`);
      return;
    }

    this.uploading.set(true);
    this.uploadProgress.set(0);

    this.attachmentService
      .uploadProductImage(this.productId(), file, progress =>
        this.uploadProgress.set(progress)
      )
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          this.uploading.set(false);
          this.uploadProgress.set(null);
        })
      )
      .subscribe({
        next: () => {
          this.notify('Image uploaded.');
          this.loadImages();
        },
        error: (error: unknown) => {
          this.notify(
            getApiErrorMessage(error, 'The image could not be uploaded.')
          );
        }
      });
  }

  setPrimary(image: ProductImageResponse): void {
    if (image.isPrimary || this.isBusy()) {
      return;
    }

    this.markBusy(image.id);

    this.attachmentService
      .setPrimaryProductImage(this.productId(), image.id)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.clearBusy(image.id))
      )
      .subscribe({
        next: () => {
          this.notify('Primary image updated.');
          this.loadImages();
        },
        error: (error: unknown) => {
          this.notify(
            getApiErrorMessage(error, 'The primary image could not be changed.')
          );
        }
      });
  }

  reorder(attachmentIds: number[]): void {
    if (this.isBusy()) {
      return;
    }

    this.reordering.set(true);

    this.attachmentService
      .reorderProductImages(this.productId(), attachmentIds)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.reordering.set(false))
      )
      .subscribe({
        next: () => this.loadImages(),
        error: (error: unknown) => {
          this.notify(
            getApiErrorMessage(error, 'The image order could not be updated.')
          );
        }
      });
  }

  confirmDelete(image: ProductImageResponse): void {
    const data: ConfirmDialogData = {
      title: 'Delete product image',
      message: `Delete "${image.originalFileName}"? This permanently removes the image.`,
      confirmLabel: 'Delete image',
      destructive: true
    };

    this.dialog
      .open(ConfirmDialogComponent, { data, width: '440px' })
      .afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(confirmed => {
        if (confirmed) {
          this.deleteImage(image);
        }
      });
  }

  preview(image: ProductImageResponse): void {
    this.dialog.open(AttachmentPreviewDialogComponent, {
      data: {
        attachmentId: image.id,
        fileName: image.originalFileName,
        contentType: image.contentType
      },
      width: '760px',
      maxWidth: '95vw'
    });
  }

  private deleteImage(image: ProductImageResponse): void {
    if (this.isBusy()) {
      return;
    }

    this.markBusy(image.id);

    this.attachmentService
      .deleteProductImage(this.productId(), image.id)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.clearBusy(image.id))
      )
      .subscribe({
        next: () => {
          this.notify('Image deleted.');
          this.loadImages();
        },
        error: (error: unknown) => {
          this.notify(
            getApiErrorMessage(error, 'The image could not be deleted.')
          );
        }
      });
  }

  private markBusy(attachmentId: number): void {
    this.busyAttachmentIds.update(current => {
      const next = new Set(current);
      next.add(attachmentId);
      return next;
    });
  }

  private clearBusy(attachmentId: number): void {
    this.busyAttachmentIds.update(current => {
      if (!current.has(attachmentId)) {
        return current;
      }

      const next = new Set(current);
      next.delete(attachmentId);
      return next;
    });
  }

  private notify(message: string): void {
    this.snackBar.open(message, 'Dismiss', { duration: 4000 });
  }
}
