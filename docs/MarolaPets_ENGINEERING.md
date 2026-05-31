# MarolaPets Engineering Documentation

## 1. Overview
`MarolaPets` is a Carbon/Oxide `RustPlugin` located at `/home/rustserver/rustserver/carbon/plugins/MarolaPets.cs`.

Current plugin metadata:
- Name: `MarolaPets`
- Version: `0.2.0`
- Base type: `RustPlugin`
- Main player command: `/pet`
- Required permission: `marolapets.use`

The plugin provides a companion-pet runtime with these main capabilities:
- spawning supported pet profiles
- custom follow/stay/guard/attack behavior
- friendly and ally-safe combat rules
- mouse-aim target selection for `/pet attack`
- world-space HUD and attack target preview
- pet bag storage gated by `horse.saddlebag`
- auto-feeding from bag or dropped items on the ground
- level-based progression for speed, attack, defense, and vitality

## 2. Runtime Architecture
The plugin is implemented as one source file but internally split into focused runtime modules:
- `CompanionBrain`: top-level state dispatcher
- `CompanionMovement`: follow/stay/guard movement and positioning
- `CompanionCombat`: target pursuit, leash rules, hit cadence, attack start
- `CompanionRecovery`: stuck detection, lateral reposition, safe teleport recovery
- `CompanionPhysics`: ground resolution, local obstacle avoidance, water-aware placement

Module wiring happens in `InitializeModules()` after config/data/profile loading.

### Core runtime models
- `PetState`: in-memory active state for one spawned pet
- `PetProgress`: persistent progression levels and XP
- `StoredData`: persistent allies, progress, bag content, and bag equipment state
- `PetProfile`: supported spawnable pet definition
- `ThreatInfo`: active combat target bookkeeping

### Key registries
- `_pets`: active pet state by owner id
- `_petOwnersByEntity`: reverse lookup from spawned entity to owner
- `_bagContainersByOwner`: active temporary loot container per owner
- `_bagOwnersByContainer`: reverse lookup for open bag UIs
- `_profiles`: spawnable pet definitions by alias/name

## 3. Lifecycle
### `Init()`
Responsibilities:
- register permission
- load and normalize config
- load stored data
- register pet profiles
- initialize runtime modules
- register `/pet` chat command

### `OnServerInitialized()`
Starts the scheduler timer with the minimum think cadence returned by `GetSchedulerInterval()`.

### `Unload()`
Responsibilities:
- destroy update timer
- persist data
- close any open bag containers
- dismiss all active pets created by this plugin

### `OnServerSave()`
Persists `StoredData`.

### `OnPlayerDisconnected()`
Removes active pets for disconnecting owners to prevent orphan runtime entities.

## 4. Persistence Model
Persistence uses `DynamicConfigFile` via:
- path: `MarolaPets/ally_data`

Persisted structures:
- `AlliesByOwner`
- `ProgressByOwner`
- `BagByOwner`
- `BagEquippedByOwner`

Not persisted:
- active pet runtime entities
- current combat target
- current guard anchor
- current temporary HUD state
- current AI tier/runtime suppression state

Operationally, active pets are ephemeral and disappear on reload/restart.

## 5. Supported Pet Profiles
Profiles are registered in `BuildProfiles()`.

Aliases currently mapped:
- wolf, lobo
- bear, urso
- polarbear, ursopolar, urso-polar
- boar, javali
- chicken, galinha
- stag, veado
- tiger, tigre
- crocodile, crocodilo, jacare

Profile fields:
- `Key`
- `DisplayName`
- `Prefab`
- `CanSwim`

Important runtime note:
- tiger and crocodile aliases are registered in code, but previous runtime logs showed spawn failures for those prefabs

## 6. Command Surface
All player interaction is routed through `CmdPet()`.

Supported subcommands:
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

### Command semantics
- `follow`: pet tracks an offset around the owner
- `stay`: pet stops in place
- `guard`: pet anchors to the current location and patrol radius rules
- `attack`: sends the pet against the current mouse-aimed target
- `status`: user-facing stat report for progression and vitals
- `diagnose`: engineering/runtime diagnostics
- `debug`: movement/combat internals

## 7. AI Loop and Scheduling
The central runtime loop is `UpdatePets()`.

Per active pet, the loop:
1. validates owner and pet entity
2. reapplies native-AI suppression if configured
3. computes owner distance and AI LOD tier
4. throttles work using `NextThinkTime`
5. updates vitals and progression
6. optionally auto-acquires a target in aggressive mode
7. delegates behavior to `CompanionBrain`
8. draws HUD and target preview

### AI LOD tiers
- `Full`
- `Simplified`
- `Sleeping`

Current defaults:
- full range: `30m`
- simplified range: `80m`
- full think interval: `0.1s`
- simplified think interval: `0.25s`
- sleeping think interval: `1.0s`

Behavior notes:
- if a pet has a target, it is forced into `Full`
- sleeping pets still draw minimal HUD but skip heavy logic while target-less

## 8. Movement Model
Movement is plugin-driven, not native-AI-driven.

### Follow behavior
`CompanionMovement.UpdateFollow()`:
- uses rotating side/rear offsets around the owner
- preserves a minimum owner distance
- moves through three speed bands: walk, run, sprint
- applies smooth acceleration/deceleration through `SmoothedVelocity`

### Guard/Stay behavior
`CompanionMovement.UpdateStay()`:
- anchors to `GuardPosition`
- allows guard radius when state is `Guard`
- otherwise behaves like a hold position with stop distance

### Recovery behavior
`CompanionRecovery.TryRecover()` escalates in stages:
1. local path refresh
2. lateral reposition
3. safe teleport near owner if distance and combat safety allow it

### Physics behavior
`CompanionPhysics` provides:
- terrain/water-aware grounding
- sphere-cast local avoidance
- alternate left/right path refresh when forward path is blocked

## 9. Combat Model
Combat is fully managed by `CompanionCombat`.

### Start conditions
`TryStartAttack()` validates:
- pet state and owner exist
- attacker is a valid combat target
- target is not friendly
- target is within activation range

### Pursuit and leash
`UpdateCombat()`:
- clears target if invalid or friendly
- clears target if unseen longer than `TargetCommitTime`
- clears target if owner-to-target distance exceeds `LeashDistance`
- moves toward grounded target position until engage distance is reached

### Strike path
When in range:
- rotate to target
- respect `NextAttackTime`
- apply `GetPetAttackDamage(state)` as slash damage
- update threat timestamp
- register attack training XP

### Friendly rules
Friendly checks cover:
- owner
- allied players
- other pets owned by the owner or an ally

## 10. Target Selection and UI Feedback
### Manual attack targeting
`FindLookTarget()` implements mouse-aim selection:
- first tries direct raycast using `Physics.DefaultRaycastLayers`
- falls back to nearby `BaseCombatEntity` scoring around the aim ray
- excludes self, allied players, and pet entities

`FindLookPlayer()` handles aimed player selection with a tighter cone.

### Attack preview
`TryDrawAttackTargetPreview()` draws:
- red text above the current selected target: `ALVO: {nome}`
- red line from player eyes to target aim point

### Pet world HUD
`TryDrawPetWorldUi()` now intentionally renders only:
- `Nome | Lv X`

Detailed runtime stats were intentionally removed from the floating HUD and moved into `/pet status`.

## 11. Vitals and Progression
The system currently tracks two consumable reserves:
- hunger, stored in `PetState.Hunger`
- thirst/energy, stored in `PetState.Stamina`

The name `Stamina` remains in code, but user-facing semantics now represent thirst/energy.

### Drain model
Per vitals update:
- hunger decreases over time
- thirst decreases over time
- additional thirst is consumed while moving
- additional thirst is consumed per attack

### Training dimensions
- `Speed`
- `Attack`
- `Defense`
- `Vitality`

### Level effects
#### Speed
- XP source: distance traveled
- effect: movement speed multiplier via `GetPetMoveSpeed()`

#### Attack
- XP source: successful attacks
- effects:
  - damage multiplier via `GetPetAttackDamage()`
  - attack cadence reduction via `GetPetAttackCooldown()`

#### Defense
- XP source: damage taken
- effect: incoming damage reduction via `GetPetDefenseReduction()`

#### Vitality
- XP source: elapsed survival time
- effects:
  - increases max hunger capacity
  - increases max thirst capacity

### Aggregate pet level
Floating HUD level is computed as:
- base `1`
- plus the positive deltas of `SpeedLevel`, `AttackLevel`, `DefenseLevel`, and `VitalityLevel`

This is an aggregate display level, not an independent XP track.

## 12. `/pet status` Output Model
`/pet status` is now the primary player-facing stat surface.

Current sections:
- header: pet name and aggregate level
- vitals: hunger and thirst current/max and percent
- speed: speed level, effective run speed, effective sprint speed
- combat: attack level, effective damage, effective cooldown, defense level, defense reduction percent

This is intentionally chat-based rather than world-space to keep the in-world HUD clean.

## 13. Bag and Feeding System
### Equipment gate
Pet bag use requires a `horse.saddlebag` item.

### UI implementation
The bag UI is implemented via a temporary `StorageContainer` created from:
- `assets/prefabs/misc/halloween/coffin/coffinstorage.prefab`

Container lifecycle:
- spawn hidden/off-world
- sync stored bag contents into the container
- open Rust loot panel for the player
- on close, sync container contents back into persistent bag state
- clean up the temporary entity

### Supported resources
The bag accepts only items present in configured restore maps:
- `Inventory.FoodRestore`
- `Inventory.WaterRestore`

### Auto-consumption order
`TryAutoConsumeFromBag()` first calls `TryAutoConsumeFromGround()`.

Effective priority is:
1. consume dropped food/water from the ground if available
2. otherwise consume from the equipped bag if thresholds are crossed

This means ground-fed resources take precedence over bag resources.

## 14. Native AI Suppression
The plugin attempts to make itself the sole decision-maker for pet behavior.

Mechanism:
- scans runtime components on the spawned NPC
- disables components whose type names match `Brain`, `FSM`, or `Navigator`
- invokes stop/disable methods when available
- clears hostile target references when configured

Engineering note:
- this is functional but update-sensitive because it depends on runtime reflection and internal member names

## 15. Hooks and Integration Points
The plugin emits these notable hooks:
- `OnPetSpawned`
- `OnPetDismissed`
- `OnPetAttackStart`
- `OnPetAttackStop`
- `OnPetDeath`

It also checks `CanLootEntity` before opening the bag UI container.

## 16. Config Domains
`PluginConfig` is grouped into these domains:
- `Movement`
- `Combat`
- `AiLod`
- `NativeAi`
- `Recall`
- `Ui`
- `Training`
- `Inventory`
- `Recovery`

Config loading is normalized and clamped in `LoadConfigValues()` before being written back to disk.

This means invalid or partial configs are coerced into valid ranges on load.

## 17. Operational Validation
Required validation source:
- `/home/rustserver/rustserver/carbon/logs/Carbon.Core.log`

Do not rely only on editor diagnostics or transient intermediate reload states.

A reload should be considered valid only when the latest log entry shows:
- `Loaded plugin MarolaPets v0.2.0`

Current observed latest clean load:
- `2026.05.31 06:14:07`

## 18. Known Limitations
- Tiger and crocodile-related aliases are registered, but runtime spawn success is not yet reliable.
- Active pet entities are not persisted across restart/reload.
- The plugin is a monolithic single-file implementation.
- Native-AI suppression is brittle against upstream Rust runtime changes.
- Aggressive auto-targeting currently focuses on hostile players, not the full set of combat entities.

## 19. Recommended Next Engineering Steps
1. Validate or remove unsupported profile prefabs.
2. Rename and migrate the persistence file from `ally_data` to a generic plugin data file.
3. Split the plugin into smaller source units by domain.
4. Add an explicit smoke-test checklist for spawn, follow, attack, bag, feeding, and status output.
5. Add structured runtime logging for native-AI suppression success/failure by prefab type.
