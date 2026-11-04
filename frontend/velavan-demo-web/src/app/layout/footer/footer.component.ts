import { Component, inject } from '@angular/core';
import { BrandingService } from '../../core/branding/branding.service';

@Component({
  selector: 'app-footer',
  templateUrl: './footer.component.html',
  styleUrl: './footer.component.scss'
})
export class FooterComponent {
  private readonly branding = inject(BrandingService);

  readonly displayName = this.branding.displayName;
}
