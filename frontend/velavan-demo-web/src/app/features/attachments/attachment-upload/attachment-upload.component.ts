import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  input,
  output,
  signal,
  viewChild
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { formatFileSize } from '../../../core/attachments/attachment-file.util';

@Component({
  selector: 'app-attachment-upload',
  imports: [MatButtonModule, MatIconModule, MatProgressBarModule],
  templateUrl: './attachment-upload.component.html',
  styleUrl: './attachment-upload.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AttachmentUploadComponent {
  private readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');

  readonly accept = input('.pdf,.jpg,.jpeg,.png,.webp');
  readonly allowedExtensions = input<readonly string[]>([]);
  readonly maxBytes = input(20 * 1024 * 1024);
  readonly uploading = input(false);
  readonly progress = input<number | null>(null);
  readonly disabled = input(false);
  readonly heading = input('Upload attachment');

  readonly fileSelected = output<File>();

  readonly validationError = signal('');
  readonly dragActive = signal(false);

  readonly isDisabled = computed(() => this.disabled() || this.uploading());
  readonly progressMode = computed(() =>
    this.progress() === null ? 'indeterminate' : 'determinate'
  );
  readonly sizeHint = computed(() => formatFileSize(this.maxBytes()));
  readonly extensionHint = computed(() => {
    const extensions = this.allowedExtensions();

    return extensions.length > 0
      ? extensions.map(extension => extension.toUpperCase()).join(', ')
      : this.accept();
  });

  openFilePicker(): void {
    if (this.isDisabled()) {
      return;
    }

    this.validationError.set('');
    this.fileInput()?.nativeElement.click();
  }

  onFileInput(event: Event): void {
    const inputElement = event.target as HTMLInputElement;
    const file = inputElement.files?.[0];

    inputElement.value = '';

    if (file) {
      this.selectFile(file);
    }
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();

    if (!this.isDisabled()) {
      this.dragActive.set(true);
    }
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.dragActive.set(false);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragActive.set(false);

    if (this.isDisabled()) {
      return;
    }

    const file = event.dataTransfer?.files?.[0];

    if (file) {
      this.selectFile(file);
    }
  }

  private selectFile(file: File): void {
    const extension = this.getExtension(file.name);
    const allowedExtensions = this.allowedExtensions();

    if (file.size <= 0) {
      this.validationError.set('The selected file is empty.');
      return;
    }

    if (file.size > this.maxBytes()) {
      this.validationError.set(
        `The selected file exceeds the ${this.sizeHint()} limit.`
      );
      return;
    }

    if (allowedExtensions.length > 0 && !allowedExtensions.includes(extension)) {
      this.validationError.set(
        `Choose a file with one of these extensions: ${this.extensionHint()}.`
      );
      return;
    }

    this.validationError.set('');
    this.fileSelected.emit(file);
  }

  private getExtension(fileName: string): string {
    const extensionIndex = fileName.lastIndexOf('.');

    return extensionIndex >= 0
      ? fileName.slice(extensionIndex).toLowerCase()
      : '';
  }
}
