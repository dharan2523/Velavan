import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ProductImageResponse } from '../../../core/attachments/attachment.models';
import { formatFileSize } from '../../../core/attachments/attachment-file.util';
import { AttachmentThumbnailComponent } from '../attachment-thumbnail/attachment-thumbnail.component';

@Component({
  selector: 'app-image-gallery',
  imports: [
    MatButtonModule,
    MatIconModule,
    MatTooltipModule,
    AttachmentThumbnailComponent
  ],
  templateUrl: './image-gallery.component.html',
  styleUrl: './image-gallery.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ImageGalleryComponent {
  readonly images = input<readonly ProductImageResponse[]>([]);
  readonly busy = input(false);

  readonly preview = output<ProductImageResponse>();
  readonly setPrimary = output<ProductImageResponse>();
  readonly deleteImage = output<ProductImageResponse>();
  readonly reorder = output<number[]>();

  move(index: number, direction: -1 | 1): void {
    const images = [...this.images()];
    const targetIndex = index + direction;

    if (this.busy() || targetIndex < 0 || targetIndex >= images.length) {
      return;
    }

    [images[index], images[targetIndex]] = [images[targetIndex], images[index]];
    this.reorder.emit(images.map(image => image.id));
  }

  formatSize(bytes: number): string {
    return formatFileSize(bytes);
  }
}
