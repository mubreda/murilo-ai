# ADR 002: Image Provider Strategy

## Contexto

Murilo AI é uma aplicação para melhorar fotografias utilizando IA. O objetivo não é criar modelos próprios, mas integrar provedores especializados. O usuário nunca deverá escolher um modelo de IA; ele escolhe apenas um perfil de imagem.

## Decisão

O primeiro provedor adotado será o Fal.ai.

O backend será responsável por decidir qual modelo utilizar conforme o perfil escolhido pelo usuário.

Perfis esperados:

- Natural
- Mini Câmera
- Webcam
- Retrato
- Documento
- Foto Antiga

Cada perfil poderá utilizar um ou mais modelos internamente.

Exemplos de mapeamentos de perfis para modelos:

- Mini Câmera
  - ESRGAN
  - CodeFormer
- Retrato
  - CodeFormer
- Documento
  - ESRGAN

Essa decisão deve permanecer transparente para o usuário.

## Arquitetura

A arquitetura prevista será:

- `Controller`
- `ImageEnhancementService`
- `ProfileResolver`
- `IImageProvider`
- `Fal.ai`

No futuro poderão existir outros providers:

- Replicate
- OpenAI
- Local Provider

Sem alterar a interface pública da aplicação.

## Consequências

### Vantagens

- Backend desacoplado dos modelos.
- Fácil troca de provider.
- Possibilidade de combinar múltiplos modelos.
- Interface simples para o usuário.
- Escalabilidade.

### Desvantagens

- Backend torna-se responsável pela estratégia.
- Necessidade de manter mapeamentos entre perfis e modelos.

## Revisão

Esta decisão poderá ser revisada caso novos provedores apresentem melhor custo-benefício ou qualidade.
