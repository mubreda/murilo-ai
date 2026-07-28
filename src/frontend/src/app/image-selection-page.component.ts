import { CommonModule } from '@angular/common';
import { Component, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSliderModule } from '@angular/material/slider';
import { ImageEnhancerService } from './image-enhancer.service';
import { ImageComparisonComponent } from './image-comparison.component';

interface ImagePreview {
  url: string;
  name: string;
  size: string;
  width: number;
  height: number;
}

type ProfileOption = 'Natural' | 'Mini Camera' | 'Webcam' | 'Retrato' | 'Documento' | 'Foto Antiga';

@Component({
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatButtonModule,
    MatFormFieldModule,
    MatSelectModule,
    MatSliderModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    ImageComparisonComponent,
  ],
  template: `
    <main class="image-page">
      <section class="page-shell">
        <mat-card class="image-grid">
          <div class="image-panel">
            <div class="card-header">
              <h1>Pré-visualização da imagem</h1>
              <p>Faça upload da imagem e veja os detalhes antes de continuar.</p>
            </div>

            <div
              class="drop-zone"
              [class.active]="dragOver()"
              (click)="pickFile(fileInput)"
              (dragover)="onDragOver($event)"
              (dragleave)="onDragLeave()"
              (drop)="onDrop($event)"
            >
              <input
                #fileInput
                type="file"
                accept="image/*"
                hidden
                (change)="onFileSelect($event)"
              />

              <ng-container *ngIf="!selectedImage(); else imagePreview">
                <div class="drop-placeholder">
                  <span>Solte a imagem aqui</span>
                  <span>ou clique para selecionar</span>
                </div>
              </ng-container>

              <ng-template #imagePreview>
                <img [src]="selectedImage()?.url" alt="Preview da imagem" />
              </ng-template>
            </div>

            <div class="feedback" *ngIf="errorMessage()">{{ errorMessage() }}</div>

            <div class="image-info" *ngIf="selectedImage()">
              <p><strong>Nome:</strong> {{ selectedImage()?.name }}</p>
              <p><strong>Tamanho:</strong> {{ selectedImage()?.size }}</p>
              <p><strong>Resolução:</strong> {{ selectedImage()?.width }} × {{ selectedImage()?.height }}</p>
            </div>

            <div class="actions" *ngIf="selectedImage()">
              <button mat-flat-button color="primary" type="button" (click)="removeImage()">
                Remover imagem
              </button>
            </div>

            <app-image-comparison
              *ngIf="selectedImage()"
              [originalImage]="selectedImage()?.url"
              [processedImage]="selectedImage()?.url"
            >
            </app-image-comparison>
          </div>

          <aside class="control-panel">
            <div class="panel-header">
              <h2>Configurações</h2>
              <p>Defina o estilo e a intensidade antes de melhorar a imagem.</p>
            </div>

            <mat-form-field appearance="outline" class="full-width">
              <mat-label>Perfil</mat-label>
              <mat-select [value]="profile()" (selectionChange)="profile.set($event.value)">
                <mat-option *ngFor="let option of profileOptions" [value]="option">
                  {{ option }}
                </mat-option>
              </mat-select>
            </mat-form-field>

            <div class="slider-group">
              <div class="slider-label">
                <span>Intensidade</span>
                <strong>{{ intensity() }}%</strong>
              </div>
              <mat-slider min="0" max="100" step="1">
                <input
                  matSliderThumb
                  [value]="intensity()"
                  (valueChange)="intensity.set($event)"
                />
              </mat-slider>
            </div>

            <button
              mat-flat-button
              color="primary"
              class="action-button"
              type="button"
              (click)="improveImage()"
              [disabled]="isProcessing() || !selectedImage()"
            >
              Melhorar imagem
              <mat-progress-spinner
                *ngIf="isProcessing()"
                diameter="18"
                mode="indeterminate"
                [strokeWidth]="3"
              ></mat-progress-spinner>
            </button>

            <div class="info-card" *ngIf="infoMessage()">
              <mat-icon>info</mat-icon>
              <span>{{ infoMessage() }}</span>
            </div>

            <app-image-comparison
              *ngIf="selectedImage()"
              [originalImage]="selectedImage()?.url ?? null"
              [processedImage]="selectedImage()?.url ?? null"
            ></app-image-comparison>
          </aside>
        </mat-card>
      </section>
    </main>
  `,
  styles: [`
    .image-page {
      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 24px;
      background: linear-gradient(180deg, #f4f7fb 0%, #ffffff 100%);
    }

    .page-shell {
      width: 100%;
      max-width: 1200px;
    }

    .image-grid {
      display: grid;
      grid-template-columns: 2fr 1fr;
      gap: 24px;
      padding: 24px;
      border-radius: 24px;
      box-shadow: 0 28px 80px rgba(14, 30, 37, 0.12);
    }

    .image-panel,
    .control-panel {
      display: grid;
      gap: 20px;
    }

    .card-header h1,
    .panel-header h2 {
      margin: 0;
      font-size: 1.75rem;
    }

    .card-header p,
    .panel-header p {
      margin: 8px 0 0;
      color: #5f6368;
      line-height: 1.6;
    }

    .drop-zone {
      position: relative;
      min-height: 360px;
      border: 2px dashed #c7cbd1;
      border-radius: 20px;
      background: #ffffff;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 24px;
      text-align: center;
      color: #5f6368;
      transition: border-color 0.2s ease, background-color 0.2s ease;
      cursor: pointer;
    }

    .drop-zone.active {
      border-color: #3f51b5;
      background: #eef3ff;
    }

    .drop-placeholder {
      display: grid;
      gap: 10px;
      font-size: 1rem;
      color: #424242;
    }

    .drop-zone img {
      max-width: 100%;
      max-height: 100%;
      border-radius: 18px;
      object-fit: contain;
    }

    .feedback {
      color: #b00020;
      font-size: 0.95rem;
    }

    .image-info {
      display: grid;
      gap: 10px;
      padding: 20px;
      border-radius: 18px;
      background: #f9fbff;
      color: #202124;
    }

    .image-info p {
      margin: 0;
      font-size: 0.95rem;
    }

    .actions {
      display: flex;
      justify-content: flex-end;
    }

    .full-width {
      width: 100%;
    }

    .slider-group {
      display: grid;
      gap: 12px;
    }

    .slider-label {
      display: flex;
      justify-content: space-between;
      align-items: center;
      font-size: 0.95rem;
      color: #202124;
    }

    .action-button {
      width: 100%;
      height: 48px;
    }

    .info-card {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 16px;
      border-radius: 16px;
      background: #eef3ff;
      color: #0f387d;
      font-size: 0.95rem;
    }

    .info-card mat-icon {
      font-size: 20px;
    }

    @media (max-width: 900px) {
      .image-grid {
        grid-template-columns: 1fr;
      }
    }
  `],
})
export class ImageSelectionPageComponent {
  constructor(private readonly imageEnhancer: ImageEnhancerService) {}

  protected readonly selectedImage = signal<ImagePreview | null>(null);
  protected readonly selectedFile = signal<File | null>(null);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly infoMessage = signal<string | null>(null);
  protected readonly isProcessing = signal(false);
  protected readonly dragOver = signal(false);
  protected readonly profile = signal<ProfileOption>('Natural');
  protected readonly intensity = signal(50);
  protected readonly profileOptions: ProfileOption[] = [
    'Natural',
    'Mini Camera',
    'Webcam',
    'Retrato',
    'Documento',
    'Foto Antiga',
  ];

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.dragOver.set(true);
  }

  protected onDragLeave(): void {
    this.dragOver.set(false);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.dragOver.set(false);

    const file = event.dataTransfer?.files?.item(0);
    this.handleFile(file);
  }

  protected onFileSelect(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.item(0);
    this.handleFile(file);
    input.value = '';
  }

  protected pickFile(input: HTMLInputElement): void {
    input.click();
  }

  protected removeImage(): void {
    this.clearPreview();
    this.errorMessage.set(null);
    this.infoMessage.set(null);
  }

  protected async improveImage(): Promise<void> {
    const file = this.selectedFile();

    if (!file) {
      return;
    }

    this.isProcessing.set(true);
    this.errorMessage.set(null);
    this.infoMessage.set(null);

    try {
      await this.imageEnhancer.enhance(file, this.profile(), this.intensity());
      this.infoMessage.set('Simulação de melhoria concluída com sucesso.');
    } catch {
      this.errorMessage.set('Falha ao processar a melhoria.');
    } finally {
      this.isProcessing.set(false);
    }
  }

  private handleFile(file: File | null | undefined): void {
    if (!file) {
      return;
    }

    if (!file.type.startsWith('image/')) {
      this.clearPreview();
      this.errorMessage.set('O arquivo selecionado não é uma imagem.');
      this.infoMessage.set(null);
      return;
    }

    this.errorMessage.set(null);
    this.infoMessage.set(null);
    this.loadPreview(file);
  }

  private loadPreview(file: File): void {
    this.clearPreview();

    const url = URL.createObjectURL(file);
    const image = new Image();

    image.onload = () => {
      this.selectedFile.set(file);
      this.selectedImage.set({
        url,
        name: file.name,
        size: this.formatFileSize(file.size),
        width: image.naturalWidth,
        height: image.naturalHeight,
      });
    };

    image.onerror = () => {
      URL.revokeObjectURL(url);
      this.errorMessage.set('Não foi possível carregar a imagem.');
    };

    image.src = url;
  }

  private clearPreview(): void {
    const current = this.selectedImage();

    if (current) {
      URL.revokeObjectURL(current.url);
    }

    this.selectedFile.set(null);
    this.selectedImage.set(null);
  }

  private formatFileSize(bytes: number): string {
    if (bytes < 1024) {
      return `${bytes} B`;
    }

    const kb = bytes / 1024;

    if (kb < 1024) {
      return `${kb.toFixed(1)} KB`;
    }

    return `${(kb / 1024).toFixed(1)} MB`;
  }
}
