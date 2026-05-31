# Auditoria do MarolaPets

## Escopo
Esta auditoria cobre a implementacao atual de `MarolaPets` em `/home/rustserver/rustserver/carbon/plugins/MarolaPets.cs`, plugin version `0.2.0`, e as evidencias mais recentes de runtime em `/home/rustserver/rustserver/carbon/logs/Carbon.Core.log`.

## Resumo Executivo
`MarolaPets` e um plugin Rust Carbon/Oxide em arquivo unico que implementa companion pets com movimentacao customizada, combate, aquisicao de alvo, protecao a aliados, bag, auto-feed, HUD em mundo e um sistema de progressao por velocidade, ataque, defesa e vitalidade.

O plugin esta funcional e atualmente carrega com hot-reload limpo, mas carrega alguns riscos importantes para engenharia:
- perfis de spawn ainda nao suportados continuam registrados para uso em producao
- a supressao da IA nativa depende de reflection fragil
- a persistencia guarda mais coisas do que o nome do arquivo sugere
- a implementacao esta concentrada em um arquivo monolitico grande

## Validacao em Runtime
Ultimo load limpo confirmado:
- `2026.05.31 06:14:07 Loaded plugin MarolaPets v0.2.0`

Historico relevante no mesmo log:
- falhas transitórias de compile em hot-reload ocorreram durante edicoes iterativas por volta de `06:10-06:11`
- essas falhas foram resolvidas e a ultima versao salva carregou com sucesso

Regra operacional:
- diagnosticos do editor sozinhos nao sao fonte suficiente de verdade para este plugin
- o log do Carbon deve ser tratado como fonte de verdade do estado final de compile/load

## Achados

### 1. Nem todos os tipos de pet registrados fazem spawn com sucesso
Severidade: Alta

Evidencia:
- `BuildProfiles()` registra `tiger/tigre` e `crocodile/crocodilo/jacare`
- logs anteriores registraram `Failed to create pet 'tigre'...` e `Failed to create pet 'jacare'...`

Impacto:
- a superficie de comandos anuncia pets que nao sao spawnaveis com confianca
- isso gera falhas visiveis para o jogador e polui suporte e testes de carga

Recomendacao:
- bloquear perfis nao suportados por feature flag ou removelos de `BuildProfiles()` ate que os caminhos de prefab sejam validados em runtime
- adicionar uma validacao de startup que monte um relatorio de capacidade por perfil

### 2. A supressao da IA nativa e baseada em reflection e fragil a updates do Rust
Severidade: Alta

Evidencia:
- `SuppressNativeAi()` desabilita componentes por pattern de nome como `Brain`, `FSM` e `Navigator`
- tambem escreve em membros internos como `AttackTarget`, `AttackTransform`, `sleeping` e `lastWarpTime`

Impacto:
- updates do jogo que renomeiem tipos runtime ou membros privados podem enfraquecer silenciosamente o controle do pet
- as falhas tendem a aparecer como desync, agressao aleatoria ou drift de movimento em vez de erro de compile

Recomendacao:
- isolar a supressao em uma camada de compatibilidade com telemetria explicita
- adicionar metricas de saude que contem acoes bem-sucedidas de supressao por familia de prefab
- considerar adapters por prefab em vez de name-matching generico se o plugin continuar sendo estrategico

### 3. O nome do arquivo de persistencia nao reflete mais o escopo real dos dados
Severidade: Media

Evidencia:
- `LoadData()` e `SaveData()` usam `MarolaPets/ally_data`
- `StoredData` hoje contem aliados, progressao, inventario da bag e estado de bag equipada

Impacto:
- manutencao e migracao ficam mais propensas a erro porque o nome sugere apenas dados de aliados
- operacao pode subestimar o blast radius de resets ou edicoes nesse arquivo

Recomendacao:
- renomear o arquivo persistido para algo como `MarolaPets/data`
- se houver necessidade de compatibilidade, adicionar uma migracao a partir de `ally_data`

### 4. A aquisicao automatica do modo agressivo e mais estreita que o modelo geral de combate
Severidade: Media

Evidencia:
- `/pet attack` manual pode mirar em `BaseCombatEntity` por `FindLookTarget()`
- a aquisicao automatica usa `TryAcquireAggressiveTarget()` -> `FindNearestHostilePlayer()`

Impacto:
- o modo agressivo fica centrado em players, enquanto o resto da superficie de combate sugere um modelo mais amplo
- isso pode confundir manutencao, design e QA ao balancear o sistema

Recomendacao:
- documentar isso como restricao intencional de design ou generalizar a aquisicao automatica para NPCs/animais com filtros explicitos

### 5. O estado runtime de pet ativo e intencionalmente nao persistido
Severidade: Media

Evidencia:
- apenas aliados, progresso, bag e bag equipada sao persistidos
- `Unload()` remove pets ativos e `OnPlayerDisconnected()` limpa pets orfaos

Impacto:
- restarts do servidor e reloads do plugin removem todos os pets ativos
- isso e seguro operacionalmente, mas e uma limitacao de produto que engenharia e operacao precisam considerar

Recomendacao:
- se persistencia em restart for desejada, definir um modelo `ActivePetSnapshot` com prefab, owner, vitais e ultima posicao conhecida
- se nao for desejada, manter o comportamento e documentar como nao-objetivo

### 6. A concentracao do codigo aumenta o risco de mudanca
Severidade: Media

Evidencia:
- todo o sistema vive em um unico arquivo com lifecycle, persistencia, comandos, movimento, combate, targeting, UI, inventario e formulas juntos

Impacto:
- hot-reloads ficam mais propensos a estados quebrados transitórios
- review, testabilidade e ownership boundaries ficam fracos

Recomendacao:
- quebrar o codigo em partial classes ou modulos internos por responsabilidade: lifecycle/config, runtime state, movement/combat, inventory, targeting/UI e persistence

### 7. A bag virtual usa uma entidade spawnada fora do mundo
Severidade: Baixa

Evidencia:
- a UI da bag usa `coffinstorage.prefab` spawnado em `y = -500`
- o ciclo de vida do container e limpo no close e no unload

Impacto:
- esse padrao e pragmatico, mas depende de cleanup confiavel e pode surpreender operacao ou ferramentas de debug

Recomendacao:
- manter se a UX estiver boa, mas documentar o ciclo de vida do container e considerar metricas de abertura/fechamento/cleanup

## Pontos Positivos de Engenharia
- a normalizacao de config e robusta e clampa valores inseguros antes de salvar
- o AI LOD e explicito e reduz trabalho desnecessario em pets distantes
- a protecao contra friendly fire cobre owners e pets aliados
- hooks de runtime estao expostos para integracao: `OnPetSpawned`, `OnPetDismissed`, `OnPetAttackStart`, `OnPetAttackStop`, `OnPetDeath`
- a simplificacao do HUD melhorou o resultado visual: texto em mundo agora esta restrito a `Name | Lv` e os stats foram movidos para `/pet status`

## Recomendacoes Operacionais
1. Remover ou desabilitar perfis de tigre/crocodilo antes de ampliar o release.
2. Adicionar uma smoke-test checklist leve apos cada hot-reload: spawn, follow, attack, bag, auto-feed e `/pet status`.
3. Tratar `/home/rustserver/rustserver/carbon/logs/Carbon.Core.log` como validacao obrigatoria de pos-deploy.
4. Renomear e migrar o arquivo de persistencia antes de adicionar mais sistemas persistidos.
5. Planejar uma etapa de refactor para separar o plugin em dominios menores antes da proxima onda grande de features.

## Prontidao para Release
Estado atual: Liberavel com restricoes

Justificativa:
- o runtime principal esta carregando e o conjunto de features e coerente
- no entanto, perfis anunciados mas nao suportados e a fragilidade da supressao da IA nativa ainda impedem um handoff de alta confianca sem endurecimento adicional
