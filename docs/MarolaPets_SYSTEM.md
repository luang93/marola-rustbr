# MarolaPets System Guide

## Purpose
This document is the dedicated functional guide for the `MarolaPets` system.

It is meant to explain, in one place, what the pet system does, how players interact with it, how the runtime behaves, which files matter, and what engineering and operations should expect from the current implementation.

For lower-level engineering detail, see:
- `docs/MarolaPets_ENGINEERING.md`
- `docs/MarolaPets_AUDIT.md`

## System Summary
`MarolaPets` is a custom Carbon/Oxide plugin that turns Rust NPC animals into player-owned companion pets.

The system currently includes:
- pet spawn and dismissal
- follow, stay, guard, and recall states
- manual attack orders based on mouse aim
- aggressive behavior with ally protection
- 3D floating identification over the pet
- 3D target preview while aiming
- pet bag and item storage
- food and water consumption from bag or from dropped items on the ground
- progression in speed, attack, defense, and vitality
- runtime and debug commands for support and balancing

Main plugin file:
- `carbon/plugins/MarolaPets.cs`

## Player Experience
### What the player sees
The current in-world HUD over the pet is intentionally minimal:
- `Name | Lv X`

Detailed stats no longer stay floating above the mob. They are now shown through:
- `/pet status`

When the player aims at something and prepares an attack order, the system can show:
- a red `ALVO: {name}` text on the selected target
- a red line from the player view to the target

### Main gameplay loop
A typical player loop is:
1. spawn a pet
2. make it follow, stay, or guard
3. use it defensively or aggressively
4. feed it from dropped food/water or use a pet bag
5. improve its level over time
6. inspect the current state with `/pet status`, `/pet debug`, or `/pet diagnose`

## Files That Matter
Core files:
- `carbon/plugins/MarolaPets.cs`
- `carbon/configs/MarolaPets.json`
- `carbon/lang/en/MarolaPets.json`

Supporting docs:
- `docs/MarolaPets_ENGINEERING.md`
- `docs/MarolaPets_AUDIT.md`

Runtime validation source:
- `carbon/logs/Carbon.Core.log`

## Commands
All interactions are routed through `/pet`.

### Basic commands
- `/pet help`
- `/pet spawn [tipo]`
- `/pet dismiss`
- `/pet recall`
- `/pet status`
- `/pet diagnose`
- `/pet debug`

### Control mode commands
- `/pet follow`
- `/pet stay`
- `/pet guard`
- `/pet radius <5|10|20>`

### Combat commands
- `/pet attack`
- `/pet passive`
- `/pet aggressive`

### Ally commands
- `/pet ally add [nome]`
- `/pet ally remove [nome]`
- `/pet ally list`

### Bag commands
- `/pet bag equip`
- `/pet bag remove`
- `/pet bag add [qtd]`
- `/pet bag take <item> [qtd]`
- `/pet bag ui`

## Supported Pets
The code currently registers aliases for these pet types:
- wolf, lobo
- bear, urso
- polarbear, ursopolar, urso-polar
- boar, javali
- chicken, galinha
- stag, veado
- tiger, tigre
- crocodile, crocodilo, jacare

Important operational note:
- some aliases are registered in code but still need runtime prefab validation
- tiger and crocodile-related entries have already shown spawn failures in runtime logs during previous validation rounds

From a product point of view, only pets that successfully spawn in runtime should be considered truly supported.

## Behavior Model
### Follow
The pet follows the owner using rotating offsets instead of standing directly on top of the player.

This improves:
- readability
- collision feel
- perceived natural movement

### Stay
The pet remains in place and stops trying to follow the owner.

### Guard
The pet stores a guard anchor and tries to remain within the configured radius.

Allowed guard radii today:
- `5`
- `10`
- `20`

### Recall
If the pet is too far away or lost, recall teleports it safely near the owner, subject to cooldown and recovery rules.

## Combat Model
### Manual attack
`/pet attack` uses the entity under the player aim.

Target acquisition is not just a simple center hit check. It combines:
- direct ray hit detection
- aim-cone style scoring for nearby entities
- exclusion rules for friendly targets and pet-owned entities

### Aggressive mode
In aggressive mode, the pet can auto-acquire nearby hostile players.

This is narrower than the full manual targeting path. Manual targeting can work with a broader `BaseCombatEntity` surface, while aggressive auto-acquisition currently focuses on hostile players.

### Friendly protection
The pet should not attack:
- its owner
- allied players
- pets owned by the owner or by allies

### Combat limits
Combat behavior is bounded by:
- activation range
- target commit time
- leash distance
- engage distance
- cooldown between hits

## HUD and Status
### In-world HUD
The floating HUD is deliberately minimal:
- pet display name
- aggregate level

Reason:
- large multiline `ddraw.text` blocks were visually poor and unstable in practice
- detailed numbers now live in chat where they are easier to read and maintain

### `/pet status`
`/pet status` is now the main stats surface.

It reports:
- pet name and aggregate level
- current hunger and hunger capacity
- current thirst and thirst capacity
- current effective run speed and sprint speed
- current effective damage
- current effective attack cadence
- current defense level and reduction

## Progression Model
The pet has four tracked training dimensions.

### Speed
How it levels:
- gains XP from movement distance

What it changes:
- effective movement speed
- follow/run/sprint responsiveness

### Attack
How it levels:
- gains XP from successful attacks

What it changes:
- damage
- hit cadence

### Defense
How it levels:
- gains XP from damage received

What it changes:
- incoming damage reduction

### Vitality
How it levels:
- gains XP from survival time over updates

What it changes:
- hunger capacity
- thirst capacity

### Aggregate level
The level shown above the pet is not a separate XP bar.

It is an aggregate display built from the sum of the positive deltas of:
- speed level
- attack level
- defense level
- vitality level

## Hunger, Thirst, and Feeding
The system tracks two long-lived resource pools:
- hunger
- thirst

Implementation note:
- internally, thirst still uses the `Stamina` field name in code
- user-facing messaging now treats it as thirst/energy

### Resource drain
Over time the pet loses:
- hunger passively
- thirst passively
- additional thirst while moving
- additional thirst when attacking

### Feeding sources
The pet can recover resources from:
- configured items inside the pet bag
- dropped items found on the ground near the pet

### Feed priority
Ground feeding is checked before bag consumption.

Effective order:
1. nearby dropped food/water
2. otherwise bag resources if the bag is equipped and thresholds are crossed

This means the pet prefers nearby world resources before spending its stored supplies.

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
