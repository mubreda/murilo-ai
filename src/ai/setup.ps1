# TODO: Definir instalação do Python e ambientes virtuais.
# Cria um ambiente virtual local para isolar as dependências do projeto.
python -m venv .venv

# Ativa o ambiente virtual.
.\.venv\Scripts\Activate.ps1

# Atualiza o pip para a versão mais recente.
python -m pip install --upgrade pip

# Instala as dependências listadas em requirements.txt.
python -m pip install -r requirements.txt

# TODO: Definir download e organização dos modelos locais.
# TODO: Definir validação inicial do ambiente.
