# marola-rustbr

[![Rust Server](https://img.shields.io/badge/Rust-Dedicated%20Server-CE422B?style=for-the-badge&logo=rust&logoColor=white)](https://rust.facepunch.com/)
[![Carbon](https://img.shields.io/badge/Carbon-Modding%20Framework-1F2937?style=for-the-badge)](https://carbonmod.gg/)
[![Plugins](https://img.shields.io/badge/Plugins-C%23%20Oxide%2FCarbon-0A66C2?style=for-the-badge&logo=csharp&logoColor=white)](./carbon/plugins)
[![GitHub](https://img.shields.io/badge/GitHub-Versionado-181717?style=for-the-badge&logo=github&logoColor=white)](https://github.com/luang93/marola-rustbr)

Repositorio de configuracao, customizacao e documentacao tecnica do servidor Rust modded com Carbon.

O objetivo deste repositório e versionar a camada que realmente importa para engenharia e operacao:
- plugins customizados e de terceiros
- configuracoes do Carbon e dos plugins
- scripts de bootstrap e execucao
- documentacao tecnica, operacional e de manutencao

A instalacao completa do Rust Dedicated nao faz parte do escopo do Git.

## Escopo do repositorio

Este projeto representa a camada de customizacao do servidor, nao um snapshot completo da instalacao.

Versionado aqui:
- `carbon/plugins`
- `carbon/configs`
- `carbon/lang`
- `carbon/modules`
- arquivos `carbon/*.json`
- scripts operacionais na raiz
- documentacao em `docs/`

Fora do Git por padrao:
- binarios do servidor e do jogo
- bundles e artefatos grandes
- saves, mapas e runtime state do jogo
- logs e arquivos temporarios
- diretorios gerados pelo Carbon, como `carbon/data`, `carbon/logs` e `carbon/temp`

## Estrutura principal

```text
.
├── README.md
├── docs/
│   ├── MarolaPets_AUDIT.md
│   ├── MarolaPets_ENGINEERING.md
│   └── MarolaPets_SYSTEM.md
├── GUIA_SCRIPTS_BASICOS_CARBON.md
├── carbon.sh
├── runds.sh
├── carbon/
│   ├── config.json
│   ├── config.auto.json
│   ├── config.profiler.json
│   ├── config.webpanel.json
│   ├── configs/
│   ├── lang/
│   ├── modules/
│   └── plugins/
└── .gitignore
```

## Documentacao disponivel

Documentacao geral:
- `README.md`: entrada principal do repositorio
- `GUIA_SCRIPTS_BASICOS_CARBON.md`: guia rapido para criar scripts/plugins

Documentacao do sistema de pet:
- `docs/MarolaPets_SYSTEM.md`: visao funcional completa do sistema de pet
- `docs/MarolaPets_ENGINEERING.md`: documentacao tecnica para engenharia
- `docs/MarolaPets_AUDIT.md`: auditoria tecnica, riscos e recomendacoes

## MarolaPets

O `MarolaPets` e o principal plugin customizado atual do servidor.

Arquivo principal:
- `carbon/plugins/MarolaPets.cs`

Capacidades principais do sistema:
- spawn de pets com aliases em PT-BR e EN
- follow, stay, guard, recall e ataque manual por mira do mouse
- modo agressivo com protecao contra aliados
- HUD 3D minimalista com nome e level
- preview 3D do alvo de ataque
- bag do pet com item-gate via `horse.saddlebag`
- consumo automatico de comida e agua da mochila ou do chao
- progressao por velocidade, ataque, defesa e vitalidade
- status detalhado via `/pet status`

Arquivos relacionados:
- `carbon/plugins/MarolaPets.cs`
- `carbon/configs/MarolaPets.json`
- `carbon/lang/en/MarolaPets.json`
- `docs/MarolaPets_SYSTEM.md`
- `docs/MarolaPets_ENGINEERING.md`
- `docs/MarolaPets_AUDIT.md`

Estado operacional importante:
- a validacao real de reload do plugin deve ser feita em `carbon/logs/Carbon.Core.log`
- alguns aliases cadastrados no codigo, como tigre e jacare/crocodilo, ainda precisam de validacao final de prefab em runtime

## Preparacao do ambiente

Este repositorio pressupoe um ambiente Rust Dedicated + Carbon ja existente.

Pre-requisitos praticos:
- Rust Dedicated instalado
- Carbon instalado e funcional
- Git instalado na maquina
- acesso ao repositorio remoto
- permissao para editar plugins/configs no ambiente do servidor

Clonagem basica:

```bash
git clone https://github.com/luang93/marola-rustbr.git
cd marola-rustbr
```

No ambiente atual de operacao:

```bash
cd /home/rustserver/rustserver
```

## Operacao diaria

Fluxo recomendado:

### 1. Verificar estado atual

```bash
git status
```

### 2. Atualizar antes de editar

```bash
git pull
```

### 3. Editar a camada versionada

Areas mais comuns:
- `carbon/plugins` para plugins `.cs`
- `carbon/configs` para configuracoes de plugins
- `carbon/lang` para mensagens e traducoes
- `carbon/modules` para modulos do Carbon
- `docs` para material tecnico e operacional

### 4. Iniciar ou reiniciar o servidor

Scripts mais usados:

```bash
./carbon.sh
```

ou

```bash
./runds.sh
```

Resumo:
- `carbon.sh`: inicializa o ambiente do Carbon e repassa os argumentos para o `RustDedicated`
- `runds.sh`: exporta bibliotecas e executa o `RustDedicated` em modo batch

### 5. Validar o comportamento

Para plugins Carbon/Oxide, sempre validar em dois niveis:
- nivel editor: sintaxe e estrutura do arquivo
- nivel runtime: `carbon/logs/Carbon.Core.log`

### 6. Versionar alteracoes

```bash
git status
git add <arquivos>
git commit -m "Describe the change"
git push
```

## Comandos uteis do Carbon

Este ambiente possui aliases configurados em `carbon/config.json`.

Aliases disponiveis:

```text
carbon  -> c.version
plugins -> c.plugins
reload  -> c.reload
load    -> c.load
unload  -> c.unload
```

Comandos uteis no console ou RCON:

```text
c.version
c.plugins
c.load NomeDoPlugin
c.reload NomeDoPlugin
c.unload NomeDoPlugin
```

Uso pratico:
- `c.version`: mostra a versao do Carbon
- `c.plugins`: lista plugins carregados
- `c.load NomeDoPlugin`: carrega um plugin manualmente
- `c.reload NomeDoPlugin`: recarrega um plugin depois de editar
- `c.unload NomeDoPlugin`: descarrega um plugin

Tambem e possivel usar os aliases curtos:

```text
plugins
load NomeDoPlugin
reload NomeDoPlugin
unload NomeDoPlugin
```

## Permissoes

Para plugins Oxide/Carbon, comandos frequentes:

```text
oxide.grant user STEAM_ID permissao.exemplo
oxide.grant group admin permissao.exemplo
oxide.revoke user STEAM_ID permissao.exemplo
oxide.show user STEAM_ID
oxide.show group admin
```

No caso do MarolaPets, a permissao base e:

```text
marolapets.use
```

## Plugins e configuracoes versionados

O repositorio versiona configuracao e/ou codigo para varios sistemas do servidor, incluindo exemplos como:
- Backpacks
- BetterLoot
- Clans
- CopyPaste
- ImageLibrary
- Kits
- NTeleportation
- RaidableBases
- Skins
- XPerience
- MarolaPets

## Boas praticas de engenharia

- sempre revisar `git status` antes de commitar
- nao subir logs, saves, bancos e artefatos de runtime
- validar reload final em `carbon/logs/Carbon.Core.log`
- preferir commits pequenos e focados por sistema
- documentar decisoes de plugin customizado em `docs/`
- separar mudancas do sistema de pet de alteracoes operacionais sem relacao

## Fluxo recomendado para o sistema de pet

Para alterar o `MarolaPets` com seguranca:

```bash
cd /home/rustserver/rustserver
git status
c.reload MarolaPets
```

Depois validar:
- spawn do pet
- follow/stay/guard
- `/pet attack`
- `/pet status`
- bag e auto-feed
- linhas finais de `carbon/logs/Carbon.Core.log`

## Observacoes finais

Se a estrutura do servidor mudar, revise o `.gitignore` e o `README` para manter o repositorio coerente com a camada realmente versionada.

Para leitura focada no pet, a entrada principal da equipe deve ser:
- `docs/MarolaPets_SYSTEM.md`
