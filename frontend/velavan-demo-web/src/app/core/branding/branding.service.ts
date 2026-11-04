import { computed, Injectable, signal } from '@angular/core';
import { DEFAULT_BRANDING } from './branding.config';
import { BrandingConfig } from './branding.model';

@Injectable({
  providedIn: 'root'
})
export class BrandingService {
  private readonly brandingState = signal<BrandingConfig>(DEFAULT_BRANDING);

  readonly branding = this.brandingState.asReadonly();
  readonly displayName = computed(() => this.brandingState().displayName);
  readonly shortName = computed(() => this.brandingState().shortName);
  readonly logoUrl = computed(() => this.brandingState().logoUrl);

  setBranding(branding: Partial<BrandingConfig>): void {
    this.brandingState.update(current => ({ ...current, ...branding }));
  }
}
