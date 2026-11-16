import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import {
  MAT_DIALOG_DATA,
  MatDialogModule,
  MatDialogRef
} from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import {
  AttachmentContent,
  AttachmentPreviewData
} from '../../../core/attachments/attachment.models';
import { AttachmentService } from '../../../core/attachments/attachment.service';
import { saveBlob } from '../../../core/attachments/attachment-file.util';
import {
  getApiErrorMessage,
  getApiErrorMessageAsync
} from '../../../core/http/api-problem.util';

@Component({
  selector: 'app-attachment-preview-dialog',
  imports: [
    MatButtonModule,
    MatDialogModule,
    MatIconModule,
    MatProgressSpinnerModule
  ],
  templateUrl: './attachment-preview-dialog.component.html',
  styleUrl: './attachment-preview-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AttachmentPreviewDialogComponent {
  readonly data = inject<AttachmentPreviewData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject(MatDialogRef<AttachmentPreviewDialogComponent>);
  private readonly attachmentService = inject(AttachmentService);
  private readonly destroyRef = inject(DestroyRef);

  readonly content = signal<AttachmentContent | null>(null);
  readonly imageUrl = signal<string | null>(null);
  readonly loading = signal(true);
  readonly error = signal('');
  readonly isImage = computed(() =>
    this.content()?.contentType.toLowerCase().startsWith('image/') ?? false
  );

  constructor() {
    this.attachmentService
      .getAttachmentContent(this.data.attachmentId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: content => {
          this.content.set(content);

          if (content.contentType.toLowerCase().startsWith('image/')) {
            this.revokeImageUrl();
            this.imageUrl.set(URL.createObjectURL(content.blob));
          }

          this.loading.set(false);
        },
        error: (error: unknown) => {
          this.revokeImageUrl();
          this.content.set(null);
          this.loading.set(false);

          if (error instanceof HttpErrorResponse && error.error instanceof Blob) {
            void getApiErrorMessageAsync(
              error,
              'The attachment could not be loaded.'
            ).then(message => this.error.set(message));
          } else {
            this.error.set(
              getApiErrorMessage(error, 'The attachment could not be loaded.')
            );
          }
        }
      });

    this.destroyRef.onDestroy(() => this.revokeImageUrl());
  }

  download(): void {
    const content = this.content();

    if (content) {
      saveBlob(content.blob, content.fileName || this.data.fileName);
    }
  }

  close(): void {
    this.dialogRef.close();
  }

  private revokeImageUrl(): void {
    const url = this.imageUrl();

    if (url) {
      URL.revokeObjectURL(url);
      this.imageUrl.set(null);
    }
  }
}
