# 🌊 marola-rustbr

[![Rust Server](https://img.shields.io/badge/Rust-Dedicated%20Server-CE422B?style=for-the-badge&logo=rust&logoColor=white)](https://rust.facepunch.com/)
[![Carbon](https://img.shields.io/badge/Carbon-Modding%20Framework-1F2937?style=for-the-badge)](https://carbonmod.gg/)
[![Plugins](https://img.shields.io/badge/Plugins-C%23%20Oxide%2FCarbon-0A66C2?style=for-the-badge&logo=csharp&logoColor=white)](./carbon/plugins)
[![GitHub](https://img.shields.io/badge/GitHub-Versionado-181717?style=for-the-badge&logo=github&logoColor=white)](https://github.com/luang93/marola-rustbr)

> Repositório de configuração, customização e documentação técnica de um servidor **Rust modded com Carbon**.

## ✨ Visão geral

Este projeto versiona a camada que realmente importa para a engenharia e operação do servidor:

- 🧩 plugins customizados e de terceiros
- ⚙️ configurações do Carbon e dos plugins
- 🚀 scripts de bootstrap e execução
- 📚 documentação técnica, operacional e de manutenção

> A instalação completa do **Rust Dedicated** não faz parte do escopo do Git.

## 📦 Escopo do repositório

Este repositório representa a camada de customização do servidor, **não** um snapshot completo da instalação.

### ✅ Versionado aqui

- `carbon/plugins`
- `carbon/configs`
- `carbon/lang`
- `carbon/modules`
- arquivos `carbon/*.json`
- scripts operacionais na raiz
- documentação em `docs/`

### 🚫 Fora do Git por padrão

- binários do servidor e do jogo
- bundles e artefatos grandes
- saves, mapas e runtime state do jogo
- logs e arquivos temporários
- diretórios gerados pelo Carbon, como `carbon/data`, `carbon/logs` e `carbon/temp`

## 🗂️ Estrutura principal

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

## 📚 Documentação disponível

### Geral

| Arquivo | Descrição |
| --- | --- |
| `README.md` | Entrada principal do repositório |
| `GUIA_SCRIPTS_BASICOS_CARBON.md` | Guia rápido para criar scripts/plugins |

### Sistema de pet

| Arquivo | Descrição |
| --- | --- |
| `docs/MarolaPets_SYSTEM.md` | Visão funcional completa do sistema de pet |
| `docs/MarolaPets_ENGINEERING.md` | Documentação técnica para engenharia |
| `docs/MarolaPets_AUDIT.md` | Auditoria técnica, riscos e recomendações |

## 🐾 MarolaPets

O `MarolaPets` é o principal plugin customizado atual do servidor.

### 📍 Arquivo principal

- `carbon/plugins/MarolaPets.cs`

### 🛠️ Capacidades principais

- spawn de pets com aliases em PT-BR e EN
- follow, stay, guard, recall e ataque manual por mira do mouse
- modo agressivo com proteção contra aliados
- HUD 3D minimalista com nome e level
- preview 3D do alvo de ataque
- bag do pet com item-gate via `horse.saddlebag`
- consumo automático de comida e água da mochila ou do chão
- progressão por velocidade, ataque, defesa e vitalidade
- status detalhado via `/pet status`

### 🔗 Arquivos relacionados

- `carbon/plugins/MarolaPets.cs`
- `carbon/configs/MarolaPets.json`
- `carbon/lang/en/MarolaPets.json`
- `docs/MarolaPets_SYSTEM.md`
- `docs/MarolaPets_ENGINEERING.md`
- `docs/MarolaPets_AUDIT.md`

### ⚠️ Estado operacional importante

- a validação real de reload do plugin deve ser feita em `carbon/logs/Carbon.Core.log`
- alguns aliases cadastrados no código, como `tigre` e `jacare/crocodilo`, ainda precisam de validação final de prefab em runtime

## 🧰 Preparação do ambiente

Este repositório pressupõe um ambiente **Rust Dedicated + Carbon** já existente.

### Pré-requisitos práticos

- Rust Dedicated instalado
- Carbon instalado e funcional
- Git instalado na máquina
- acesso ao repositório remoto
- permissão para editar plugins/configs no ambiente do servidor

### Clonagem básica

```bash
git clone https://github.com/luang93/marola-rustbr.git
cd marola-rustbr
```

### Ambiente atual de operação

```bash
cd /home/rustserver/rustserver
```

## 🔄 Operação diária

### 1. Verificar estado atual

```bash
git status
```

### 2. Atualizar antes de editar

```bash
git pull
```

### 3. Editar a camada versionada

Áreas mais comuns:

- `carbon/plugins` para plugins `.cs`
- `carbon/configs` para configurações de plugins
- `carbon/lang` para mensagens e traduções
- `carbon/modules` para módulos do Carbon
- `docs` para material técnico e operacional

### 4. Iniciar ou reiniciar o servidor

Scripts mais usados:

```bash
./carbon.sh
```

ou

```bash
./runds.sh
```

**Resumo rápido**

- `carbon.sh`: inicializa o ambiente do Carbon e repassa os argumentos para o `RustDedicated`
- `runds.sh`: exporta bibliotecas e executa o `RustDedicated` em modo batch

### 5. Validar o comportamento

Para plugins Carbon/Oxide, sempre validar em dois níveis:

- nível editor: sintaxe e estrutura do arquivo
- nível runtime: `carbon/logs/Carbon.Core.log`

### 6. Versionar alterações

```bash
git status
git add <arquivos>
git commit -m "Describe the change"
git push
```

## 🎮 Comandos úteis do Carbon

Este ambiente possui aliases configurados em `carbon/config.json`.

### Aliases disponíveis

```text
carbon  -> c.version
plugins -> c.plugins
reload  -> c.reload
load    -> c.load
unload  -> c.unload
```

### Comandos úteis no console ou RCON

```text
c.version
c.plugins
c.load NomeDoPlugin
c.reload NomeDoPlugin
c.unload NomeDoPlugin
```

### Uso prático

- `c.version`: mostra a versão do Carbon
- `c.plugins`: lista plugins carregados
- `c.load NomeDoPlugin`: carrega um plugin manualmente
- `c.reload NomeDoPlugin`: recarrega um plugin depois de editar
- `c.unload NomeDoPlugin`: descarrega um plugin

Também é possível usar os aliases curtos:

```text
plugins
load NomeDoPlugin
reload NomeDoPlugin
unload NomeDoPlugin
```

## 🔐 Permissões

Para plugins Oxide/Carbon, comandos frequentes:

```text
oxide.grant user STEAM_ID permissao.exemplo
oxide.grant group admin permissao.exemplo
oxide.revoke user STEAM_ID permissao.exemplo
oxide.show user STEAM_ID
oxide.show group admin
```

No caso do MarolaPets, a permissão base é:

```text
marolapets.use
```

## 🧩 Plugins e configurações versionados

O repositório versiona configuração e/ou código para vários sistemas do servidor, incluindo:

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

## ✅ Boas práticas de engenharia

- sempre revisar `git status` antes de commitar
- não subir logs, saves, bancos e artefatos de runtime
- validar reload final em `carbon/logs/Carbon.Core.log`
- preferir commits pequenos e focados por sistema
- documentar decisões de plugin customizado em `docs/`
- separar mudanças do sistema de pet de alterações operacionais sem relação

## 🐎 Fluxo recomendado para o sistema de pet

Para alterar o `MarolaPets` com segurança:

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

## 📝 Observações finais

Se a estrutura do servidor mudar, revise o `.gitignore` e o `README` para manter o repositório coerente com a camada realmente versionada.

Para leitura focada no pet, a entrada principal da equipe deve ser:

- `docs/MarolaPets_SYSTEM.md`
