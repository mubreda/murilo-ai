# Murilo AI - IA Local

## Objetivo

Esta pasta centraliza a estrutura inicial para integração futura de modelos locais de visão computacional no projeto Murilo AI.

## Estrutura das pastas

- `codeformer/` - arquivos e scripts relacionados ao modelo CodeFormer.
- `realesrgan/` - arquivos e scripts relacionados ao modelo RealESRGAN.
- `models/` - armazenamento futuro de pesos e checkpoints.
- `input/` - imagens de entrada para testes locais.
- `output/` - resultados gerados pelos modelos.
- `scripts/` - scripts de preparação, execução e automação.
- `docs/` - documentação técnica e notas de uso.

## Fluxo esperado de processamento

1. Receber uma imagem de entrada.
2. Selecionar o modelo adequado conforme o perfil da imagem.
3. Executar o processamento localmente.
4. Salvar o resultado em `output/`.
5. Integrar o resultado ao fluxo principal do projeto.

## Dependências previstas

- Python
- PyTorch
- torchvision
- opencv-python
- numpy
- Pillow
- einops
- tqdm
- albumentations

## Organização dos modelos

- `models/` será usado para armazenar checkpoints e pesos.
- Cada modelo pode ter sua própria subestrutura futura, se necessário.
- Os arquivos de configuração e scripts de execução ficarão em `scripts/` e `docs/`.
