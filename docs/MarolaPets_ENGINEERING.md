# Documentacao de Engenharia do MarolaPets

## 1. Visao Geral
`MarolaPets` e um `RustPlugin` Carbon/Oxide localizado em `/home/rustserver/rustserver/carbon/plugins/MarolaPets.cs`.

Metadados atuais do plugin:
- Nome: `MarolaPets`
- Versao: `0.2.0`
- Tipo base: `RustPlugin`
- Comando principal do jogador: `/pet`
- Permissao obrigatoria: `marolapets.use`

O plugin fornece um runtime de companion pet com estas capacidades principais:
- spawn de perfis de pet suportados
- comportamento customizado de follow, stay, guard e attack
- regras de combate seguras para friendly e aliados
- selecao de alvo por mira do mouse para `/pet attack`
- HUD em mundo e preview de alvo de ataque
- bag do pet bloqueada por `horse.saddlebag`
- auto-feed por itens da bag ou dropados no chao
- progressao por velocidade, ataque, defesa e vitalidade

## 2. Arquitetura de Runtime
O plugin e implementado em um unico arquivo, mas internamente separado em modulos focados:
- `CompanionBrain`: despachante principal de estado
- `CompanionMovement`: follow, stay, guard, movimentacao e posicionamento
- `CompanionCombat`: perseguicao de alvo, leash, cadencia e inicio de ataque
- `CompanionRecovery`: deteccao de stuck, reposicionamento lateral e teleporte seguro
- `CompanionPhysics`: grounding, evitacao local de obstaculos e suporte a agua

O wiring desses modulos acontece em `InitializeModules()` depois do carregamento de config, dados e perfis.

### Modelos centrais de runtime
- `PetState`: estado em memoria de um pet ativo
- `PetProgress`: levels persistidos e XP
- `StoredData`: aliados persistidos, progresso, bag e equipamento da bag
- `PetProfile`: definicao de pet suportado para spawn
- `ThreatInfo`: bookkeeping do alvo atual em combate

### Registros principais
- `_pets`: estado do pet ativo por owner id
- `_petOwnersByEntity`: lookup reverso da entidade para o dono
- `_bagContainersByOwner`: container temporario de loot por owner
- `_bagOwnersByContainer`: lookup reverso de containers de bag abertos
- `_profiles`: definicoes de pets spawnaveis por alias/nome

## 3. Ciclo de Vida
### `Init()`
Responsabilidades:
- registrar permissao
- carregar e normalizar config
- carregar dados persistidos
- registrar perfis de pet
- inicializar modulos de runtime
- registrar o chat command `/pet`

### `OnServerInitialized()`
Inicia o timer do scheduler usando a menor cadencia retornada por `GetSchedulerInterval()`.

### `Unload()`
Responsabilidades:
- destruir o timer de update
- persistir dados
- fechar bags abertas
- remover todos os pets ativos criados por este plugin

### `OnServerSave()`
Persiste `StoredData`.

### `OnPlayerDisconnected()`
Remove pets ativos de jogadores desconectados para evitar entidades orfas.

## 4. Modelo de Persistencia
A persistencia usa `DynamicConfigFile` via:
- caminho: `MarolaPets/ally_data`

Estruturas persistidas:
- `AlliesByOwner`
- `ProgressByOwner`
- `BagByOwner`
- `BagEquippedByOwner`

Nao persistido:
- entidades runtime de pet ativas
- alvo atual de combate
- ancora atual de guarda
- estado temporario de HUD
- AI tier atual e estado de supressao da IA nativa

Na pratica, pets ativos sao efemeros e desaparecem em reload/restart.

## 5. Perfis de Pet Suportados
Os perfis sao registrados em `BuildProfiles()`.

Aliases mapeados atualmente:
- wolf, lobo
- bear, urso
- polarbear, ursopolar, urso-polar
- boar, javali
- chicken, galinha
- stag, veado
- tiger, tigre
- crocodile, crocodilo, jacare

Campos do perfil:
- `Key`
- `DisplayName`
- `Prefab`
- `CanSwim`

Observacao importante de runtime:
- aliases de tigre e crocodilo/jacare estao registrados no codigo, mas logs anteriores mostraram falhas de spawn nesses prefabs

## 6. Superficie de Comandos
Toda a interacao do jogador passa por `CmdPet()`.

Subcomandos suportados:
- `/pet help`
- `/pet spawn [tipo]`
- `/pet dismiss`
- `/pet recall`
- `/pet status`
- `/pet diagnose`
- `/pet debug`
- `/pet follow`
- `/pet stay`
- `/pet guard`
- `/pet radius <5|10|20>`
- `/pet attack`
- `/pet bag equip`
- `/pet bag remove`
- `/pet bag add [qtd]`
- `/pet bag take <item> [qtd]`
- `/pet bag ui`
- `/pet ally add [nome]`
- `/pet ally remove [nome]`
- `/pet ally list`
- `/pet passive`
- `/pet aggressive`

### Semantica dos comandos
- `follow`: o pet segue um offset ao redor do dono
- `stay`: o pet para no local
- `guard`: o pet ancora a posicao atual e respeita o raio de guarda
- `attack`: manda o pet atacar o alvo que esta sob a mira
- `status`: leitura de status para progressao e vitais
- `diagnose`: diagnostico tecnico/runtime
- `debug`: informacoes internas de movimento e combate

## 7. Loop de IA e Agendamento
O loop central de runtime e `UpdatePets()`.

Para cada pet ativo, o loop:
1. valida owner e entidade do pet
2. reaplica a supressao de IA nativa quando configurado
3. calcula a distancia do owner e o AI LOD tier
4. controla throttling com `NextThinkTime`
5. atualiza vitais e progressao
6. tenta adquirir alvo automatico em modo agressivo
7. delega o comportamento ao `CompanionBrain`
8. desenha HUD e preview de alvo

### AI LOD tiers
- `Full`
- `Simplified`
- `Sleeping`

Defaults atuais:
- full range: `30m`
- simplified range: `80m`
- full think interval: `0.1s`
- simplified think interval: `0.25s`
- sleeping think interval: `1.0s`

Notas de comportamento:
- se o pet tiver alvo, ele entra forcadamente em `Full`
- pets em `Sleeping` ainda podem desenhar o HUD minimo, mas pulam parte da logica pesada quando estao sem alvo

## 8. Modelo de Movimento
O movimento e dirigido pelo plugin, nao pela IA nativa do prefab.

### Comportamento de follow
`CompanionMovement.UpdateFollow()`:
- usa offsets rotativos laterais e traseiros ao redor do owner
- preserva distancia minima do owner
- alterna entre walk, run e sprint
- aplica aceleracao e desaceleracao suave por `SmoothedVelocity`

### Comportamento de guard/stay
`CompanionMovement.UpdateStay()`:
- ancora em `GuardPosition`
- permite raio quando o estado e `Guard`
- fora isso, funciona como hold position com stop distance

### Comportamento de recovery
`CompanionRecovery.TryRecover()` escala em estagios:
1. refresh local de path
2. reposicionamento lateral
3. teleporte seguro para perto do owner se distancia e combate permitirem

### Comportamento fisico
`CompanionPhysics` fornece:
- grounding com suporte a terreno e agua
- evitacao local por sphere-cast
- refresh alternativo de path para esquerda/direita se a frente estiver bloqueada

## 9. Modelo de Combate
O combate e totalmente controlado por `CompanionCombat`.

### Condicoes de inicio
`TryStartAttack()` valida:
- existencia do pet state e do owner
- alvo valido de combate
- alvo nao friendly
- alvo dentro do activation range

### Perseguicao e leash
`UpdateCombat()`:
- limpa o alvo se ele for invalido ou friendly
- limpa o alvo se nao for visto por mais de `TargetCommitTime`
- limpa o alvo se a distancia owner-to-target ultrapassar `LeashDistance`
- move em direcao ao alvo ate atingir `EngageDistance`

### Caminho de golpe
Quando esta em range:
- rotaciona para o alvo
- respeita `NextAttackTime`
- aplica `GetPetAttackDamage(state)` como dano slash
- atualiza o timestamp de threat
- registra XP de treino de ataque

### Regras de friendly
As checagens de friendly cobrem:
- owner
- players aliados
- outros pets do owner ou de aliados

## 10. Selecao de Alvo e Feedback de UI
### Targeting manual de ataque
`FindLookTarget()` implementa selecao por mira do mouse:
- primeiro tenta raycast direto com `Physics.DefaultRaycastLayers`
- depois faz score em `BaseCombatEntity` proximos do raio
- exclui self, aliados e entidades que sao pets

`FindLookPlayer()` trata a selecao de players por um cone mais estreito.

### Preview de ataque
`TryDrawAttackTargetPreview()` desenha:
- texto vermelho sobre o alvo atual: `ALVO: {nome}`
- linha vermelha dos olhos do player ate o ponto de mira do alvo

### HUD do pet em mundo
`TryDrawPetWorldUi()` agora desenha apenas:
- `Nome | Lv X`

Os stats detalhados foram removidos do HUD flutuante e movidos para `/pet status`.

## 11. Vitais e Progressao
O sistema atualmente rastreia dois reservatorios:
- fome, armazenada em `PetState.Hunger`
- sede/energia, armazenada em `PetState.Stamina`

O nome `Stamina` continua no codigo, mas para o usuario passou a representar sede/energia.

### Modelo de dreno
Em cada update de vitais:
- a fome diminui com o tempo
- a sede diminui com o tempo
- movimento consome sede adicional
- ataque consome sede adicional

### Dimensoes de treino
- `Speed`
- `Attack`
- `Defense`
- `Vitality`

### Efeitos por level
#### Speed
- fonte de XP: distancia percorrida
- efeito: multiplicador de velocidade via `GetPetMoveSpeed()`

#### Attack
- fonte de XP: ataques bem-sucedidos
- efeitos:
  - multiplicador de dano via `GetPetAttackDamage()`
  - reducao da cadencia via `GetPetAttackCooldown()`

#### Defense
- fonte de XP: dano recebido
- efeito: reducao de dano de entrada via `GetPetDefenseReduction()`

#### Vitality
- fonte de XP: tempo de sobrevivencia acumulado
- efeitos:
  - aumenta capacidade maxima de fome
  - aumenta capacidade maxima de sede

### Level agregado do pet
O level exibido no HUD e calculado por:
- base `1`
- mais os deltas positivos de `SpeedLevel`, `AttackLevel`, `DefenseLevel` e `VitalityLevel`

Esse level e apenas de exibicao, nao uma trilha de XP independente.

## 12. Modelo de Saida do `/pet status`
`/pet status` agora e a principal superficie de status do sistema.

Secoes atuais:
- cabecalho: nome do pet e level agregado
- vitais: fome e sede atuais/maximas e percentuais
- speed: nivel de speed, run speed efetiva e sprint speed efetiva
- combat: nivel de ataque, dano efetivo, cooldown efetivo, nivel de defesa e reducao percentual

Isso foi intencionalmente movido para o chat para manter o HUD em mundo limpo.

## 13. Sistema de Bag e Alimentacao
### Gate de equipamento
O uso da bag do pet exige o item:
- `horse.saddlebag`

### Implementacao da UI
A UI da bag e implementada por um `StorageContainer` temporario criado a partir de:
- `assets/prefabs/misc/halloween/coffin/coffinstorage.prefab`

Ciclo de vida do container:
- spawn escondido fora do mapa
- sync dos itens persistidos para dentro do container
- abertura do painel de loot para o player
- sync de volta ao fechar
- limpeza da entidade temporaria

### Recursos suportados
A bag aceita apenas itens presentes nos mapas configurados:
- `Inventory.FoodRestore`
- `Inventory.WaterRestore`

### Ordem de auto-consumo
`TryAutoConsumeFromBag()` chama `TryAutoConsumeFromGround()` primeiro.

Prioridade efetiva:
1. consumir comida/agua do chao, se houver
2. caso contrario, consumir da bag equipada se os thresholds forem atingidos

Isso significa que itens do chao tem prioridade sobre o estoque da bag.

## 14. Supressao da IA Nativa
O plugin tenta se tornar a unica fonte de decisao de comportamento do pet.

Mecanismo:
- varre os componentes runtime do NPC spawnado
- desabilita componentes cujo nome de tipo combine com `Brain`, `FSM` ou `Navigator`
- invoca metodos de stop/disable quando existirem
- limpa referencias de alvos hostis quando configurado

Nota de engenharia:
- isso funciona, mas e sensivel a updates porque depende de reflection e nomes internos da runtime

## 15. Hooks e Pontos de Integracao
O plugin emite estes hooks relevantes:
- `OnPetSpawned`
- `OnPetDismissed`
- `OnPetAttackStart`
- `OnPetAttackStop`
- `OnPetDeath`

Ele tambem consulta `CanLootEntity` antes de abrir a UI da bag.

## 16. Dominios de Config
`PluginConfig` e dividido em:
- `Movement`
- `Combat`
- `AiLod`
- `NativeAi`
- `Recall`
- `Ui`
- `Training`
- `Inventory`
- `Recovery`

O carregamento de config e normalizado e clampado em `LoadConfigValues()` antes de ser salvo de volta em disco.

Isso significa que configs invalidas ou parciais sao coeridas para faixas validas no load.

## 17. Validacao Operacional
Fonte obrigatoria de validacao:
- `/home/rustserver/rustserver/carbon/logs/Carbon.Core.log`

Nao confie apenas em diagnosticos do editor ou estados intermediarios de hot-reload.

Um reload so deve ser considerado valido quando a ultima entrada relevante do log mostrar:
- `Loaded plugin MarolaPets v0.2.0`

Ultimo load limpo observado:
- `2026.05.31 06:14:07`

## 18. Limitacoes Conhecidas
- aliases ligados a tigre e crocodilo/jacare ainda nao sao confiaveis em runtime
- pets ativos nao persistem entre restart/reload
- o plugin continua monolitico em um unico arquivo
- a supressao da IA nativa e fragil frente a mudancas upstream do Rust
- a aquisicao automatica do modo agressivo hoje foca em players hostis, nao no conjunto completo de entidades de combate

## 19. Proximos Passos Recomendados de Engenharia
1. Validar ou remover perfis com prefab ainda nao confiavel.
2. Renomear e migrar o arquivo de persistencia de `ally_data` para um data file generico do plugin.
3. Dividir o plugin em unidades menores por dominio.
4. Adicionar uma smoke-test checklist explicita para spawn, follow, attack, bag, feed e `/pet status`.
5. Adicionar logging estruturado para sucesso/falha da supressao de IA nativa por familia de prefab.
