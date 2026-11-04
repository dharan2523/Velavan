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
import {
  AttachmentOwnerType,
  AttachmentResponse
} from '../../../core/attachments/attachment.models';
import { AttachmentService } from '../../../core/attachments/attachment.service';
import { saveBlob } from '../../../core/attachments/attachment-file.util';
import { getApiErrorMessage } from '../../../core/http/api-problem.util';
import {
  ConfirmDialogComponent,
  ConfirmDialogData
} from '../../users/confirm-dialog/confirm-dialog.component';
import { AttachmentListComponent } from '../attachment-list/attachment-list.component';
import { AttachmentPreviewDialogComponent } from '../attachment-preview-dialog/attachment-preview-dialog.component';
import { AttachmentUploadComponent } from '../attachment-upload/attachment-upload.component';

const DOCUMENT_EXTENSIONS = ['.pdf', '.jpg', '.jpeg', '.png', '.webp'] as const;
const DOCUMENT_MAX_BYTES = 20 * 1024 * 1024;

@Component({
  selector: 'app-attachment-manager',
  imports: [
    MatButtonModule,
    MatIconModule,
    MatProgressBarModule,
    AttachmentListComponent,
    AttachmentUploadComponent
  ],
  templateUrl: './attachment-manager.component.html',
  styleUrl: './attachment-manager.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AttachmentManagerComponent {
  private readonly attachmentService = inject(AttachmentService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);

  readonly ownerType = input.required<AttachmentOwnerType>();
  readonly ownerId = input.required<number>();
  readonly heading = input('Attachments');
  readonly maxAttachments = input(25);

  readonly attachments = signal<AttachmentResponse[]>([]);
  readonly loading = signal(false);
  readonly loadError = signal('');
  readonly uploading = signal(false);
  readonly uploadProgress = signal<number | null>(null);
  readonly busyAttachmentId = signal<number | null>(null);

  readonly documentExtensions = DOCUMENT_EXTENSIONS;
  readonly maxDocumentBytes = DOCUMENT_MAX_BYTES;
  readonly atLimit = computed(
    () => this.attachments().length >= this.maxAttachments()
  );

  constructor() {
    effect(() => {
      const ownerType = this.ownerType();
      const ownerId = this.ownerId();
      this.loadAttachments(ownerType, ownerId);
    });
  }

  loadAttachments(
    ownerType = this.ownerType(),
    ownerId = this.ownerId()
  ): void {
    this.loading.set(true);
    this.loadError.set('');

    this.attachmentService
      .getOwnerAttachments(ownerType, ownerId)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false))
      )
      .subscribe({
        next: attachments => this.attachments.set([...attachments]),
        error: (error: unknown) => {
          this.attachments.set([]);
          this.loadError.set(
            getApiErrorMessage(error, 'Attachments could not be loaded.')
          );
        }
      });
  }

  upload(file: File): void {
    if (this.atLimit()) {
      this.notify(
        `At most ${this.maxAttachments()} attachments can be uploaded.`
      );
      return;
    }

    this.uploading.set(true);
    this.uploadProgress.set(0);

    this.attachmentService
      .uploadOwnerAttachment(
        this.ownerType(),
        this.ownerId(),
        file,
        progress => this.uploadProgress.set(progress)
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
          this.notify('Attachment uploaded.');
          this.loadAttachments();
        },
        error: (error: unknown) => {
          this.notify(
            getApiErrorMessage(error, 'The attachment could not be uploaded.')
          );
        }
      });
  }

  confirmDelete(attachment: AttachmentResponse): void {
    const data: ConfirmDialogData = {
      title: 'Delete attachment',
      message: `Delete "${attachment.originalFileName}"? This permanently removes the file.`,
      confirmLabel: 'Delete attachment',
      destructive: true
    };

    this.dialog
      .open(ConfirmDialogComponent, { data, width: '440px' })
      .afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(confirmed => {
        if (confirmed) {
          this.deleteAttachment(attachment);
        }
      });
  }

  preview(attachment: AttachmentResponse): void {
    this.dialog.open(AttachmentPreviewDialogComponent, {
      data: {
        attachmentId: attachment.id,
        fileName: attachment.originalFileName,
        contentType: attachment.contentType
      },
      width: '760px',
      maxWidth: '95vw'
    });
  }

  download(attachment: AttachmentResponse): void {
    this.busyAttachmentId.set(attachment.id);

    this.attachmentService
      .getAttachmentContent(attachment.id, true)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busyAttachmentId.set(null))
      )
      .subscribe({
        next: content => saveBlob(content.blob, content.fileName),
        error: (error: unknown) => {
          this.notify(
            getApiErrorMessage(error, 'The attachment could not be downloaded.')
          );
        }
      });
  }

  private deleteAttachment(attachment: AttachmentResponse): void {
    this.busyAttachmentId.set(attachment.id);

    this.attachmentService
      .deleteOwnerAttachment(
        this.ownerType(),
        this.ownerId(),
        attachment.id
      )
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busyAttachmentId.set(null))
      )
      .subscribe({
        next: () => {
          this.notify('Attachment deleted.');
          this.loadAttachments();
        },
        error: (error: unknown) => {
          this.notify(
            getApiErrorMessage(error, 'The attachment could not be deleted.')
          );
        }
      });
  }

  private notify(message: string): void {
    this.snackBar.open(message, 'Dismiss', { duration: 4000 });
  }
}
