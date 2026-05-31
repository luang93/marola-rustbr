# marola-rustbr

Repositorio de configuracao e customizacao do servidor Rust modded com Carbon.

Este repositorio nao versiona a instalacao completa do servidor. O foco aqui e manter sob controle de versao apenas o que faz sentido para administracao, manutencao e recuperacao do ambiente.

## Visao geral

Este projeto serve como base de manutencao do servidor. Aqui ficam os plugins, configuracoes, idiomas e scripts operacionais que definem o comportamento do ambiente modded.

Em vez de subir a instalacao completa do Rust Dedicated, o repositorio guarda so a camada que interessa para operacao e historico tecnico.

## O que este repositorio guarda

- Plugins Carbon em `carbon/plugins`
- Configuracoes dos plugins em `carbon/configs`
- Configuracoes principais do Carbon em `carbon/*.json`
- Arquivos de linguagem em `carbon/lang`
- Configuracoes de modulos em `carbon/modules`
- Scripts auxiliares na raiz, como `carbon.sh` e `runds.sh`
- Documentacao operacional, como `GUIA_SCRIPTS_BASICOS_CARBON.md`

## O que fica fora do Git

Para evitar subir lixo operacional, binarios grandes ou dados sensiveis/temporarios, o `.gitignore` exclui por padrao:

- Instalacao do Rust Dedicated e arquivos binarios grandes
- Bundles e arquivos do jogo
- Saves, mapas, bancos e dados de runtime do servidor
- Logs
- Dados gerados pelo Carbon em `carbon/data`, `carbon/logs` e `carbon/temp`

Esse repositorio representa a camada de customizacao do servidor, nao o servidor completo.

## Estrutura principal

```text
.
├── README.md
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

## Instalacao e preparo do ambiente

Este repositorio nao instala o servidor sozinho. Ele deve ser usado em cima de um ambiente Rust + Carbon ja existente.

Pre requisitos praticos:

- servidor Rust Dedicated ja instalado
- Carbon ja presente no ambiente
- Git instalado na maquina
- acesso ao repositorio

Para clonar este projeto em uma maquina com o ambiente pronto:

```bash
git clone https://github.com/luang93/marola-rustbr.git
cd marola-rustbr
```

Se a maquina ja tiver uma pasta do servidor pronta, o uso normal e atualizar os arquivos versionados dentro dela e manter fora do Git os binarios, saves e dados de runtime.

## Operacao diaria

Fluxo pratico para o dia a dia deste servidor:

### 1. Entrar na pasta do projeto

```bash
cd /home/rustserver/rustserver
```

### 2. Ver o que mudou

```bash
git status
```

### 3. Atualizar do GitHub antes de editar

```bash
git pull
```

### 4. Iniciar o servidor

Os scripts presentes na raiz indicam duas formas basicas de execucao:

```bash
./carbon.sh
```

ou

```bash
./runds.sh
```

Resumo rapido:

- `carbon.sh` inicializa o ambiente do Carbon e repassa os argumentos para o `RustDedicated`
- `runds.sh` exporta bibliotecas e executa o `RustDedicated` em modo batch

### 5. Editar configuracoes ou plugins

Areas mais comuns:

- `carbon/plugins` para plugins `.cs`
- `carbon/configs` para configuracoes dos plugins
- `carbon/lang` para mensagens e traducoes
- `carbon/modules` para configuracoes de modulos do Carbon

### 6. Salvar e validar no servidor

Como os watchers de script estao ativos neste ambiente, alteracoes em plugins normalmente sao detectadas automaticamente.

Quando precisar controlar manualmente, use os comandos do Carbon listados abaixo.

### 7. Versionar alteracoes

```bash
git add .
git commit -m "Describe the change"
git push
```

## Plugins atuais versionados

Atualmente o repositorio acompanha estes plugins principais:

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

## Guia para criar scripts

Existe um guia separado com exemplos basicos de plugin para este ambiente:

- `GUIA_SCRIPTS_BASICOS_CARBON.md`

Esse guia explica:

- Onde criar plugins
- Estrutura minima de um plugin Carbon/Oxide
- Exemplo com comando simples
- Uso de permissoes
- Fluxo seguro para testar sem baguncar o servidor

## Comandos uteis do Carbon

Este ambiente ja possui aliases configurados em `carbon/config.json`.

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

Como existe alias configurado, tambem da para usar:

```text
plugins
load NomeDoPlugin
reload NomeDoPlugin
unload NomeDoPlugin
```

Para administracao de permissoes em plugins, alguns comandos comuns do ecossistema Oxide/Carbon sao:

```text
oxide.grant user STEAM_ID permissao.exemplo
oxide.grant group admin permissao.exemplo
oxide.revoke user STEAM_ID permissao.exemplo
oxide.show user STEAM_ID
oxide.show group admin
```

Esses comandos sao uteis quando um plugin depende de permissao para liberar funcoes ou comandos.

## Fluxo de atualizacao com Git

Fluxo basico para atualizar este repositorio depois de alterar configs ou plugins:

```bash
cd /home/rustserver/rustserver
git status
git add .
git commit -m "Describe the change"
git push
```

## Boas praticas

- Edite plugins novos separadamente antes de mexer nos plugins grandes ja instalados
- Sempre faca `git pull` antes de iniciar uma rodada nova de alteracoes
- Revise o `git status` antes de cada commit
- Nao suba saves, logs, bancos ou dumps do servidor
- Nao trate a pasta versionada como backup completo do servidor
- Use este repositorio como historico de configuracao e codigo customizado

## Observacao

Se o servidor mudar muito de estrutura, revise o `.gitignore` para manter o repositorio limpo e evitar subir arquivos de runtime por acidente.
