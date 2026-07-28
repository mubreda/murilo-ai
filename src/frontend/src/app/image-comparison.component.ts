import { CommonModule } from '@angular/common';
import { Component, Input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';

@Component({
  standalone: true,
  imports: [CommonModule, MatButtonModule],
  selector: 'app-image-comparison',
  template: `
    <section class="comparison-shell">
      <div class="comparison-header">
        <div>
          <h3>Comparação de imagem</h3>
          <p>Confira o resultado mock em dois modos de visualização.</p>
        </div>

        <div class="comparison-controls">
          <button
            mat-stroked-button
            color="primary"
            [disabled]="mode() === 'side-by-side'"
            type="button"
            (click)="mode.set('side-by-side')"
          >
            Lado a lado
          </button>
          <button
            mat-stroked-button
            color="primary"
            [disabled]="mode() === 'slider'"
            type="button"
            (click)="mode.set('slider')"
          >
            Slider
          </button>
        </div>
      </div>

      <div class="comparison-empty" *ngIf="!originalImage || !processedImage">
        Selecione uma imagem para comparar.</div>

      <div class="comparison-content" *ngIf="originalImage && processedImage">
        <div class="side-by-side" *ngIf="mode() === 'side-by-side'">
          <div class="preview-card">
            <span>Original</span>
            <img [src]="originalImage" [alt]="alt" />
          </div>
          <div class="preview-card">
            <span>Melhorada</span>
            <img [src]="processedImage" [alt]="alt" />
          </div>
        </div>

        <div class="slider-comparison" *ngIf="mode() === 'slider'">
          <div class="slider-frame">
            <img [src]="originalImage" [alt]="alt" />
            <div class="processed-overlay" [style.width.%]="sliderValue()">
              <img [src]="processedImage" [alt]="alt" />
            </div>
          </div>

          <div class="range-row">
            <input
              type="range"
              min="0"
              max="100"
              [value]="sliderValue()"
              (input)="sliderValue.set($any($event.target).valueAsNumber)"
            />
            <span>{{ sliderValue() }}%</span>
          </div>
        </div>
      </div>
    </section>
  `,
  styles: [`
    .comparison-shell {
      display: grid;
      gap: 16px;
      padding: 20px;
      border-radius: 20px;
      background: #f3f5fc;
      border: 1px solid rgba(63, 81, 181, 0.12);
    }

    .comparison-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 16px;
      flex-wrap: wrap;
    }

    .comparison-header h3 {
      margin: 0;
      font-size: 1.1rem;
    }

    .comparison-header p {
      margin: 4px 0 0;
      color: #5f6368;
      font-size: 0.95rem;
    }

    .comparison-controls {
      display: flex;
      gap: 12px;
      flex-wrap: wrap;
    }

    .comparison-empty {
      padding: 24px;
      border-radius: 16px;
      background: #ffffff;
      color: #5f6368;
      text-align: center;
    }

    .comparison-content {
      display: grid;
      gap: 16px;
    }

    .side-by-side {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 16px;
    }

    .preview-card {
      display: grid;
      gap: 12px;
      padding: 16px;
      border-radius: 18px;
      background: white;
      border: 1px solid rgba(14, 30, 37, 0.08);
    }

    .preview-card span {
      font-weight: 600;
      color: #202124;
    }

    .preview-card img {
      width: 100%;
      height: 280px;
      object-fit: contain;
      border-radius: 12px;
      background: #f9fbff;
    }

    .slider-comparison {
      display: grid;
      gap: 16px;
    }

    .slider-frame {
      position: relative;
      overflow: hidden;
      border-radius: 20px;
      min-height: 320px;
      background: #ffffff;
      border: 1px solid rgba(14, 30, 37, 0.08);
    }

    .slider-frame img {
      width: 100%;
      height: 100%;
      object-fit: contain;
      display: block;
    }

    .processed-overlay {
      position: absolute;
      top: 0;
      left: 0;
      height: 100%;
      overflow: hidden;
      border-top-left-radius: 20px;
      border-bottom-left-radius: 20px;
      transition: width 0.2s ease;
    }

    .processed-overlay img {
      width: 100%;
      height: 100%;
      object-fit: contain;
      display: block;
    }

    .range-row {
      display: flex;
      align-items: center;
      gap: 14px;
    }

    .range-row input[type='range'] {
      width: 100%;
      appearance: none;
      height: 4px;
      border-radius: 999px;
      background: #d1d9ff;
      outline: none;
    }

    .range-row input[type='range']::-webkit-slider-thumb {
      appearance: none;
      width: 18px;
      height: 18px;
      border-radius: 50%;
      background: #3f51b5;
      cursor: pointer;
      border: none;
    }

    .range-row span {
      min-width: 42px;
      text-align: right;
      color: #202124;
      font-weight: 600;
    }

    @media (max-width: 900px) {
      .side-by-side {
        grid-template-columns: 1fr;
      }
    }
  `],
})
export class ImageComparisonComponent {
  @Input() originalImage: string | null | undefined = null;
  @Input() processedImage: string | null | undefined = null;
  @Input() alt = 'Imagem';

  protected readonly mode = signal<'side-by-side' | 'slider'>('side-by-side');
  protected readonly sliderValue = signal(50);
}
