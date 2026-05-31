# Guia do Sistema MarolaPets

## Objetivo
Este documento e o guia funcional dedicado ao sistema `MarolaPets`.

Ele explica, em um unico lugar, o que o sistema de pet faz, como os jogadores interagem com ele, como o runtime se comporta, quais arquivos importam e o que engenharia e operacao devem esperar da implementacao atual.

Para detalhes mais profundos de engenharia, consulte:
- `docs/MarolaPets_ENGINEERING.md`
- `docs/MarolaPets_AUDIT.md`

## Resumo do Sistema
`MarolaPets` e um plugin customizado Carbon/Oxide que transforma animais NPC do Rust em companions controlados pelo jogador.

O sistema atualmente inclui:
- spawn e dismiss de pets
- estados de follow, stay, guard e recall
- ordem manual de ataque baseada na mira do mouse
- comportamento agressivo com protecao a aliados
- identificacao 3D flutuante sobre o pet
- preview 3D de alvo ao mirar
- bag do pet e armazenamento de itens
- consumo de comida e agua da mochila ou de itens dropados no chao
- progressao em velocidade, ataque, defesa e vitalidade
- comandos de runtime e debug para suporte e balanceamento

Arquivo principal do plugin:
- `carbon/plugins/MarolaPets.cs`

## Experiencia do Jogador
### O que o jogador ve
O HUD atual em cima do pet foi reduzido de forma intencional para:
- `Nome | Lv X`

Os status detalhados nao ficam mais flutuando sobre o mob. Agora eles aparecem por:
- `/pet status`

Quando o jogador mira em algo e prepara uma ordem de ataque, o sistema pode mostrar:
- um texto vermelho `ALVO: {nome}` no alvo selecionado
- uma linha vermelha da visao do jogador ate o alvo

### Loop principal de gameplay
Um fluxo tipico do jogador e:
1. spawnar um pet
2. colocar o pet em follow, stay ou guard
3. usar o pet de forma defensiva ou agressiva
4. alimentar por comida/agua dropada ou usar a bag do pet
5. evoluir o level ao longo do tempo
6. inspecionar o estado atual com `/pet status`, `/pet debug` ou `/pet diagnose`

## Arquivos Importantes
Arquivos centrais:
- `carbon/plugins/MarolaPets.cs`
- `carbon/configs/MarolaPets.json`
- `carbon/lang/en/MarolaPets.json`

Documentos de apoio:
- `docs/MarolaPets_ENGINEERING.md`
- `docs/MarolaPets_AUDIT.md`

Fonte de validacao em runtime:
- `carbon/logs/Carbon.Core.log`

## Comandos
Toda a interacao passa pelo comando `/pet`.

### Comandos basicos
- `/pet help`
- `/pet spawn [tipo]`
- `/pet dismiss`
- `/pet recall`
- `/pet status`
- `/pet diagnose`
- `/pet debug`

### Comandos de controle
- `/pet follow`
- `/pet stay`
- `/pet guard`
- `/pet radius <5|10|20>`

### Comandos de combate
- `/pet attack`
- `/pet passive`
- `/pet aggressive`

### Comandos de aliados
- `/pet ally add [nome]`
- `/pet ally remove [nome]`
- `/pet ally list`

### Comandos da bag
- `/pet bag equip`
- `/pet bag remove`
- `/pet bag add [qtd]`
- `/pet bag take <item> [qtd]`
- `/pet bag ui`

## Pets Suportados
O codigo atualmente registra aliases para estes tipos de pet:
- wolf, lobo
- bear, urso
- polarbear, ursopolar, urso-polar
- boar, javali
- chicken, galinha
- stag, veado
- tiger, tigre
- crocodile, crocodilo, jacare

Observacao operacional importante:
- alguns aliases estao registrados no codigo, mas ainda precisam de validacao real de prefab em runtime
- entradas ligadas a tigre e crocodilo/jacare ja mostraram falha de spawn em validacoes anteriores no log

Do ponto de vista de produto, so devem ser considerados realmente suportados os pets que spawnam com sucesso em runtime.

## Modelo de Comportamento
### Follow
O pet segue o dono usando offsets rotativos em vez de ficar colado no player.

Isso melhora:
- legibilidade
- sensacao de colisao
- percepcao de movimento natural

### Stay
O pet fica parado e deixa de tentar seguir o dono.

### Guard
O pet armazena uma ancora de guarda e tenta permanecer dentro do raio configurado.

Raios de guarda permitidos hoje:
- `5`
- `10`
- `20`

### Recall
Se o pet ficar longe demais ou se perder, o recall o teleporta com seguranca para perto do dono, respeitando cooldown e regras de recovery.

## Modelo de Combate
### Ataque manual
`/pet attack` usa a entidade que esta sob a mira do jogador.

A aquisicao de alvo nao depende apenas de um hit central simples. Ela combina:
- deteccao de hit direto no raycast
- score por cone de mira para entidades proximas
- regras de exclusao para alvos friendly e entidades que sao pets

### Modo agressivo
No modo agressivo, o pet pode adquirir automaticamente players hostis proximos.

Esse caminho e mais estreito que o caminho manual completo. O targeting manual pode trabalhar com um conjunto mais amplo de `BaseCombatEntity`, enquanto a aquisicao automatica agressiva hoje foca em players hostis.

### Protecao a friendly
O pet nao deve atacar:
- o proprio dono
- players aliados
- pets do dono ou de aliados

### Limites de combate
O comportamento de combate e limitado por:
- activation range
- target commit time
- leash distance
- engage distance
- cooldown entre golpes

## HUD e Status
### HUD em mundo
O HUD flutuante foi reduzido de forma intencional para:
- nome exibido do pet
- level agregado

Motivo:
- blocos grandes e multiline de `ddraw.text` ficaram visualmente ruins e instaveis na pratica
- os numeros detalhados agora vivem no chat, onde sao mais legiveis e mais simples de manter

### `/pet status`
`/pet status` agora e a principal superficie de status do sistema.

Ele mostra:
- nome do pet e level agregado
- fome atual e capacidade de fome
- sede atual e capacidade de sede
- velocidade efetiva de corrida e sprint
- dano efetivo atual
- cadencia efetiva de ataque
- nivel e reducao de defesa

## Modelo de Progressao
O pet possui quatro trilhas de treino.

### Velocidade
Como evolui:
- ganha XP por distancia percorrida

O que altera:
- velocidade efetiva de movimento
- resposta de follow, run e sprint

### Ataque
Como evolui:
- ganha XP por ataques bem-sucedidos

O que altera:
- dano
- cadencia de golpes

### Defesa
Como evolui:
- ganha XP por dano recebido

O que altera:
- reducao de dano de entrada

### Vitalidade
Como evolui:
- ganha XP por tempo de sobrevivencia nas atualizacoes

O que altera:
- capacidade de fome
- capacidade de sede

### Level agregado
O level mostrado acima do pet nao e uma barra de XP separada.

Ele e um level de exibicao agregado, construido pela soma dos deltas positivos de:
- speed level
- attack level
- defense level
- vitality level

## Fome, Sede e Alimentacao
O sistema rastreia dois reservatorios de longo prazo:
- fome
- sede

Observacao de implementacao:
- internamente, a sede ainda usa o campo `Stamina` no codigo
- no texto para o jogador, esse valor passou a ser tratado como sede/energia

### Dreno de recursos
Com o tempo o pet perde:
- fome passivamente
- sede passivamente
- sede adicional ao se mover
- sede adicional ao atacar

### Fontes de alimentacao
O pet pode recuperar recursos de:
- itens configurados dentro da bag do pet
- itens dropados no chao perto do pet

### Prioridade de consumo
A alimentacao do chao e checada antes da bag.

Ordem efetiva:
1. itens de comida/agua proximos no chao
2. caso contrario, recursos da bag se ela estiver equipada e os thresholds forem atingidos

Isso significa que o pet prefere consumir recursos do mundo antes de gastar o estoque guardado.

## Bag System
### Equip requirement
The pet bag requires:
- `horse.saddlebag`

Without that item equipped on the pet, the bag commands are blocked.

### What the bag stores
The bag only supports items present in the configured restore maps:
- `FoodRestore`
- `WaterRestore`

This makes the bag a curated pet supply inventory, not a generic backpack.

### UI model
The bag UI is implemented using a temporary hidden `StorageContainer`.

Flow:
1. create an off-world container
2. sync persistent bag contents into it
3. open the Rust loot panel for the player
4. sync contents back on close
5. destroy the temporary entity

## Runtime Safety and Recovery
### Why recovery exists
Rust animal prefabs are not naturally built to act like deterministic companion pets under full plugin control.

The plugin therefore includes recovery logic to deal with:
- getting stuck
- path blockage
- drift from the expected destination
- excessive owner distance

### Recovery escalation
When movement appears stalled, the system escalates through:
1. local path refresh
2. lateral reposition
3. safe teleport near the owner

### Safe teleport constraints
Safe teleport is blocked when:
- the pet currently has a target
- the pet was recently in combat
- nearby hostile players are too close to the owner

## Native AI Suppression
The plugin tries to disable the prefab's wild NPC decision-making and replace it with plugin-driven logic.

It does that by:
- disabling behavior components that look like brains or navigators
- invoking stop methods when found
- clearing attack targets and transforms
- reapplying this suppression during updates if configured

This is one of the most powerful but also most fragile parts of the system.

If Rust runtime internals change, symptoms can include:
- random pathing drift
- spontaneous hostile behavior
- inconsistent movement ownership between plugin and prefab runtime

## Data and Persistence
Persisted today:
- ally links
- progression data
- bag contents
- bag equipped state

Not persisted today:
- active pet entities
- current target
- current guard position
- exact current runtime state

Practical consequence:
- pets are not durable across reloads or restarts
- this is safe operationally, but it is a feature limitation to be aware of

## Operational Validation
### Source of truth
The authoritative validation source is:
- `carbon/logs/Carbon.Core.log`

This is important because:
- editor diagnostics may show no errors while a Carbon hot-reload still fails
- during iterative edits, the log may contain transient compile failures before the final successful load

A change should only be considered live when the latest relevant log entry shows a successful plugin load.

### Basic smoke test after edits
Recommended smoke test:
1. reload the plugin
2. spawn a pet
3. confirm follow works
4. confirm `/pet status` prints the expected sections
5. confirm target preview appears when aiming
6. confirm `/pet attack` starts combat correctly
7. confirm bag open/add/take works if equipped
8. confirm food or water on the ground is consumed when thresholds are reached

## Current Limitations
Known current constraints:
- some registered pet aliases still fail to spawn in runtime
- active pets are not restart-persistent
- the implementation is still monolithic in a single source file
- native AI suppression is reflection-driven and sensitive to upstream changes
- aggressive auto-targeting is currently narrower than the manual attack path

## Recommended Engineering Direction
Short-term:
- validate or remove unsupported spawn aliases
- keep the Carbon log in the deploy checklist
- maintain a focused smoke-test script for pet changes

Mid-term:
- rename and migrate the persistence file to reflect its actual scope
- split the plugin into smaller source units by concern
- add runtime observability around native AI suppression per prefab family

Long-term:
- decide whether pets should survive restarts
- formalize profile capability validation so unsupported animals are never exposed to players by default
