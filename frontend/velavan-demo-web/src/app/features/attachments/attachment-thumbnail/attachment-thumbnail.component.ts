import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Subscription } from 'rxjs';
import { AttachmentService } from '../../../core/attachments/attachment.service';

@Component({
  selector: 'app-attachment-thumbnail',
  imports: [MatIconModule, MatProgressSpinnerModule],
  template: `
    @if (objectUrl(); as url) {
      <img [src]="url" [alt]="alt()" loading="lazy" />
    } @else if (loading()) {
      <mat-spinner diameter="24" aria-label="Loading image" />
    } @else {
      <mat-icon aria-hidden="true">broken_image</mat-icon>
    }
  `,
  styles: `
    :host {
      display: grid;
      width: 100%;
      height: 100%;
      min-height: 96px;
      place-items: center;
      overflow: hidden;
      background: #f1f5f9;
      color: var(--app-muted);
    }

    img {
      width: 100%;
      height: 100%;
      object-fit: cover;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AttachmentThumbnailComponent {
  private readonly attachmentService = inject(AttachmentService);
  private readonly destroyRef = inject(DestroyRef);
  private loadSubscription?: Subscription;

  readonly attachmentId = input.required<number>();
  readonly alt = input('Attachment preview');

  readonly objectUrl = signal<string | null>(null);
  readonly loading = signal(true);

  constructor() {
    effect(() => {
      const attachmentId = this.attachmentId();
      this.load(attachmentId);
    });

    this.destroyRef.onDestroy(() => {
      this.loadSubscription?.unsubscribe();
      this.revokeObjectUrl();
    });
  }

  private load(attachmentId: number): void {
    this.loadSubscription?.unsubscribe();
    this.revokeObjectUrl();
    this.loading.set(true);

    this.loadSubscription = this.attachmentService
      .getAttachmentContent(attachmentId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: content => {
          this.objectUrl.set(URL.createObjectURL(content.blob));
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
        }
      });
  }

  private revokeObjectUrl(): void {
    const url = this.objectUrl();

    if (url) {
      URL.revokeObjectURL(url);
      this.objectUrl.set(null);
    }
  }
}
