# MarolaPets Audit

## Scope
This audit covers the current implementation of `MarolaPets` in `/home/rustserver/rustserver/carbon/plugins/MarolaPets.cs`, plugin version `0.2.0`, and the latest runtime evidence from `/home/rustserver/rustserver/carbon/logs/Carbon.Core.log`.

## Executive Summary
`MarolaPets` is a single-file Carbon/Oxide Rust plugin that implements companion pets with custom movement, combat, target acquisition, ally protection, bag storage, auto-feeding, world HUD, and a progression system for speed, attack, defense, and vitality.

The plugin is functional and currently hot-reloads cleanly, but it carries several engineering risks:
- unsupported spawn profiles are still registered for production use
- native AI suppression depends on brittle runtime reflection
- persistence storage is broader than its file naming suggests
- the implementation is concentrated in a large monolithic source file

## Runtime Validation
Latest confirmed clean runtime load:
- `2026.05.31 06:14:07 Loaded plugin MarolaPets v0.2.0`

Important runtime history from the same log:
- hot-reload transient compile failures occurred during iterative edits around `06:10-06:11`
- these failures were resolved, and the latest saved source loaded successfully

Operational rule:
- editor diagnostics alone are not authoritative for this plugin
- the Carbon log must be treated as the source of truth for final compile/load state

## Findings

### 1. Registered pet types do not all spawn successfully
Severity: High

Evidence:
- `BuildProfiles()` registers `tiger/tigre` and `crocodile/crocodilo/jacare`
- runtime log previously recorded `Failed to create pet 'tigre'...` and `Failed to create pet 'jacare'...`

Impact:
- the command surface advertises pets that are not reliably spawnable
- this creates user-facing failures and makes support/load testing noisy

Recommendation:
- gate unsupported profiles behind a feature flag or remove them from `BuildProfiles()` until prefab paths are validated in runtime
- add a startup validation pass that attempts a safe prefab existence check and logs a capability report

### 2. Native AI suppression is reflection-driven and fragile across Rust updates
Severity: High

Evidence:
- `SuppressNativeAi()` disables components by type-name pattern matching such as `Brain`, `FSM`, and `Navigator`
- it also writes internal members such as `AttackTarget`, `AttackTransform`, `sleeping`, and `lastWarpTime`

Impact:
- game updates that rename runtime types or private members can silently weaken control of pets
- failures here will appear as desync, random aggression, or movement drift rather than a compile-time break

Recommendation:
- isolate suppression into a dedicated compatibility layer with explicit telemetry
- add a startup/runtime health metric that counts successful suppression actions per prefab family
- consider per-prefab adapters instead of generic name-matching if this plugin remains strategic

### 3. Persistence file naming no longer matches persisted scope
Severity: Medium

Evidence:
- `LoadData()` and `SaveData()` use `MarolaPets/ally_data`
- `StoredData` currently contains allies, training progress, bag inventory, and bag equipment state

Impact:
- maintenance and migration work become error-prone because the storage name implies only ally data
- ops engineers may underestimate the blast radius of resets or edits to the data file

Recommendation:
- rename the persisted file to something like `MarolaPets/data`
- if backward compatibility matters, add a migration path from `ally_data`

### 4. Aggressive auto-targeting is narrower than the full combat model
Severity: Medium

Evidence:
- manual `/pet attack` can target `BaseCombatEntity` via `FindLookTarget()`
- automatic aggressive acquisition uses `TryAcquireAggressiveTarget()` -> `FindNearestHostilePlayer()`

Impact:
- aggressive mode is effectively player-centric, while the rest of the combat surface suggests a broader hostile-entity model
- this can confuse future maintainers and designers during balancing or QA

Recommendation:
- document this as an intentional design constraint, or generalize aggressive acquisition to NPCs/animals with explicit filtering rules

### 5. Active pet runtime state is intentionally non-persistent
Severity: Medium

Evidence:
- only allies, progress, bag contents, and bag equipped state are persisted
- `Unload()` dismisses active pets and `OnPlayerDisconnected()` removes orphan pets

Impact:
- server restarts and plugin reloads remove all active pets
- this is safe operationally, but it is a product limitation that engineering and operations need to account for

Recommendation:
- if restart persistence is desired, define a new persisted `ActivePetSnapshot` model with prefab, owner, vitals, and last known position
- if not desired, keep this behavior and document it as a non-goal

### 6. Source concentration increases change risk
Severity: Medium

Evidence:
- the entire system lives in a single plugin file with lifecycle, persistence, command routing, movement, combat, targeting, UI, inventory, and formulas together

Impact:
- hot-reload edits are more likely to create transient broken states
- review, testability, and ownership boundaries are weak

Recommendation:
- split the code into partial classes or separate internal modules by concern: lifecycle/config, runtime state, movement/combat, inventory, targeting/UI, persistence

### 7. Virtual bag container uses an off-world spawned entity
Severity: Low

Evidence:
- bag UI uses `coffinstorage.prefab` spawned at `y = -500`
- container lifecycle is cleaned up on close and unload

Impact:
- this is a pragmatic pattern, but it depends on reliable cleanup and may be surprising for ops/debug tooling

Recommendation:
- keep it if UX is acceptable, but document the container lifecycle and consider explicit metrics for open/close/cleanup counts

## Positive Engineering Notes
- Config normalization is robust and clamps unsafe values before saving.
- AI LOD is explicit and reduces unnecessary full-think work at distance.
- Friendly-fire protection covers both owners and allied pets.
- Runtime hooks are exposed for integration: `OnPetSpawned`, `OnPetDismissed`, `OnPetAttackStart`, `OnPetAttackStop`, `OnPetDeath`.
- The current HUD simplification is cleaner: world-space text is now restricted to `Name | Lv` and full stats were moved to `/pet status`.

## Operational Recommendations
1. Remove or disable unsupported tiger/crocodile profiles before wider release.
2. Add a lightweight smoke-test checklist after every hot-reload: spawn, follow, attack, bag, auto-feed, `/pet status`.
3. Treat `/home/rustserver/rustserver/carbon/logs/Carbon.Core.log` as mandatory post-deploy validation.
4. Rename and migrate the persistence file before adding more persisted systems.
5. Plan a refactor pass to split the plugin into smaller ownership domains before the next large feature wave.

## Release Readiness
Current state: Conditionally releasable

Rationale:
- core runtime is loading and the feature set is coherent
- however, advertised unsupported pet types and the brittle native-AI suppression model make this unsuitable for a high-confidence production handoff without follow-up hardening
