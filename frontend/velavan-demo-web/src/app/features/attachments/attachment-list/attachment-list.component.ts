import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AttachmentResponse } from '../../../core/attachments/attachment.models';
import { formatFileSize } from '../../../core/attachments/attachment-file.util';

@Component({
  selector: 'app-attachment-list',
  imports: [DatePipe, MatButtonModule, MatIconModule, MatTooltipModule],
  templateUrl: './attachment-list.component.html',
  styleUrl: './attachment-list.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AttachmentListComponent {
  readonly attachments = input<readonly AttachmentResponse[]>([]);
  readonly busyAttachmentIds = input<ReadonlySet<number>>(new Set());

  readonly preview = output<AttachmentResponse>();
  readonly download = output<AttachmentResponse>();
  readonly deleteAttachment = output<AttachmentResponse>();

  isImage(attachment: AttachmentResponse): boolean {
    return attachment.contentType.toLowerCase().startsWith('image/');
  }

  fileIcon(attachment: AttachmentResponse): string {
    if (this.isImage(attachment)) {
      return 'image';
    }

    return attachment.contentType === 'application/pdf'
      ? 'picture_as_pdf'
      : 'description';
  }

  isBusy(attachmentId: number): boolean {
    return this.busyAttachmentIds().has(attachmentId);
  }

  formatSize(bytes: number): string {
    return formatFileSize(bytes);
  }
}
