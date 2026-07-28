import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';

@Injectable({
  providedIn: 'root',
})
export class ImageEnhancerService {
  private readonly endpoint = 'http://localhost:5181/api/image/enhance';

  constructor(private readonly http: HttpClient) {}

  async enhance(file: File, profile: string, intensity: number): Promise<void> {
    const payload = {
      profile,
      intensity,
    };

    try {
      const response = await firstValueFrom(
        this.http.post<{ success: boolean }>(this.endpoint, payload),
      );

      if (!response?.success) {
        throw new Error('O backend retornou falha ao melhorar a imagem.');
      }
    } catch (error) {
      if (error instanceof HttpErrorResponse) {
        throw new Error('Falha de conexão com o backend. Tente novamente.');
      }
      throw error instanceof Error ? error : new Error('Erro desconhecido ao processar a melhoria.');
    }
  }
}
