using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Oxide.Core;
using Oxide.Core.Configuration;
using Rust;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("MarolaPets", "GitHub Copilot", "0.2.0")]
    [Description("MVP companion pet system for Marola Rust BR.")]
    public class MarolaPets : RustPlugin
    {
        // Permissao base para todos os comandos expostos ao jogador neste plugin.
        private const string UsePermission = "marolapets.use";
        private const string BagContainerPrefab = "assets/prefabs/misc/halloween/coffin/coffinstorage.prefab";
        private const string BagLootPanel = "generic_resizable";
        private const string BagEquipItemShortname = "horse.saddlebag";

        // Estado em runtime: pets ativos por dono, busca reversa por entidade e definicoes de pets que podem ser spawnados.
        private readonly Dictionary<ulong, PetState> _pets = new Dictionary<ulong, PetState>();
        private readonly Dictionary<BaseCombatEntity, ulong> _petOwnersByEntity = new Dictionary<BaseCombatEntity, ulong>();
        private readonly Dictionary<ulong, StorageContainer> _bagContainersByOwner = new Dictionary<ulong, StorageContainer>();
        private readonly Dictionary<StorageContainer, ulong> _bagOwnersByContainer = new Dictionary<StorageContainer, ulong>();
        private readonly Dictionary<string, PetProfile> _profiles = new Dictionary<string, PetProfile>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ulong> _ownerBuffer = new List<ulong>();
        private readonly List<BaseEntity> _entityScanBuffer = new List<BaseEntity>();
        private readonly List<BasePlayer> _playerScanBuffer = new List<BasePlayer>();

        // Configuracao carregada e timer principal que dirige o comportamento de follow/combat.
        private PluginConfig _config;
        private Timer _updateTimer;
        private DynamicConfigFile _dataFile;
        private StoredData _storedData;
        private ICompanionBrain _brain;
        private ICompanionMovement _movement;
        private ICompanionCombat _combat;
        private ICompanionRecovery _recovery;
        private CompanionPhysics _physics;

        // Estados deterministas expostos pelo runtime do companion.
        private enum CompanionState
        {
            Idle,
            Follow,
            Stay,
            Guard,
            Attack,
            Mounted,
            Dead
        }

        private enum PetAggression
        {
            Passive,
            Aggressive
        }

        private enum PetAiTier
        {
            Full,
            Simplified,
            Sleeping
        }

        private enum PetTrainingType
        {
            Speed,
            Attack,
            Defense,
            Vitality
        }

        private class PetProgress
        {
            public int SpeedLevel = 1;
            public float SpeedXp;
            public int AttackLevel = 1;
            public float AttackXp;
            public int DefenseLevel = 1;
            public float DefenseXp;
            public int VitalityLevel = 1;
            public float VitalityXp;
        }

        private class PetBagEntry
        {
            public string Shortname;
            public int Amount;
        }

        private class StoredData
        {
            public Dictionary<ulong, List<ulong>> AlliesByOwner = new Dictionary<ulong, List<ulong>>();
            public Dictionary<ulong, PetProgress> ProgressByOwner = new Dictionary<ulong, PetProgress>();
            public Dictionary<ulong, List<PetBagEntry>> BagByOwner = new Dictionary<ulong, List<PetBagEntry>>();
            public Dictionary<ulong, bool> BagEquippedByOwner = new Dictionary<ulong, bool>();
        }

        // Definicao estatica de um tipo de pet que pode ser spawnado por comando.
        private class PetProfile
        {
            public string Key;
            public string DisplayName;
            public string Prefab;
            public bool CanSwim;
        }

        private class ThreatInfo
        {
            public uint TargetId;
            public float ThreatValue;
            public float LastSeenTime;
        }

        // Estado em runtime do pet ativo de cada dono.
        private class PetState
        {
            public ulong OwnerId;
            public string PetType;
            public BaseNpc Entity;
            public CompanionState State;
            public PetAggression Aggression;
            public BaseCombatEntity Target;
            public Vector3 GuardPosition;
            public float GuardRadius;
            public Vector3 LastKnownOwnerPosition;
            public Vector3 LastNetworkPosition;
            public Vector3 PreviousPosition;
            public Vector3 SmoothedVelocity;
            public Vector3 LastResolvedDestination;
            public int FollowOffsetIndex;
            public float LastOffsetSwapTime;
            public float NextAttackTime;
            public float LastRecallTime;
            public float NextThinkTime;
            public float LastCombatTime;
            public float StuckSinceTime;
            public float LastRecoveryTime;
            public float LastVitalsUpdateTime;
            public float LastBagConsumeTime;
            public float Hunger;
            public float Stamina;
            public PetAiTier AiTier;
            public int RecoveryStage;
            public bool NativeAiSuppressed;
            public float LastNativeAiCheckTime;
            public string LastNativeAiReport;
            public float LastUiDrawTime;
            public ThreatInfo Threat = new ThreatInfo();
        }

        // Valores ajustaveis separados por movimento, combate e recall para balancear o comportamento sem mexer na logica.
        private class PluginConfig
        {
            public MovementConfig Movement = new MovementConfig();
            public CombatConfig Combat = new CombatConfig();
            public AiLodConfig AiLod = new AiLodConfig();
            public NativeAiConfig NativeAi = new NativeAiConfig();
            public RecallConfig Recall = new RecallConfig();
            public UiConfig Ui = new UiConfig();
            public TrainingConfig Training = new TrainingConfig();
            public InventoryConfig Inventory = new InventoryConfig();

            // Controla com que frequencia os pets atualizam, quao perto seguem e quando passam a ser considerados perdidos.
            public class MovementConfig
            {
                public float TickInterval = 0.1f;
                public float IdleDistance = 2f;
                public float WalkDistance = 8f;
                public float RunDistance = 15f;
                public float SprintDistance = 50f;
                public float MinOwnerDistance = 2f;
                public float SideOffset = 2f;
                public float RearOffset = 2.6f;
                public float DefaultGuardRadius = 10f;
                public float StopDistance = 1.4f;
                public float WalkSpeed = 2.5f;
                public float RunSpeed = 4.9f;
                public float SprintSpeed = 7.2f;
                public float Acceleration = 8f;
                public float Deceleration = 10f;
                public float LostDistance = 60f;
                public float OwnerMoveThreshold = 0.2f;
                public float NetworkMinMoveDistance = 0.1f;
                public float TurnSpeed = 220f;
                public float OffsetRotateInterval = 2.75f;
                public float LocalAvoidanceDistance = 1.35f;
                public float PathRefreshDistance = 2.25f;
            }

            // Controla distancia de aquisicao de alvo, limite de afastamento, alcance de ataque e cadencia entre golpes.
            public class CombatConfig
            {
                public float ActivationRange = 20f;
                public float LeashDistance = 30f;
                public float EngageDistance = 2.2f;
                public float MoveSpeed = 5.8f;
                public float Damage = 20f;
                public float Cooldown = 0.95f;
                public float TargetCommitTime = 4f;
            }

            // LOD da IA: perto pensa mais, longe simplifica e muito longe entra em sleep com checagens esporadicas.
            public class AiLodConfig
            {
                public float FullRange = 30f;
                public float SimplifiedRange = 80f;
                public float FullThinkInterval = 0.1f;
                public float SimplifiedThinkInterval = 0.25f;
                public float SleepingThinkInterval = 1f;
            }

            // Neutraliza o runtime selvagem do prefab para que o plugin seja a unica fonte de decisao do pet.
            public class NativeAiConfig
            {
                public bool DisableBrain = true;
                public bool DisableNavigator = true;
                public bool ClearHostileTargets = true;
                public bool ReapplyEveryThink = true;
            }

            // Controla o recall manual do pet, incluindo cooldown e distancia de aparicao ao lado do jogador.
            public class RecallConfig
            {
                public float Cooldown = 8f;
                public float SpawnOffset = 2f;
            }

            public class UiConfig
            {
                public bool Enabled = true;
                public float DrawInterval = 1f;
                public float CompactDistance = 20f;
                public float MaxOwnerDistance = 35f;
                public float VerticalOffset = 1.9f;
            }

            public class TrainingConfig
            {
                public float HungerDrainPerMinute = 1.5f;
                public float ThirstDrainPerMinute = 1.2f;
                public float StaminaDrainMovePerSecond = 2.6f;
                public float StaminaDrainAttack = 5.5f;
                public float LowHungerThreshold = 25f;
                public float SpeedXpPerMeter = 0.55f;
                public float SpeedXpPerLevel = 180f;
                public float SpeedBonusPerLevel = 0.065f;
                public float AttackXpPerHit = 18f;
                public float AttackXpPerLevel = 140f;
                public float AttackBonusPerLevel = 0.08f;
                public float AttackSpeedBonusPerLevel = 0.03f;
                public float DefenseXpPerDamage = 1.35f;
                public float DefenseXpPerLevel = 160f;
                public float DefenseBonusPerLevel = 0.045f;
                public float VitalityXpPerMinute = 1.2f;
                public float VitalityXpPerLevel = 120f;
                public float CapacityBonusPerLevel = 12f;
            }

            public class InventoryConfig
            {
                public int Capacity = 8;
                public float AutoConsumeCooldown = 8f;
                public float AutoEatThreshold = 55f;
                public float AutoDrinkThreshold = 30f;
                public float GroundFeedRadius = 2.5f;
                public Dictionary<string, float> FoodRestore = new Dictionary<string, float>
                {
                    ["apple"] = 12f,
                    ["black.raspberries"] = 8f,
                    ["blueberries"] = 8f,
                    ["can.beans"] = 24f,
                    ["can.tuna"] = 18f,
                    ["chicken.cooked"] = 22f,
                    ["corn"] = 10f,
                    ["fish.cooked"] = 24f,
                    ["granolabar"] = 14f,
                    ["humanmeat.cooked"] = 16f,
                    ["mushroom"] = 8f,
                    ["pork.cooked"] = 24f,
                    ["pumpkin"] = 14f
                };
                public Dictionary<string, float> WaterRestore = new Dictionary<string, float>
                {
                    ["smallwaterbottle"] = 18f,
                    ["waterjug"] = 35f
                };
            }

            public RecoveryConfig Recovery = new RecoveryConfig();

            public class RecoveryConfig
            {
                public float StuckDeltaThreshold = 0.2f;
                public float StuckDuration = 2.5f;
                public float LateralRepositionDistance = 2.5f;
                public float SafeTeleportDistance = 50f;
                public float NearbyEnemyRange = 12f;
                public float CombatBlockDuration = 10f;
                public float RetryCooldown = 0.75f;
            }
        }

        private interface ICompanionBrain
        {
            void Update(BasePlayer owner, PetState state, float ownerDistance, float thinkInterval);
        }

        private interface ICompanionMovement
        {
            void UpdateFollow(BasePlayer owner, PetState state, float ownerDistance, float thinkInterval);
            void UpdateStay(BasePlayer owner, PetState state, float thinkInterval);
            void MoveTowards(PetState state, Vector3 destination, float speed, float thinkInterval, bool forceNetworkUpdate);
            void Teleport(PetState state, Vector3 destination, bool forceNetworkUpdate);
            void RotateTowards(BaseNpc entity, Vector3 destination, bool forceNetworkUpdate, float thinkInterval);
            Vector3 GetFollowAnchor(BasePlayer owner, PetState state);
            Vector3 GetRecallPosition(BasePlayer owner);
            Vector3 GetAnchorPosition(BasePlayer owner, PetState state);
            string BuildDiagnosisAssessment(PetState state, float ownerDistance, float anchorDistance);
        }

        private interface ICompanionCombat
        {
            void UpdateCombat(BasePlayer owner, PetState state, float thinkInterval);
            bool TryStartAttack(BasePlayer owner, PetState state, BaseCombatEntity attacker, bool ownerTriggered);
        }

        private interface ICompanionRecovery
        {
            void Reset(PetState state);
            bool TryRecover(BasePlayer owner, PetState state, Vector3 desiredDestination, float ownerDistance);
        }

        private sealed class CompanionBrain : ICompanionBrain
        {
            private readonly MarolaPets _plugin;

            public CompanionBrain(MarolaPets plugin)
            {
                _plugin = plugin;
            }

            public void Update(BasePlayer owner, PetState state, float ownerDistance, float thinkInterval)
            {
                if (state.Target != null)
                {
                    _plugin._combat.UpdateCombat(owner, state, thinkInterval);
                    return;
                }

                switch (state.State)
                {
                    case CompanionState.Stay:
                    case CompanionState.Guard:
                        _plugin._movement.UpdateStay(owner, state, thinkInterval);
                        return;
                    case CompanionState.Mounted:
                    case CompanionState.Dead:
                        return;
                    default:
                        _plugin._movement.UpdateFollow(owner, state, ownerDistance, thinkInterval);
                        return;
                }
            }
        }

        private sealed class CompanionPhysics
        {
            private readonly MarolaPets _plugin;

            public CompanionPhysics(MarolaPets plugin)
            {
                _plugin = plugin;
            }

            public Vector3 GetGroundedPosition(Vector3 position, bool allowWater)
            {
                var probeMask = allowWater
                    ? LayerMask.GetMask("Terrain", "World", "Construction", "Default", "Water")
                    : LayerMask.GetMask("Terrain", "World", "Construction", "Default");
                var probeStart = position + Vector3.up * 4f;
                if (Physics.Raycast(probeStart, Vector3.down, out var hit, 12f, probeMask, QueryTriggerInteraction.Ignore))
                {
                    position.y = hit.point.y + 0.08f;
                    return position;
                }

                var height = TerrainMeta.HeightMap.GetHeight(position);
                var waterHeight = TerrainMeta.WaterMap.GetHeight(position);
                var targetHeight = height + 0.25f;

                if (allowWater && waterHeight > targetHeight)
                {
                    targetHeight = waterHeight + 0.1f;
                }

                if (position.y < targetHeight)
                {
                    position.y = targetHeight;
                }

                return position;
            }

            public Vector3 ResolveMovementTarget(PetState state, Vector3 desiredTarget)
            {
                var canSwim = _plugin.CanPetSwim(state);
                var groundedTarget = GetGroundedPosition(desiredTarget, canSwim);
                var current = state.Entity.transform.position;
                var direction = groundedTarget - current;
                direction.y = 0f;

                if (direction.sqrMagnitude <= 0.01f)
                {
                    return groundedTarget;
                }

                var distance = Mathf.Min(_plugin._config.Movement.LocalAvoidanceDistance, direction.magnitude);
                var ray = new Ray(current + Vector3.up * 0.5f, direction.normalized);
                if (!Physics.SphereCast(ray, 0.35f, distance, Rust.Layers.Solid))
                {
                    return groundedTarget;
                }

                var left = Quaternion.Euler(0f, -35f, 0f) * direction.normalized;
                if (!Physics.SphereCast(new Ray(current + Vector3.up * 0.5f, left), 0.35f, distance, Rust.Layers.Solid))
                {
                    return GetGroundedPosition(current + left * _plugin._config.Movement.PathRefreshDistance, canSwim);
                }

                var right = Quaternion.Euler(0f, 35f, 0f) * direction.normalized;
                if (!Physics.SphereCast(new Ray(current + Vector3.up * 0.5f, right), 0.35f, distance, Rust.Layers.Solid))
                {
                    return GetGroundedPosition(current + right * _plugin._config.Movement.PathRefreshDistance, canSwim);
                }

                return groundedTarget;
            }
        }

        private sealed class CompanionMovement : ICompanionMovement
        {
            private static readonly Vector3[] FollowOffsetWeights =
            {
                new Vector3(-1f, 0f, 0.5f),
                new Vector3(1f, 0f, 0.5f),
                new Vector3(-1f, 0f, -1f),
                new Vector3(1f, 0f, -1f)
            };

            private readonly MarolaPets _plugin;

            public CompanionMovement(MarolaPets plugin)
            {
                _plugin = plugin;
            }

            public void UpdateFollow(BasePlayer owner, PetState state, float ownerDistance, float thinkInterval)
            {
                if (ownerDistance > _plugin._config.Movement.LostDistance)
                {
                    if (_plugin._recovery.TryRecover(owner, state, GetRecallPosition(owner), ownerDistance))
                    {
                        state.State = CompanionState.Follow;
                    }

                    return;
                }

                var destination = GetFollowAnchor(owner, state);
                state.LastResolvedDestination = destination;
                var distanceToDestination = Vector3.Distance(state.Entity.transform.position, destination);

                if (_plugin._recovery.TryRecover(owner, state, destination, ownerDistance))
                {
                    destination = GetFollowAnchor(owner, state);
                    distanceToDestination = Vector3.Distance(state.Entity.transform.position, destination);
                }

                var desiredSpeed = GetDesiredFollowSpeed(distanceToDestination);
                if (desiredSpeed <= 0.01f)
                {
                    state.State = CompanionState.Idle;
                    ApplyIdleDeceleration(state, thinkInterval);
                    RotateTowards(state.Entity, owner.transform.position, false, thinkInterval);
                    return;
                }

                state.State = CompanionState.Follow;
                MoveTowards(state, destination, desiredSpeed, thinkInterval, false);
                RotateTowards(state.Entity, owner.transform.position, false, thinkInterval);
            }

            public void UpdateStay(BasePlayer owner, PetState state, float thinkInterval)
            {
                var anchor = _plugin._physics.GetGroundedPosition(state.GuardPosition, _plugin.CanPetSwim(state));
                state.LastResolvedDestination = anchor;
                var distanceToAnchor = Vector3.Distance(state.Entity.transform.position, anchor);
                if (_plugin._recovery.TryRecover(owner, state, anchor, distanceToAnchor))
                {
                    anchor = _plugin._physics.GetGroundedPosition(state.GuardPosition, _plugin.CanPetSwim(state));
                    distanceToAnchor = Vector3.Distance(state.Entity.transform.position, anchor);
                }

                var allowedRadius = state.State == CompanionState.Guard
                    ? Mathf.Max(state.GuardRadius, _plugin._config.Movement.StopDistance)
                    : _plugin._config.Movement.StopDistance;
                if (distanceToAnchor > allowedRadius)
                {
                    MoveTowards(state, anchor, _plugin._config.Movement.WalkSpeed, thinkInterval, false);
                }
                else
                {
                    ApplyIdleDeceleration(state, thinkInterval);
                }

                RotateTowards(state.Entity, owner.transform.position, false, thinkInterval);
            }

            public void MoveTowards(PetState state, Vector3 destination, float speed, float thinkInterval, bool forceNetworkUpdate)
            {
                destination = _plugin._physics.ResolveMovementTarget(state, destination);
                var current = state.Entity.transform.position;
                var toDestination = destination - current;
                toDestination.y = 0f;
                if (toDestination.sqrMagnitude <= 0.0001f)
                {
                    ApplyIdleDeceleration(state, thinkInterval);
                    return;
                }

                var desiredVelocity = toDestination.normalized * _plugin.GetPetMoveSpeed(state, speed);
                var currentVelocity = state.SmoothedVelocity;
                var acceleration = desiredVelocity.magnitude >= currentVelocity.magnitude ? _plugin._config.Movement.Acceleration : _plugin._config.Movement.Deceleration;
                state.SmoothedVelocity = Vector3.MoveTowards(currentVelocity, desiredVelocity, acceleration * thinkInterval);

                var planarStep = state.SmoothedVelocity * thinkInterval;
                if (planarStep.magnitude > toDestination.magnitude)
                {
                    planarStep = toDestination;
                }

                Teleport(state, current + planarStep, forceNetworkUpdate);
            }

            public void Teleport(PetState state, Vector3 destination, bool forceNetworkUpdate)
            {
                destination = _plugin._physics.GetGroundedPosition(destination, _plugin.CanPetSwim(state));
                state.PreviousPosition = state.Entity.transform.position;
                state.Entity.transform.position = destination;

                if (!forceNetworkUpdate && Vector3.Distance(state.LastNetworkPosition, destination) < _plugin._config.Movement.NetworkMinMoveDistance)
                {
                    return;
                }

                state.LastNetworkPosition = destination;
                state.Entity.UpdateNetworkGroup();
                state.Entity.SendNetworkUpdateImmediate();
            }

            public void RotateTowards(BaseNpc entity, Vector3 destination, bool forceNetworkUpdate, float thinkInterval)
            {
                var direction = destination - entity.transform.position;
                direction.y = 0f;
                if (direction.sqrMagnitude <= 0.01f)
                {
                    return;
                }

                var targetRotation = Quaternion.LookRotation(direction.normalized);
                entity.transform.rotation = Quaternion.RotateTowards(entity.transform.rotation, targetRotation, _plugin._config.Movement.TurnSpeed * thinkInterval);
                if (forceNetworkUpdate)
                {
                    entity.SendNetworkUpdateImmediate();
                }
            }

            public Vector3 GetFollowAnchor(BasePlayer owner, PetState state)
            {
                var currentTime = Time.time;
                if (state.LastOffsetSwapTime <= 0f || currentTime - state.LastOffsetSwapTime >= _plugin._config.Movement.OffsetRotateInterval)
                {
                    state.FollowOffsetIndex = (state.FollowOffsetIndex + 1) % FollowOffsetWeights.Length;
                    state.LastOffsetSwapTime = currentTime;
                }

                var ownerPosition = owner.transform.position;
                var offsetWeight = FollowOffsetWeights[state.FollowOffsetIndex];
                var side = owner.eyes.BodyRight() * (_plugin._config.Movement.SideOffset * offsetWeight.x);
                var rear = -owner.eyes.BodyForward() * (_plugin._config.Movement.RearOffset * Mathf.Abs(offsetWeight.z));
                var frontBias = owner.eyes.BodyForward() * Mathf.Max(0f, offsetWeight.z) * (_plugin._config.Movement.SideOffset * 0.45f);
                var anchor = ownerPosition + side + rear + frontBias;
                var ownerOffset = anchor - ownerPosition;
                ownerOffset.y = 0f;
                if (ownerOffset.sqrMagnitude < _plugin._config.Movement.MinOwnerDistance * _plugin._config.Movement.MinOwnerDistance)
                {
                    var fallbackDirection = ownerOffset.sqrMagnitude > 0.001f ? ownerOffset.normalized : -owner.eyes.BodyForward();
                    anchor = ownerPosition + fallbackDirection * _plugin._config.Movement.MinOwnerDistance;
                }

                return _plugin._physics.GetGroundedPosition(anchor, _plugin.CanPetSwim(state));
            }

            public Vector3 GetRecallPosition(BasePlayer owner)
            {
                return _plugin._physics.GetGroundedPosition(owner.transform.position + owner.eyes.BodyRight() * _plugin._config.Recall.SpawnOffset, true);
            }

            public Vector3 GetAnchorPosition(BasePlayer owner, PetState state)
            {
                if (state.Target != null && _plugin.IsValidCombatTarget(state.Target))
                {
                    return _plugin._physics.GetGroundedPosition(state.Target.transform.position, _plugin.CanPetSwim(state));
                }

                if (state.State == CompanionState.Follow || state.State == CompanionState.Idle)
                {
                    return GetFollowAnchor(owner, state);
                }

                return _plugin._physics.GetGroundedPosition(state.GuardPosition, _plugin.CanPetSwim(state));
            }

            public string BuildDiagnosisAssessment(PetState state, float ownerDistance, float anchorDistance)
            {
                if (!state.NativeAiSuppressed)
                {
                    return "o runtime nativo ainda esta competindo com o plugin; isso continua sendo a principal causa de desync e fuga de comportamento.";
                }

                if (state.RecoveryStage > 0)
                {
                    return $"o companion acionou recovery stage {state.RecoveryStage}; isso indica stuck local ou bloqueio de rota, nao perda total de controle.";
                }

                if ((state.State == CompanionState.Follow || state.State == CompanionState.Idle) && state.Target == null && anchorDistance > _plugin._config.Movement.StopDistance * 2f)
                {
                    return "ha atraso acima do esperado no follow; o runtime foi contido e o proximo suspeito e obstaculo local, agua ou rotacao insuficiente para convergir no offset atual.";
                }

                if (ownerDistance > _plugin._config.Movement.LostDistance)
                {
                    return "o companion ultrapassou a janela segura de separacao e ficou elegivel para recovery/teleporte seguro.";
                }

                return "o companion esta sendo controlado apenas pelo plugin, com follow suave e sem sinais atuais de stuck ou drift relevante.";
            }

            private float GetDesiredFollowSpeed(float distanceToDestination)
            {
                if (distanceToDestination <= _plugin._config.Movement.IdleDistance)
                {
                    return 0f;
                }

                if (distanceToDestination <= _plugin._config.Movement.WalkDistance)
                {
                    return _plugin._config.Movement.WalkSpeed;
                }

                if (distanceToDestination <= _plugin._config.Movement.RunDistance)
                {
                    return _plugin._config.Movement.RunSpeed;
                }

                return _plugin._config.Movement.SprintSpeed;
            }

            private void ApplyIdleDeceleration(PetState state, float thinkInterval)
            {
                state.SmoothedVelocity = Vector3.MoveTowards(state.SmoothedVelocity, Vector3.zero, _plugin._config.Movement.Deceleration * thinkInterval);
            }
        }

        private sealed class CompanionCombat : ICompanionCombat
        {
            private readonly MarolaPets _plugin;

            public CompanionCombat(MarolaPets plugin)
            {
                _plugin = plugin;
            }

            public void UpdateCombat(BasePlayer owner, PetState state, float thinkInterval)
            {
                var target = state.Target;
                if (!_plugin.IsValidCombatTarget(target) || _plugin.IsFriendly(state.OwnerId, target))
                {
                    _plugin.ClearTarget(state);
                    return;
                }

                if (Time.time - state.Threat.LastSeenTime > _plugin._config.Combat.TargetCommitTime)
                {
                    _plugin.ClearTarget(state);
                    return;
                }

                var ownerDistanceToTarget = Vector3.Distance(owner.transform.position, target.transform.position);
                if (ownerDistanceToTarget > _plugin._config.Combat.LeashDistance)
                {
                    _plugin.ClearTarget(state);
                    return;
                }

                state.State = CompanionState.Attack;
                state.LastCombatTime = Time.time;

                var targetPosition = _plugin._physics.GetGroundedPosition(target.transform.position, _plugin.CanPetSwim(state));
                var distanceToTarget = Vector3.Distance(state.Entity.transform.position, targetPosition);
                if (_plugin._recovery.TryRecover(owner, state, targetPosition, ownerDistanceToTarget))
                {
                    targetPosition = _plugin._physics.GetGroundedPosition(target.transform.position, _plugin.CanPetSwim(state));
                    distanceToTarget = Vector3.Distance(state.Entity.transform.position, targetPosition);
                }

                if (distanceToTarget > _plugin._config.Combat.EngageDistance)
                {
                    _plugin._movement.MoveTowards(state, targetPosition, _plugin._config.Combat.MoveSpeed, thinkInterval, true);
                    _plugin._movement.RotateTowards(state.Entity, target.transform.position, true, thinkInterval);
                    return;
                }

                _plugin._movement.RotateTowards(state.Entity, target.transform.position, true, thinkInterval);
                if (Time.time < state.NextAttackTime)
                {
                    return;
                }

                state.NextAttackTime = Time.time + _plugin.GetPetAttackCooldown(state);
                state.Threat.LastSeenTime = Time.time;
                target.Hurt(_plugin.GetPetAttackDamage(state), DamageType.Slash, state.Entity);
                target.SendNetworkUpdateImmediate();
                _plugin.RegisterAttackTraining(state);
            }

            public bool TryStartAttack(BasePlayer owner, PetState state, BaseCombatEntity attacker, bool ownerTriggered)
            {
                if (state == null || owner == null || !_plugin.IsValidPet(state.Entity) || !_plugin.IsValidCombatTarget(attacker))
                {
                    return false;
                }

                if (_plugin.IsFriendly(state.OwnerId, attacker))
                {
                    return false;
                }

                var referencePosition = ownerTriggered ? owner.transform.position : state.Entity.transform.position;
                if (Vector3.Distance(referencePosition, attacker.transform.position) > _plugin._config.Combat.ActivationRange)
                {
                    return false;
                }

                if (state.Target == attacker)
                {
                    state.Threat.LastSeenTime = Time.time;
                    state.Threat.ThreatValue += 0.25f;
                    return false;
                }

                state.Target = attacker;
                state.State = CompanionState.Attack;
                state.LastCombatTime = Time.time;
                state.Threat.TargetId = attacker.net != null ? (uint)attacker.net.ID.Value : 0u;
                state.Threat.ThreatValue = 1f;
                state.Threat.LastSeenTime = Time.time;
                Interface.CallHook("OnPetAttackStart", owner, state.Entity, attacker);
                _plugin.Reply(owner, "PetDefending", _plugin.GetTargetName(attacker));
                return true;
            }
        }

        private sealed class CompanionRecovery : ICompanionRecovery
        {
            private readonly MarolaPets _plugin;

            public CompanionRecovery(MarolaPets plugin)
            {
                _plugin = plugin;
            }

            public void Reset(PetState state)
            {
                state.StuckSinceTime = 0f;
                state.RecoveryStage = 0;
                state.LastRecoveryTime = 0f;
            }

            public bool TryRecover(BasePlayer owner, PetState state, Vector3 desiredDestination, float ownerDistance)
            {
                if (Time.time - state.LastRecoveryTime < _plugin._config.Recovery.RetryCooldown)
                {
                    return false;
                }

                var movementDelta = Vector3.Distance(state.PreviousPosition, state.Entity.transform.position);
                state.PreviousPosition = state.Entity.transform.position;

                if (movementDelta > _plugin._config.Recovery.StuckDeltaThreshold)
                {
                    state.StuckSinceTime = 0f;
                    state.RecoveryStage = 0;
                    return false;
                }

                if (state.StuckSinceTime <= 0f)
                {
                    state.StuckSinceTime = Time.time;
                    return false;
                }

                if (Time.time - state.StuckSinceTime < _plugin._config.Recovery.StuckDuration)
                {
                    return false;
                }

                state.LastRecoveryTime = Time.time;
                state.RecoveryStage = Mathf.Clamp(state.RecoveryStage + 1, 1, 3);

                if (state.RecoveryStage == 1)
                {
                    var refreshed = _plugin._physics.ResolveMovementTarget(state, desiredDestination);
                    _plugin._movement.Teleport(state, Vector3.MoveTowards(state.Entity.transform.position, refreshed, _plugin._config.Movement.PathRefreshDistance), true);
                    return true;
                }

                if (state.RecoveryStage == 2)
                {
                    var toOwner = owner.transform.position - state.Entity.transform.position;
                    toOwner.y = 0f;
                    var lateral = Vector3.Cross(Vector3.up, toOwner.sqrMagnitude > 0.01f ? toOwner.normalized : owner.eyes.BodyForward()).normalized;
                    var offset = state.FollowOffsetIndex % 2 == 0 ? lateral : -lateral;
                    _plugin._movement.Teleport(state, state.Entity.transform.position + offset * _plugin._config.Recovery.LateralRepositionDistance, true);
                    return true;
                }

                if (ownerDistance >= _plugin._config.Recovery.SafeTeleportDistance && _plugin.CanSafeTeleport(owner, state))
                {
                    _plugin._movement.Teleport(state, _plugin._movement.GetRecallPosition(owner), true);
                    Reset(state);
                    return true;
                }

                return false;
            }
        }

        // Bootstrap do plugin: registra permissao, carrega config, prepara definicoes de pets e expõe o comando de chat.
        private void Init()
        {
            permission.RegisterPermission(UsePermission, this);
            LoadConfigValues();
            LoadData();
            BuildProfiles();
            InitializeModules();
            cmd.AddChatCommand("pet", this, nameof(CmdPet));
        }

        // Inicia o loop principal da IA somente depois que o servidor terminou de iniciar.
        private void OnServerInitialized()
        {
            _updateTimer = timer.Every(GetSchedulerInterval(), UpdatePets);
        }

        // No unload, destrói primeiro o timer e depois remove as entidades de pet que este plugin criou.
        private void Unload()
        {
            _updateTimer?.Destroy();
            SaveData();

            for (var playerIndex = 0; playerIndex < BasePlayer.activePlayerList.Count; playerIndex++)
            {
                CloseBagContainer(BasePlayer.activePlayerList[playerIndex], true);
            }

            _ownerBuffer.Clear();
            _ownerBuffer.AddRange(_pets.Keys);
            for (var index = 0; index < _ownerBuffer.Count; index++)
            {
                DismissPet(_ownerBuffer[index], false);
            }
        }

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig();
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermission"] = "Voce nao tem permissao para usar este comando.",
                ["Usage"] = "Use /pet help para ver todos os comandos do sistema.",
                ["InvalidType"] = "Tipo de pet invalido. Tipos disponiveis: {0}.",
                ["PetAlreadyActive"] = "Voce ja possui um pet ativo. Use /pet dismiss antes.",
                ["PetSpawnFailed"] = "Nao foi possivel criar o pet. Verifique o prefab configurado.",
                ["PetSpawned"] = "Seu {0} foi chamado | controle: {1} | agressao: {2}.",
                ["PetDismissed"] = "Seu pet foi removido.",
                ["NoPet"] = "Voce nao possui um pet ativo.",
                ["PetLost"] = "Seu pet ficou perdido. Use /pet recall para traze-lo de volta.",
                ["PetRecallCooldown"] = "Aguarde {0:0.0}s para usar recall novamente.",
                ["PetRecalled"] = "Seu pet retornou ao seu lado.",
                ["PetStatusHeader"] = "Status do {0} | level {1}",
                ["PetStatusVitals"] = "Fome: {0:0}/{1:0} ({2:0}%) | Sede: {3:0}/{4:0} ({5:0}%)",
                ["PetStatusSpeed"] = "Velocidade: lv {0} | corrida {1:0.0} | sprint {2:0.0}",
                ["PetStatusCombat"] = "Ataque: lv {0} | dano {1:0.0} | cadencia {2:0.00}s | defesa lv {3} | reducao {4:0}%",
                ["PetDiagnosisHeader"] = "Diagnostico do pet {0}:",
                ["PetDiagnosisRuntime"] = "Runtime | entidade: {0} | owner: {1} | plugin: {2}/{3}/{4}",
                ["PetDiagnosisDistance"] = "Distancias | dono: {0:0.0}m | ancora: {1:0.0}m | alvo: {2}",
                ["PetDiagnosisNative"] = "IA nativa | suprimida: {0} | ultimo check: {1:0.0}s | detalhes: {2}",
                ["PetDiagnosisAssessment"] = "Analise | {0}",
                ["PetDied"] = "Seu pet morreu.",
                ["PetDefending"] = "Seu pet entrou em modo defend contra {0}.",
                ["ControlModeChanged"] = "Modo de controle do pet alterado para {0}.",
                ["AggressionModeChanged"] = "Modo de agressao do pet alterado para {0}.",
                ["HelpHeader"] = "Comandos do MarolaPets:",
                ["HelpSpawn"] = "/pet spawn [tipo] | cria o pet. Exemplos: lobo, urso, tigre, jacare, javali, veado, galinha.",
                ["HelpDismiss"] = "/pet dismiss | remove o pet atual.",
                ["HelpFollow"] = "/pet follow | pet segue voce com offset dinamico.",
                ["HelpStay"] = "/pet stay | pet para e espera no lugar.",
                ["HelpGuard"] = "/pet guard | pet ancora a guarda na posicao atual.",
                ["HelpRadius"] = "/pet radius <5|10|20> | define o raio da guarda.",
                ["HelpAttack"] = "/pet attack | manda o pet atacar o alvo que voce estiver olhando.",
                ["HelpBag"] = "/pet bag equip/remove/ui | equipa o bagageiro do pet com Saddle bag.",
                ["HelpAlly"] = "/pet ally add/remove/list | gerencia aliados do pet.",
                ["HelpModes"] = "/pet passive ou /pet aggressive | controla a agressao automatica.",
                ["HelpInfo"] = "/pet status, /pet diagnose, /pet debug | informacoes do runtime do pet.",
                ["AttackNoTarget"] = "Nenhum alvo valido na sua mira.",
                ["AttackStarted"] = "Ordem enviada. Seu pet vai atacar {0}.",
                ["RadiusChanged"] = "Raio de guarda ajustado para {0:0}m.",
                ["InvalidRadius"] = "Raio invalido. Use 5, 10 ou 20.",
                ["AllyUsage"] = "Use /pet ally add [nome], /pet ally remove [nome] ou /pet ally list.",
                ["AllyTargetNotFound"] = "Nao achei um player valido para esse comando de ally.",
                ["AllyAdded"] = "{0} agora e aliado do seu pet.",
                ["AllyRemoved"] = "{0} foi removido dos aliados do seu pet.",
                ["AllyAlreadyAdded"] = "{0} ja esta na lista de aliados.",
                ["AllyListHeader"] = "Aliados do pet: {0}",
                ["AllyListEmpty"] = "Voce ainda nao possui aliados cadastrados para o pet.",
                ["BagUsage"] = "Use /pet bag equip, /pet bag remove, /pet bag add [qtd], /pet bag take <item> [qtd] ou /pet bag ui.",
                ["BagNoHeldItem"] = "Equipe um item de comida ou agua na mao para guardar na mochila do pet.",
                ["BagItemUnsupported"] = "Esse item nao e suportado pela mochila do pet. Hoje ela aceita comida e agua configuradas.",
                ["BagNotEquipped"] = "Seu pet nao esta com bagageiro equipado. Use /pet bag equip com 1x {0}.",
                ["BagAlreadyEquipped"] = "Seu pet ja esta com o bagageiro equipado.",
                ["BagEquipMissing"] = "Voce precisa ter 1x {0} para equipar a mochila do pet.",
                ["BagEquipped"] = "Bagageiro equipado no pet usando 1x {0}.",
                ["BagRemoveNotEmpty"] = "Esvazie a mochila do pet antes de remover o bagageiro.",
                ["BagRemoveNoInventorySpace"] = "Voce nao tem espaco para remover o bagageiro do pet agora.",
                ["BagRemoved"] = "Bagageiro removido do pet e devolvido: {0}.",
                ["BagFull"] = "A mochila do pet esta cheia. Capacidade atual: {0} tipos de item.",
                ["BagStored"] = "Guardado na mochila do pet: {0}x {1}.",
                ["BagEmpty"] = "A mochila do pet esta vazia.",
                ["BagListHeader"] = "Mochila do pet: {0}",
                ["BagEntry"] = "- {0}x {1}",
                ["BagTakeUsage"] = "Use /pet bag take <item> [qtd].",
                ["BagItemNotFound"] = "Nao achei esse item na mochila do pet.",
                ["BagWithdrawn"] = "Retirado da mochila do pet: {0}x {1}.",
                ["BagNoInventorySpace"] = "Voce nao tem espaco para retirar esse item agora.",
                ["BagAutoConsumed"] = "Seu pet consumiu {0} da mochila para recuperar {1}.",
                ["GroundAutoConsumed"] = "Seu pet consumiu {0} do chao para recuperar {1}.",
                ["PetDebugHeader"] = "Debug do pet {0}:",
                ["PetDebugMovement"] = "Movimento | estado: {0} | vel: {1:0.00} | offset: {2} | recovery: {3}",
                ["PetDebugAnchor"] = "Anchor | dono: {0:0.0}m | ancora: {1:0.0}m | guard radius: {2:0}m",
                ["PetDebugThreat"] = "Combate | alvo id: {0} | threat: {1:0.00} | ultimo combate: {2:0.0}s atras",
                ["PetUiNameLabel"] = "{0} | Lv {1}",
                ["PetUiStatusLabel"] = "{1} | {2} | ♥ {3:0}/{4:0}",
                ["PetUiStatsLabel"] = "⚡ {6:0}% | ✦ V{7} | ⚔ A{8} | ☄ F {5:0}%",
                ["PetUiLabelCompact"] = "{0} | ♥ {3:0}/{4:0} | ☄ {5:0}% | ⚡ {6:0}% | ✦ V{7} | ⚔ A{8} | {1}/{2}",
                ["PetTargetPreview"] = "ALVO: {0}",
                ["PetTrainingLevelUp"] = "Seu pet evoluiu {0} para o nivel {1}."
            }, this);
        }

        // Registro central dos pets suportados. Hoje este MVP expõe apenas lobo, mas a estrutura aceita mais tipos.
        private void BuildProfiles()
        {
            _profiles.Clear();
            RegisterProfile("wolf", "Lobo", "assets/rust.ai/agents/wolf/wolf.prefab", true);
            RegisterProfile("lobo", "Lobo", "assets/rust.ai/agents/wolf/wolf.prefab", true);

            RegisterProfile("bear", "Urso", "assets/rust.ai/agents/bear/bear.prefab", true);
            RegisterProfile("urso", "Urso", "assets/rust.ai/agents/bear/bear.prefab", true);

            RegisterProfile("polarbear", "Urso Polar", "assets/rust.ai/agents/bear/polarbear.prefab", true);
            RegisterProfile("ursopolar", "Urso Polar", "assets/rust.ai/agents/bear/polarbear.prefab", true);
            RegisterProfile("urso-polar", "Urso Polar", "assets/rust.ai/agents/bear/polarbear.prefab", true);

            RegisterProfile("boar", "Javali", "assets/rust.ai/agents/boar/boar.prefab", false);
            RegisterProfile("javali", "Javali", "assets/rust.ai/agents/boar/boar.prefab", false);

            RegisterProfile("chicken", "Galinha", "assets/rust.ai/agents/chicken/chicken.prefab", false);
            RegisterProfile("galinha", "Galinha", "assets/rust.ai/agents/chicken/chicken.prefab", false);

            RegisterProfile("stag", "Veado", "assets/rust.ai/agents/stag/stag.prefab", false);
            RegisterProfile("veado", "Veado", "assets/rust.ai/agents/stag/stag.prefab", false);

            RegisterProfile("tiger", "Tigre", "assets/rust.ai/agents/tiger/tiger.prefab", true);
            RegisterProfile("tigre", "Tigre", "assets/rust.ai/agents/tiger/tiger.prefab", true);

            RegisterProfile("crocodile", "Jacare", "assets/rust.ai/agents/crocodile/crocodile.prefab", true);
            RegisterProfile("crocodilo", "Jacare", "assets/rust.ai/agents/crocodile/crocodile.prefab", true);
            RegisterProfile("jacare", "Jacare", "assets/rust.ai/agents/crocodile/crocodile.prefab", true);
        }

        // Helper usado por BuildProfiles para manter a adicao de novos pets orientada a dados.
        private void RegisterProfile(string key, string displayName, string prefab, bool canSwim)
        {
            _profiles[key] = new PetProfile
            {
                Key = key,
                DisplayName = displayName,
                Prefab = prefab,
                CanSwim = canSwim
            };
        }

        // Carrega a config do disco, corrige secoes ausentes e salva o resultado normalizado de volta no arquivo.
        private void LoadConfigValues()
        {
            _config = Config.ReadObject<PluginConfig>();
            if (_config?.Movement == null || _config.Combat == null || _config.AiLod == null || _config.NativeAi == null || _config.Recall == null || _config.Recovery == null || _config.Ui == null || _config.Training == null || _config.Inventory == null)
            {
                PrintWarning("Config invalida ou vazia; recriando configuracao padrao do MarolaPets.");
                LoadDefaultConfig();
            }

            _config.Movement.MinOwnerDistance = Mathf.Clamp(_config.Movement.MinOwnerDistance, 2f, 4f);
            _config.Movement.IdleDistance = Mathf.Clamp(_config.Movement.IdleDistance, _config.Movement.MinOwnerDistance, 4f);
            _config.Movement.WalkDistance = Mathf.Max(_config.Movement.WalkDistance, _config.Movement.IdleDistance + 1f);
            _config.Movement.RunDistance = Mathf.Max(_config.Movement.RunDistance, _config.Movement.WalkDistance + 1f);
            _config.Movement.SprintDistance = Mathf.Max(_config.Movement.SprintDistance, _config.Movement.RunDistance + 1f);
            _config.Movement.SideOffset = Mathf.Max(_config.Movement.SideOffset, 1.2f);
            _config.Movement.RearOffset = Mathf.Max(_config.Movement.RearOffset, 1.5f);
            _config.Movement.DefaultGuardRadius = ClampGuardRadius(_config.Movement.DefaultGuardRadius);
            _config.Movement.StopDistance = Mathf.Clamp(_config.Movement.StopDistance, 0.5f, _config.Movement.MinOwnerDistance);
            _config.Movement.TickInterval = Mathf.Max(_config.Movement.TickInterval, 0.05f);
            _config.Movement.Acceleration = Mathf.Max(_config.Movement.Acceleration, 1f);
            _config.Movement.Deceleration = Mathf.Max(_config.Movement.Deceleration, 1f);
            _config.Movement.WalkSpeed = Mathf.Max(_config.Movement.WalkSpeed, 1f);
            _config.Movement.RunSpeed = Mathf.Max(_config.Movement.RunSpeed, _config.Movement.WalkSpeed);
            _config.Movement.SprintSpeed = Mathf.Max(_config.Movement.SprintSpeed, _config.Movement.RunSpeed);
            _config.AiLod.FullRange = Mathf.Max(_config.AiLod.FullRange, 5f);
            _config.AiLod.SimplifiedRange = Mathf.Max(_config.AiLod.SimplifiedRange, _config.AiLod.FullRange + 1f);
            _config.AiLod.FullThinkInterval = Mathf.Max(_config.AiLod.FullThinkInterval, 0.05f);
            _config.AiLod.SimplifiedThinkInterval = Mathf.Max(_config.AiLod.SimplifiedThinkInterval, _config.AiLod.FullThinkInterval);
            _config.AiLod.SleepingThinkInterval = Mathf.Max(_config.AiLod.SleepingThinkInterval, _config.AiLod.SimplifiedThinkInterval);
            _config.Recovery.StuckDeltaThreshold = Mathf.Max(_config.Recovery.StuckDeltaThreshold, 0.01f);
            _config.Recovery.StuckDuration = Mathf.Max(_config.Recovery.StuckDuration, 1f);
            _config.Recovery.SafeTeleportDistance = Mathf.Max(_config.Recovery.SafeTeleportDistance, 50f);
            _config.Recovery.RetryCooldown = Mathf.Max(_config.Recovery.RetryCooldown, 0.1f);
            _config.Ui.DrawInterval = Mathf.Max(_config.Ui.DrawInterval, 0.25f);
            _config.Ui.CompactDistance = Mathf.Clamp(_config.Ui.CompactDistance, 5f, 30f);
            _config.Ui.MaxOwnerDistance = Mathf.Max(_config.Ui.MaxOwnerDistance, 5f);
            _config.Ui.CompactDistance = Mathf.Min(_config.Ui.CompactDistance, _config.Ui.MaxOwnerDistance);
            _config.Ui.VerticalOffset = Mathf.Clamp(_config.Ui.VerticalOffset, 0.5f, 4f);
            _config.Training.HungerDrainPerMinute = Mathf.Max(_config.Training.HungerDrainPerMinute, 0.1f);
            _config.Training.ThirstDrainPerMinute = Mathf.Max(_config.Training.ThirstDrainPerMinute, 0.1f);
            _config.Training.StaminaDrainMovePerSecond = Mathf.Max(_config.Training.StaminaDrainMovePerSecond, 0.1f);
            _config.Training.StaminaDrainAttack = Mathf.Max(_config.Training.StaminaDrainAttack, 0.1f);
            _config.Training.LowHungerThreshold = Mathf.Clamp(_config.Training.LowHungerThreshold, 5f, 60f);
            _config.Training.SpeedXpPerMeter = Mathf.Max(_config.Training.SpeedXpPerMeter, 0.01f);
            _config.Training.SpeedXpPerLevel = Mathf.Max(_config.Training.SpeedXpPerLevel, 1f);
            _config.Training.SpeedBonusPerLevel = Mathf.Clamp(_config.Training.SpeedBonusPerLevel, 0f, 0.25f);
            _config.Training.AttackXpPerHit = Mathf.Max(_config.Training.AttackXpPerHit, 0.01f);
            _config.Training.AttackXpPerLevel = Mathf.Max(_config.Training.AttackXpPerLevel, 1f);
            _config.Training.AttackBonusPerLevel = Mathf.Clamp(_config.Training.AttackBonusPerLevel, 0f, 0.4f);
            _config.Training.AttackSpeedBonusPerLevel = Mathf.Clamp(_config.Training.AttackSpeedBonusPerLevel, 0f, 0.12f);
            _config.Training.DefenseXpPerDamage = Mathf.Max(_config.Training.DefenseXpPerDamage, 0.01f);
            _config.Training.DefenseXpPerLevel = Mathf.Max(_config.Training.DefenseXpPerLevel, 1f);
            _config.Training.DefenseBonusPerLevel = Mathf.Clamp(_config.Training.DefenseBonusPerLevel, 0f, 0.2f);
            _config.Training.VitalityXpPerMinute = Mathf.Max(_config.Training.VitalityXpPerMinute, 0.01f);
            _config.Training.VitalityXpPerLevel = Mathf.Max(_config.Training.VitalityXpPerLevel, 1f);
            _config.Training.CapacityBonusPerLevel = Mathf.Clamp(_config.Training.CapacityBonusPerLevel, 0f, 50f);
            _config.Inventory.Capacity = Mathf.Clamp(_config.Inventory.Capacity, 1, 24);
            _config.Inventory.AutoConsumeCooldown = Mathf.Max(_config.Inventory.AutoConsumeCooldown, 1f);
            _config.Inventory.AutoEatThreshold = Mathf.Clamp(_config.Inventory.AutoEatThreshold, 5f, 95f);
            _config.Inventory.AutoDrinkThreshold = Mathf.Clamp(_config.Inventory.AutoDrinkThreshold, 5f, 95f);
            _config.Inventory.GroundFeedRadius = Mathf.Clamp(_config.Inventory.GroundFeedRadius, 0.5f, 6f);
            _config.Inventory.FoodRestore = _config.Inventory.FoodRestore ?? new Dictionary<string, float>();
            _config.Inventory.WaterRestore = _config.Inventory.WaterRestore ?? new Dictionary<string, float>();

            SaveConfig();
        }

        private void LoadData()
        {
            _dataFile = Interface.Oxide.DataFileSystem.GetFile("MarolaPets/ally_data");
            _storedData = _dataFile.ReadObject<StoredData>() ?? new StoredData();
            if (_storedData.AlliesByOwner == null)
            {
                _storedData.AlliesByOwner = new Dictionary<ulong, List<ulong>>();
            }

            if (_storedData.ProgressByOwner == null)
            {
                _storedData.ProgressByOwner = new Dictionary<ulong, PetProgress>();
            }

            if (_storedData.BagByOwner == null)
            {
                _storedData.BagByOwner = new Dictionary<ulong, List<PetBagEntry>>();
            }

            if (_storedData.BagEquippedByOwner == null)
            {
                _storedData.BagEquippedByOwner = new Dictionary<ulong, bool>();
            }
        }

        private void SaveData()
        {
            _dataFile?.WriteObject(_storedData);
        }

        private void OnServerSave()
        {
            SaveData();
        }

        private void InitializeModules()
        {
            _physics = new CompanionPhysics(this);
            _movement = new CompanionMovement(this);
            _combat = new CompanionCombat(this);
            _recovery = new CompanionRecovery(this);
            _brain = new CompanionBrain(this);
        }

        private void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        // A limpeza no disconnect evita que pets órfãos fiquem no mundo quando o dono sai.
        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null)
            {
                return;
            }

            CloseBagContainer(player, true);
            DismissPet(player.userID, false);
        }

        // O tratamento de morte cobre dois caminhos: remover o estado do pet quando ele morre ou limpar alvos quando inimigos morrem.
        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null)
            {
                return;
            }

            if (TryGetPetState(entity, out var petState))
            {
                HandlePetDeath(petState);
                return;
            }

            foreach (var state in _pets.Values)
            {
                if (state.Target == entity)
                {
                    ClearTarget(state);
                }
            }
        }

        // Esses hooks de IA impedem que o pet escolha como alvo o proprio dono ou pets aliados.
        private object CanNpcAttack(BaseNpc npc, BaseEntity entity)
        {
            if (npc == null || entity == null || !TryGetPetState(npc, out var state))
            {
                return null;
            }

            return IsFriendlyEntity(state.OwnerId, entity) ? false : (object)null;
        }

        private object OnNpcTarget(BaseEntity npc, BaseEntity entity)
        {
            if (npc == null || entity == null || !TryGetPetState(npc, out var state))
            {
                return null;
            }

            return IsFriendlyEntity(state.OwnerId, entity) ? true : (object)null;
        }

        private object OnNpcTargetSense(BaseEntity owner, BaseEntity entity, AIBrainSenses brainSenses)
        {
            if (owner == null || entity == null || !TryGetPetState(owner, out var state))
            {
                return null;
            }

            return IsFriendlyEntity(state.OwnerId, entity) ? true : (object)null;
        }

        // O dano recebido é o gatilho que tira o pet do modo passivo de follow e o coloca em modo defensivo.
        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null)
            {
                return;
            }

            var attacker = info.Initiator as BaseCombatEntity ?? info.InitiatorPlayer;
            if (!IsValidCombatTarget(attacker))
            {
                return;
            }

            if (info.InitiatorPlayer != null && _pets.TryGetValue(info.InitiatorPlayer.userID, out var attackerPetState) && attackerPetState.Aggression == PetAggression.Aggressive && attackerPetState.State != CompanionState.Stay)
            {
                _combat.TryStartAttack(info.InitiatorPlayer, attackerPetState, entity, true);
            }

            if (entity is BasePlayer owner && _pets.TryGetValue(owner.userID, out var ownerState))
            {
                ownerState.LastCombatTime = Time.time;
                if (ownerState.Aggression == PetAggression.Aggressive && ownerState.State != CompanionState.Stay)
                {
                    _combat.TryStartAttack(owner, ownerState, attacker, true);
                }

                return;
            }

            if (TryGetPetState(entity, out var petState))
            {
                var damageTaken = info.damageTypes.Total();
                var defenseReduction = GetPetDefenseReduction(petState);
                if (defenseReduction > 0f)
                {
                    info.damageTypes.ScaleAll(1f - defenseReduction);
                    damageTaken = info.damageTypes.Total();
                }

                var petOwner = BasePlayer.FindByID(petState.OwnerId);
                petState.LastCombatTime = Time.time;
                RegisterDefenseTraining(petState, damageTaken);
                if (petState.Aggression == PetAggression.Aggressive)
                {
                    _combat.TryStartAttack(petOwner, petState, attacker, false);
                }
            }
        }

        // Ponto principal de entrada do jogador: um roteador simples que delega cada subcomando para um metodo focado.
        private void CmdPet(BasePlayer player, string command, string[] args)
        {
            if (player == null)
            {
                return;
            }

            if (!permission.UserHasPermission(player.UserIDString, UsePermission))
            {
                Reply(player, "NoPermission");
                return;
            }

            if (args.Length == 0)
            {
                ShowHelp(player);
                return;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "help":
                    ShowHelp(player);
                    return;

                case "spawn":
                    HandleSpawnCommand(player, args);
                    return;

                case "dismiss":
                    if (!DismissPet(player.userID, true))
                    {
                        Reply(player, "NoPet");
                    }

                    return;

                case "recall":
                    RecallPet(player);
                    return;

                case "status":
                    ShowStatus(player);
                    return;

                case "diagnose":
                    DiagnosePet(player);
                    return;

                case "debug":
                    ShowDebug(player);
                    return;

                case "follow":
                    HandleControlCommand(player, CompanionState.Follow);
                    return;

                case "stay":
                    HandleControlCommand(player, CompanionState.Stay);
                    return;

                case "guard":
                    HandleControlCommand(player, CompanionState.Guard);
                    return;

                case "radius":
                    HandleRadiusCommand(player, args);
                    return;

                case "attack":
                    HandleAttackCommand(player);
                    return;

                case "bag":
                    HandleBagCommand(player, args);
                    return;

                case "ally":
                    HandleAllyCommand(player, args);
                    return;

                case "passive":
                    HandleAggressionCommand(player, PetAggression.Passive);
                    return;

                case "aggressive":
                    HandleAggressionCommand(player, PetAggression.Aggressive);
                    return;

                default:
                    Reply(player, "Usage");
                    return;
            }
        }

        private void HandleControlCommand(BasePlayer player, CompanionState requestedState)
        {
            if (!_pets.TryGetValue(player.userID, out var state) || !IsValidPet(state.Entity))
            {
                Reply(player, "NoPet");
                return;
            }

            state.State = requestedState;
            state.GuardPosition = state.Entity.transform.position;
            if (requestedState == CompanionState.Guard && state.GuardRadius <= 0f)
            {
                state.GuardRadius = _config.Movement.DefaultGuardRadius;
            }

            ClearTarget(state);
            Reply(player, "ControlModeChanged", FormatState(state.State));
        }

        private void HandleRadiusCommand(BasePlayer player, string[] args)
        {
            if (!_pets.TryGetValue(player.userID, out var state) || !IsValidPet(state.Entity))
            {
                Reply(player, "NoPet");
                return;
            }

            if (args.Length < 2 || !float.TryParse(args[1], out var guardRadius))
            {
                Reply(player, "InvalidRadius");
                return;
            }

            guardRadius = ClampGuardRadius(guardRadius);
            if (guardRadius <= 0f)
            {
                Reply(player, "InvalidRadius");
                return;
            }

            state.GuardRadius = guardRadius;
            if (state.State == CompanionState.Guard)
            {
                state.GuardPosition = state.Entity.transform.position;
            }

            Reply(player, "RadiusChanged", guardRadius);
        }

        private void HandleAttackCommand(BasePlayer player)
        {
            if (!_pets.TryGetValue(player.userID, out var state) || !IsValidPet(state.Entity))
            {
                Reply(player, "NoPet");
                return;
            }

            var target = FindLookTarget(player);
            if (!IsValidCombatTarget(target) || IsFriendly(player.userID, target))
            {
                Reply(player, "AttackNoTarget");
                return;
            }

            if (_combat.TryStartAttack(player, state, target, true))
            {
                Reply(player, "AttackStarted", GetTargetName(target));
            }
        }

        private void ShowHelp(BasePlayer player)
        {
            Reply(player, "HelpHeader");
            Reply(player, "HelpSpawn");
            Reply(player, "HelpDismiss");
            Reply(player, "HelpFollow");
            Reply(player, "HelpStay");
            Reply(player, "HelpGuard");
            Reply(player, "HelpRadius");
            Reply(player, "HelpAttack");
            Reply(player, "HelpBag");
            Reply(player, "HelpAlly");
            Reply(player, "HelpModes");
            Reply(player, "HelpInfo");
        }

        private void HandleAllyCommand(BasePlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "AllyUsage");
                return;
            }

            switch (args[1].ToLowerInvariant())
            {
                case "add":
                    UpdateAlly(player, args, true);
                    return;
                case "remove":
                    UpdateAlly(player, args, false);
                    return;
                case "list":
                    ShowAllies(player);
                    return;
                default:
                    Reply(player, "AllyUsage");
                    return;
            }
        }

        private void HandleBagCommand(BasePlayer player, string[] args)
        {
            if (!_pets.TryGetValue(player.userID, out var state) || !IsValidPet(state.Entity))
            {
                Reply(player, "NoPet");
                return;
            }

            if (args.Length < 2)
            {
                OpenBagContainer(player);
                return;
            }

            switch (args[1].ToLowerInvariant())
            {
                case "equip":
                    EquipBag(player);
                    return;
                case "remove":
                case "unequip":
                    RemoveBag(player);
                    return;
                case "add":
                    StoreHeldItemInBag(player, args);
                    return;
                case "take":
                    WithdrawBagItem(player, args);
                    return;
                case "list":
                case "ui":
                    OpenBagContainer(player);
                    return;
                default:
                    Reply(player, "BagUsage");
                    return;
            }
        }

        private void StoreHeldItemInBag(BasePlayer player, string[] args)
        {
            if (!EnsureBagEquipped(player))
            {
                return;
            }

            var heldItem = player.GetActiveItem();
            if (heldItem == null)
            {
                Reply(player, "BagNoHeldItem");
                return;
            }

            var shortname = heldItem.info.shortname;
            if (!IsSupportedBagItem(shortname))
            {
                Reply(player, "BagItemUnsupported");
                return;
            }

            var amount = heldItem.amount;
            if (args.Length > 2 && (!int.TryParse(args[2], out amount) || amount <= 0))
            {
                Reply(player, "BagUsage");
                return;
            }

            amount = Mathf.Clamp(amount, 1, heldItem.amount);
            if (!TryAddBagItem(player.userID, shortname, amount))
            {
                Reply(player, "BagFull", _config.Inventory.Capacity);
                return;
            }

            heldItem.UseItem(amount);
            SaveData();
            Reply(player, "BagStored", amount, GetItemDisplayName(shortname));
            RefreshBagUi(player);
        }

        private void WithdrawBagItem(BasePlayer player, string[] args)
        {
            if (!EnsureBagEquipped(player))
            {
                return;
            }

            if (args.Length < 3)
            {
                Reply(player, "BagTakeUsage");
                return;
            }

            var shortname = args[2].ToLowerInvariant();
            var amount = 1;
            if (args.Length > 3 && (!int.TryParse(args[3], out amount) || amount <= 0))
            {
                Reply(player, "BagTakeUsage");
                return;
            }

            if (!TryRemoveBagItem(player.userID, shortname, amount, out var removedAmount))
            {
                Reply(player, "BagItemNotFound");
                return;
            }

            var item = ItemManager.CreateByName(shortname, removedAmount);
            if (item == null)
            {
                TryAddBagItem(player.userID, shortname, removedAmount);
                Reply(player, "BagItemUnsupported");
                return;
            }

            if (!item.MoveToContainer(player.inventory.containerMain) && !item.MoveToContainer(player.inventory.containerBelt))
            {
                item.Remove();
                TryAddBagItem(player.userID, shortname, removedAmount);
                Reply(player, "BagNoInventorySpace");
                return;
            }

            SaveData();
            Reply(player, "BagWithdrawn", removedAmount, GetItemDisplayName(shortname));
            RefreshBagUi(player);
        }

        private void ShowBag(BasePlayer player)
        {
            OpenBagContainer(player);
        }

        private void OpenBagContainer(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            if (!EnsureBagEquipped(player))
            {
                return;
            }

            CloseBagContainer(player, true);

            var container = CreateBagContainer(_config.Inventory.Capacity);
            if (container == null || container.inventory == null)
            {
                Reply(player, "BagUsage");
                return;
            }

            SyncStoredBagToContainer(player.userID, container.inventory);
            _bagContainersByOwner[player.userID] = container;
            _bagOwnersByContainer[container] = player.userID;

            if (player.CanInteract()
                && Interface.CallHook("CanLootEntity", player, container) == null
                && player.inventory.loot.StartLootingEntity(container, false))
            {
                player.inventory.loot.AddContainer(container.inventory);
                player.inventory.loot.SendImmediate();
                player.ClientRPCPlayer(null, player, "RPC_OpenLootPanel", container.panelName);
                return;
            }

            CleanupBagContainer(container);
            _bagContainersByOwner.Remove(player.userID);
            _bagOwnersByContainer.Remove(container);
        }

        private void RefreshBagUi(BasePlayer player)
        {
            if (player == null || !_bagContainersByOwner.ContainsKey(player.userID))
            {
                return;
            }

            OpenBagContainer(player);
        }

        private void CloseBagContainer(BasePlayer player, bool sync)
        {
            if (player == null)
            {
                return;
            }

            if (!_bagContainersByOwner.TryGetValue(player.userID, out var container) || container == null)
            {
                return;
            }

            if (sync)
            {
                SyncContainerToStoredBag(player.userID, container.inventory);
            }

            if (player.inventory?.loot != null && player.inventory.loot.entitySource == container)
            {
                player.inventory.loot.Clear();
                player.ClientRPCPlayer(null, player, "RPC_CloseLootPanel");
            }

            CleanupBagContainer(container);
            _bagContainersByOwner.Remove(player.userID);
            _bagOwnersByContainer.Remove(container);
        }

        private StorageContainer CreateBagContainer(int capacity)
        {
            var storageEntity = GameManager.server.CreateEntity(BagContainerPrefab, new Vector3(0f, -500f, 0f)) as StorageContainer;
            if (storageEntity == null)
            {
                return null;
            }

            storageEntity.SetFlag(BaseEntity.Flags.Disabled, true);
            storageEntity.requireAuthIfNotLocked = false;

            var groundWatch = storageEntity.GetComponent<GroundWatch>();
            if (groundWatch != null)
            {
                UnityEngine.Object.Destroy(groundWatch);
            }

            UnityEngine.Object.Destroy(storageEntity.GetComponent<DestroyOnGroundMissing>());
            foreach (var collider in storageEntity.GetComponentsInChildren<Collider>())
            {
                UnityEngine.Object.Destroy(collider);
            }

            storageEntity.baseProtection = null;
            storageEntity.panelName = BagLootPanel;
            storageEntity.enableSaving = false;
            storageEntity._limitedNetworking = true;
            storageEntity.Spawn();
            BaseEntity.Query.Server.Remove(storageEntity);
            storageEntity.net?.SwitchGroup(Network.Net.sv.visibility.Get(0));
            storageEntity._limitedNetworking = false;

            if (storageEntity.inventory == null)
            {
                storageEntity.CreateInventory(true);
                storageEntity.OnInventoryFirstCreated(storageEntity.inventory);
            }

            storageEntity.inventory.allowedContents = ItemContainer.ContentsType.Generic;
            storageEntity.inventory.capacity = capacity;
            return storageEntity;
        }

        private void CleanupBagContainer(StorageContainer container)
        {
            if (container == null)
            {
                return;
            }

            if (container.inventory != null)
            {
                container.inventory.Clear();
            }

            if (!container.IsDestroyed)
            {
                container.Kill();
            }
        }

        private void SyncStoredBagToContainer(ulong ownerId, ItemContainer container)
        {
            if (container == null)
            {
                return;
            }

            container.Clear();
            var bag = GetOrCreateBag(ownerId);
            for (var index = 0; index < bag.Count; index++)
            {
                var entry = bag[index];
                AddItemToContainer(container, entry.Shortname, entry.Amount);
            }

            container.MarkDirty();
        }

        private void SyncContainerToStoredBag(ulong ownerId, ItemContainer container)
        {
            var bag = GetOrCreateBag(ownerId);
            bag.Clear();
            if (container?.itemList != null)
            {
                for (var index = 0; index < container.itemList.Count; index++)
                {
                    var item = container.itemList[index];
                    if (item == null || item.info == null || item.amount <= 0)
                    {
                        continue;
                    }

                    TryAddBagItem(ownerId, item.info.shortname, item.amount);
                }
            }

            SaveData();
        }

        private void AddItemToContainer(ItemContainer container, string shortname, int amount)
        {
            if (container == null || string.IsNullOrWhiteSpace(shortname) || amount <= 0)
            {
                return;
            }

            var definition = ItemManager.FindItemDefinition(shortname);
            if (definition == null)
            {
                return;
            }

            var remaining = amount;
            var stackSize = Math.Max(1, definition.stackable);
            while (remaining > 0)
            {
                var stackAmount = Math.Min(stackSize, remaining);
                var item = ItemManager.Create(definition, stackAmount);
                if (item == null)
                {
                    break;
                }

                if (!item.MoveToContainer(container))
                {
                    item.Remove();
                    break;
                }

                remaining -= stackAmount;
            }
        }

        private void OnLootEntityEnd(BasePlayer player, StorageContainer container)
        {
            if (player == null || container == null || !_bagOwnersByContainer.TryGetValue(container, out var ownerId))
            {
                return;
            }

            SyncContainerToStoredBag(ownerId, container.inventory);
            CleanupBagContainer(container);
            _bagOwnersByContainer.Remove(container);
            _bagContainersByOwner.Remove(ownerId);
        }

        private void UpdateAlly(BasePlayer owner, string[] args, bool add)
        {
            var ally = ResolvePlayerForAllyCommand(owner, args);
            if (ally == null || ally.userID == owner.userID)
            {
                Reply(owner, "AllyTargetNotFound");
                return;
            }

            if (add)
            {
                if (AddAlly(owner.userID, ally.userID))
                {
                    AddAlly(ally.userID, owner.userID);
                    SaveData();
                    Reply(owner, "AllyAdded", ally.displayName);
                    return;
                }

                Reply(owner, "AllyAlreadyAdded", ally.displayName);
                return;
            }

            RemoveAlly(owner.userID, ally.userID);
            RemoveAlly(ally.userID, owner.userID);
            SaveData();
            Reply(owner, "AllyRemoved", ally.displayName);
        }

        private void ShowAllies(BasePlayer owner)
        {
            if (!_storedData.AlliesByOwner.TryGetValue(owner.userID, out var allyIds) || allyIds == null || allyIds.Count == 0)
            {
                Reply(owner, "AllyListEmpty");
                return;
            }

            var allyNames = new List<string>();
            for (var index = 0; index < allyIds.Count; index++)
            {
                var ally = FindPlayerById(allyIds[index]);
                allyNames.Add(ally != null ? ally.displayName : allyIds[index].ToString());
            }

            Reply(owner, "AllyListHeader", string.Join(", ", allyNames));
        }

        private void ShowDebug(BasePlayer player)
        {
            if (!_pets.TryGetValue(player.userID, out var state) || !IsValidPet(state.Entity))
            {
                Reply(player, "NoPet");
                return;
            }

            var ownerDistance = Vector3.Distance(player.transform.position, state.Entity.transform.position);
            var anchorPosition = _movement.GetAnchorPosition(player, state);
            var anchorDistance = Vector3.Distance(anchorPosition, state.Entity.transform.position);

            Reply(player, "PetDebugHeader", GetDisplayName(state.PetType));
            Reply(player, "PetDebugMovement", FormatState(state.State), state.SmoothedVelocity.magnitude, state.FollowOffsetIndex, state.RecoveryStage);
            Reply(player, "PetDebugAnchor", ownerDistance, anchorDistance, state.GuardRadius <= 0f ? _config.Movement.DefaultGuardRadius : state.GuardRadius);
            Reply(player, "PetDebugThreat", state.Threat.TargetId, state.Threat.ThreatValue, Time.time - state.LastCombatTime);
        }

        private void HandleAggressionCommand(BasePlayer player, PetAggression aggression)
        {
            if (!_pets.TryGetValue(player.userID, out var state) || !IsValidPet(state.Entity))
            {
                Reply(player, "NoPet");
                return;
            }

            state.Aggression = aggression;
            if (aggression == PetAggression.Passive)
            {
                ClearTarget(state);
            }

            Reply(player, "AggressionModeChanged", FormatAggression(aggression));
        }

        // O spawn valida o tipo pedido, garante um unico pet ativo por dono, cria o NPC e registra seu estado.
        private void HandleSpawnCommand(BasePlayer player, string[] args)
        {
            var petType = args.Length > 1 ? args[1].ToLowerInvariant() : "wolf";
            if (!_profiles.TryGetValue(petType, out var profile))
            {
                Reply(player, "InvalidType", string.Join(", ", _profiles.Keys.OrderBy(key => key)));
                return;
            }

            if (_pets.TryGetValue(player.userID, out var existingState) && IsValidPet(existingState.Entity))
            {
                Reply(player, "PetAlreadyActive");
                return;
            }

            DismissPet(player.userID, false);

            var spawnPosition = _movement.GetRecallPosition(player);
            var entity = GameManager.server.CreateEntity(profile.Prefab, spawnPosition, Quaternion.identity, true) as BaseNpc;
            if (entity == null)
            {
                Reply(player, "PetSpawnFailed");
                PrintWarning($"Failed to create pet '{petType}' from prefab '{profile.Prefab}'.");
                return;
            }

            entity.OwnerID = player.userID;
            entity.enableSaving = false;
            entity.Spawn();
            _movement.RotateTowards(entity, player.transform.position + player.eyes.BodyForward(), true, _config.Movement.TickInterval);

            var state = new PetState
            {
                OwnerId = player.userID,
                PetType = petType,
                Entity = entity,
                State = CompanionState.Follow,
                Aggression = PetAggression.Aggressive,
                Target = null,
                GuardPosition = entity.transform.position,
                GuardRadius = _config.Movement.DefaultGuardRadius,
                LastKnownOwnerPosition = player.transform.position,
                LastNetworkPosition = entity.transform.position,
                PreviousPosition = entity.transform.position,
                LastResolvedDestination = entity.transform.position,
                FollowOffsetIndex = 0,
                LastOffsetSwapTime = Time.time,
                NextAttackTime = 0f,
                LastRecallTime = 0f,
                NextThinkTime = 0f,
                LastCombatTime = 0f,
                StuckSinceTime = 0f,
                LastRecoveryTime = 0f,
                LastVitalsUpdateTime = Time.time,
                Hunger = GetPetMaxHunger(player.userID),
                Stamina = GetPetMaxThirst(player.userID),
                AiTier = PetAiTier.Full,
                RecoveryStage = 0,
                NativeAiSuppressed = false,
                LastNativeAiCheckTime = 0f,
                LastNativeAiReport = "pending"
            };

            _pets[player.userID] = state;
            _petOwnersByEntity[entity] = player.userID;
            EnforceNativeAiControl(state, true);

            Interface.CallHook("OnPetSpawned", player, entity, petType);
            Reply(player, "PetSpawned", profile.DisplayName, FormatState(state.State), FormatAggression(state.Aggression));
        }

        // Recall é um reset seguro: limpa o combate, teleporta para perto do dono e reinicia o modo follow.
        private void RecallPet(BasePlayer player)
        {
            if (!_pets.TryGetValue(player.userID, out var state) || !IsValidPet(state.Entity))
            {
                Reply(player, "NoPet");
                return;
            }

            var timeSinceRecall = Time.time - state.LastRecallTime;
            if (state.LastRecallTime > 0f && timeSinceRecall < _config.Recall.Cooldown)
            {
                Reply(player, "PetRecallCooldown", _config.Recall.Cooldown - timeSinceRecall);
                return;
            }

            state.LastRecallTime = Time.time;
            state.State = CompanionState.Follow;
            state.NextThinkTime = 0f;
            state.AiTier = PetAiTier.Full;
            ClearTarget(state);
            _recovery.Reset(state);
            _movement.Teleport(state, _movement.GetRecallPosition(player), true);
            state.GuardPosition = state.Entity.transform.position;
            EnforceNativeAiControl(state, true);
            _movement.RotateTowards(state.Entity, player.transform.position + player.eyes.BodyForward(), true, _config.Movement.TickInterval);
            Reply(player, "PetRecalled");
        }

        private void ShowStatus(BasePlayer player)
        {
            if (!_pets.TryGetValue(player.userID, out var state) || !IsValidPet(state.Entity))
            {
                Reply(player, "NoPet");
                return;
            }

            var progress = GetProgress(state.OwnerId);
            var maxHunger = GetPetMaxHunger(state.OwnerId);
            var maxThirst = GetPetMaxThirst(state.OwnerId);

            Reply(player, "PetStatusHeader", GetDisplayName(state.PetType), GetPetLevel(progress));
            Reply(player, "PetStatusVitals", state.Hunger, maxHunger, GetPetHungerPercent(state), state.Stamina, maxThirst, GetPetThirstPercent(state));
            Reply(player, "PetStatusSpeed", progress.SpeedLevel, GetPetMoveSpeed(state, _config.Movement.RunSpeed), GetPetMoveSpeed(state, _config.Movement.SprintSpeed));
            Reply(player, "PetStatusCombat", progress.AttackLevel, GetPetAttackDamage(state), GetPetAttackCooldown(state), progress.DefenseLevel, GetPetDefenseReduction(state) * 100f);
        }

        private void DiagnosePet(BasePlayer player)
        {
            if (!_pets.TryGetValue(player.userID, out var state) || !IsValidPet(state.Entity))
            {
                Reply(player, "NoPet");
                return;
            }

            EnforceNativeAiControl(state, true);

            var ownerDistance = Vector3.Distance(player.transform.position, state.Entity.transform.position);
            var anchorPosition = _movement.GetAnchorPosition(player, state);
            var anchorDistance = Vector3.Distance(anchorPosition, state.Entity.transform.position);
            var targetInfo = state.Target == null ? "none" : $"{GetTargetName(state.Target)} ({Vector3.Distance(state.Entity.transform.position, state.Target.transform.position):0.0}m)";

            Reply(player, "PetDiagnosisHeader", GetDisplayName(state.PetType));
            Reply(player, "PetDiagnosisRuntime", state.Entity.ShortPrefabName, state.OwnerId, GetStatusMode(state), FormatState(state.State), FormatAggression(state.Aggression));
            Reply(player, "PetDiagnosisDistance", ownerDistance, anchorDistance, targetInfo);
            Reply(player, "PetDiagnosisNative", state.NativeAiSuppressed ? "yes" : "no", Time.time - state.LastNativeAiCheckTime, state.LastNativeAiReport ?? "n/a");
            Reply(player, "PetDiagnosisAssessment", _movement.BuildDiagnosisAssessment(state, ownerDistance, anchorDistance));
        }

        // Loop principal da IA: primeiro valida o estado do dono/pet e depois decide entre follow, stay/guard ou combate.
        private void UpdatePets()
        {
            var now = Time.time;

            _ownerBuffer.Clear();
            _ownerBuffer.AddRange(_pets.Keys);
            for (var index = 0; index < _ownerBuffer.Count; index++)
            {
                var ownerId = _ownerBuffer[index];
                if (!_pets.TryGetValue(ownerId, out var state))
                {
                    continue;
                }

                var owner = BasePlayer.FindByID(ownerId);

                if (owner == null || !owner.IsConnected)
                {
                    DismissPet(ownerId, false);
                    continue;
                }

                if (!IsValidPet(state.Entity))
                {
                    CleanupState(ownerId, state);
                    continue;
                }

                if (_config.NativeAi.ReapplyEveryThink)
                {
                    EnforceNativeAiControl(state, false);
                }

                var ownerDistance = Vector3.Distance(owner.transform.position, state.Entity.transform.position);
                var aiTier = GetAiTier(ownerDistance, state.Target != null);
                if (now < state.NextThinkTime)
                {
                    continue;
                }

                var thinkInterval = GetThinkInterval(aiTier);
                state.NextThinkTime = now + thinkInterval;
                state.AiTier = aiTier;
                UpdatePetVitals(state, now, thinkInterval);

                if (aiTier == PetAiTier.Sleeping && state.Target == null)
                {
                    TryDrawPetWorldUi(owner, state, ownerDistance, now);
                    state.State = state.State == CompanionState.Follow ? CompanionState.Idle : state.State;
                    state.SmoothedVelocity = Vector3.zero;
                    state.LastKnownOwnerPosition = owner.transform.position;
                    continue;
                }

                if (state.Target == null && state.Aggression == PetAggression.Aggressive && state.State != CompanionState.Stay && TryAcquireAggressiveTarget(owner, state))
                {
                    thinkInterval = _config.AiLod.FullThinkInterval;
                    state.NextThinkTime = now + thinkInterval;
                    state.AiTier = PetAiTier.Full;
                }

                _brain.Update(owner, state, ownerDistance, thinkInterval);
                TryDrawPetWorldUi(owner, state, ownerDistance, now);
                TryDrawAttackTargetPreview(owner, state);

                state.LastKnownOwnerPosition = owner.transform.position;
            }
        }

        // A limpeza de alvo é o ponto unico de saida do combate e também dispara um hook de parada para integracoes.
        private void ClearTarget(PetState state)
        {
            if (state == null)
            {
                return;
            }

            if (state.Target != null)
            {
                var owner = BasePlayer.FindByID(state.OwnerId);
                Interface.CallHook("OnPetAttackStop", owner, state.Entity, state.Target);
            }

            state.Target = null;
            state.Threat.TargetId = 0u;
            state.Threat.ThreatValue = 0f;
            state.Threat.LastSeenTime = 0f;
            state.State = state.State == CompanionState.Stay || state.State == CompanionState.Guard ? state.State : CompanionState.Follow;
        }

        // Dismiss é o caminho publico de remocao: remove o estado, opcionalmente avisa o dono e dispara hooks de integracao.
        private bool DismissPet(ulong ownerId, bool notifyOwner)
        {
            if (!_pets.TryGetValue(ownerId, out var state))
            {
                return false;
            }

            var owner = BasePlayer.FindByID(ownerId);
            CleanupState(ownerId, state);

            if (notifyOwner && owner != null)
            {
                Reply(owner, "PetDismissed");
            }

            Interface.CallHook("OnPetDismissed", owner, state?.Entity, state?.PetType);
            return true;
        }

        private void EquipBag(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            if (HasEquippedBag(player.userID))
            {
                Reply(player, "BagAlreadyEquipped");
                return;
            }

            var definition = ItemManager.FindItemDefinition(BagEquipItemShortname);
            if (definition == null)
            {
                Reply(player, "BagItemUnsupported");
                return;
            }

            if (player.inventory.GetAmount(definition.itemid) < 1 || player.inventory.Take(null, definition.itemid, 1) != 1)
            {
                Reply(player, "BagEquipMissing", GetItemDisplayName(BagEquipItemShortname));
                return;
            }

            _storedData.BagEquippedByOwner[player.userID] = true;
            SaveData();
            Reply(player, "BagEquipped", GetItemDisplayName(BagEquipItemShortname));
        }

        private void RemoveBag(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            if (!EnsureBagEquipped(player))
            {
                return;
            }

            CloseBagContainer(player, true);

            var bag = GetOrCreateBag(player.userID);
            if (bag.Count > 0)
            {
                Reply(player, "BagRemoveNotEmpty");
                return;
            }

            var definition = ItemManager.FindItemDefinition(BagEquipItemShortname);
            if (definition == null)
            {
                Reply(player, "BagItemUnsupported");
                return;
            }

            var item = ItemManager.Create(definition, 1);
            if (item == null)
            {
                Reply(player, "BagItemUnsupported");
                return;
            }

            if (!item.MoveToContainer(player.inventory.containerMain) && !item.MoveToContainer(player.inventory.containerBelt))
            {
                item.Remove();
                Reply(player, "BagRemoveNoInventorySpace");
                return;
            }

            _storedData.BagEquippedByOwner[player.userID] = false;
            SaveData();
            Reply(player, "BagRemoved", GetItemDisplayName(BagEquipItemShortname));
        }

        private bool EnsureBagEquipped(BasePlayer player)
        {
            if (player == null)
            {
                return false;
            }

            if (HasEquippedBag(player.userID))
            {
                return true;
            }

            Reply(player, "BagNotEquipped", GetItemDisplayName(BagEquipItemShortname));
            return false;
        }

        private bool HasEquippedBag(ulong ownerId)
        {
            return _storedData?.BagEquippedByOwner != null
                && _storedData.BagEquippedByOwner.TryGetValue(ownerId, out var equipped)
                && equipped;
        }

        // A limpeza interna faz o trabalho destrutivo e é reutilizada por unload, dismiss, tratamento de estado invalido e disconnect.
        private void CleanupState(ulong ownerId, PetState state)
        {
            _pets.Remove(ownerId);

            if (state?.Entity != null)
            {
                _petOwnersByEntity.Remove(state.Entity);
                if (IsValidPet(state.Entity))
                {
                    state.Entity.Kill();
                }
            }
        }

        // A morte do pet precisa de uma limpeza propria porque o Rust destrói a entidade antes do fluxo normal de dismiss.
        private void HandlePetDeath(PetState state)
        {
            if (state == null)
            {
                return;
            }

            var owner = BasePlayer.FindByID(state.OwnerId);
            state.State = CompanionState.Dead;
            _pets.Remove(state.OwnerId);
            if (state.Entity != null)
            {
                _petOwnersByEntity.Remove(state.Entity);
            }

            Interface.CallHook("OnPetDeath", owner, state.Entity, state.PetType);
            if (owner != null)
            {
                Reply(owner, "PetDied");
            }
        }

        // A busca reversa permite que hooks genericos descubram se um NPC pertence a este sistema de pets.
        private bool TryGetPetState(BaseEntity entity, out PetState state)
        {
            state = null;
            if (!(entity is BaseCombatEntity combatEntity))
            {
                return false;
            }

            if (!_petOwnersByEntity.TryGetValue(combatEntity, out var ownerId))
            {
                return false;
            }

            return _pets.TryGetValue(ownerId, out state);
        }

        // As regras de amizade protegem o dono e seus proprios pets de fogo amigo acidental.
        private bool IsFriendly(ulong ownerId, BaseCombatEntity target)
        {
            if (target == null)
            {
                return true;
            }

            if (target is BasePlayer player && (player.userID == ownerId || IsAlliedWith(ownerId, player.userID)))
            {
                return true;
            }

            return TryGetPetState(target, out var otherState) && (otherState.OwnerId == ownerId || IsAlliedWith(ownerId, otherState.OwnerId));
        }

        private bool IsFriendlyEntity(ulong ownerId, BaseEntity target)
        {
            return target is BaseCombatEntity combatEntity && IsFriendly(ownerId, combatEntity);
        }

        // Helpers de validacao centralizam as checagens de sanidade das entidades do Rust usadas em todo o loop da IA.
        private bool IsValidPet(BaseNpc entity)
        {
            return entity != null && entity.IsValid() && !entity.IsDestroyed && !entity.IsDead();
        }

        private bool IsValidCombatTarget(BaseCombatEntity entity)
        {
            return entity != null && entity.IsValid() && !entity.IsDestroyed && !entity.IsDead();
        }

        private float GetSchedulerInterval()
        {
            return Mathf.Min(_config.Movement.TickInterval, _config.AiLod.FullThinkInterval, _config.AiLod.SimplifiedThinkInterval, _config.AiLod.SleepingThinkInterval);
        }

        private PetAiTier GetAiTier(float ownerDistance, bool hasTarget)
        {
            if (hasTarget)
            {
                return PetAiTier.Full;
            }

            if (ownerDistance <= _config.AiLod.FullRange)
            {
                return PetAiTier.Full;
            }

            if (ownerDistance <= _config.AiLod.SimplifiedRange)
            {
                return PetAiTier.Simplified;
            }

            return PetAiTier.Sleeping;
        }

        private float GetThinkInterval(PetAiTier aiTier)
        {
            switch (aiTier)
            {
                case PetAiTier.Simplified:
                    return _config.AiLod.SimplifiedThinkInterval;
                case PetAiTier.Sleeping:
                    return _config.AiLod.SleepingThinkInterval;
                default:
                    return _config.AiLod.FullThinkInterval;
            }
        }

        private void EnforceNativeAiControl(PetState state, bool forceReport)
        {
            if (state == null || !IsValidPet(state.Entity))
            {
                return;
            }

            var report = SuppressNativeAi(state.Entity);
            state.NativeAiSuppressed = !string.IsNullOrEmpty(report) && !report.Contains("no-runtime-components");
            state.LastNativeAiCheckTime = Time.time;

            if (forceReport || !string.Equals(state.LastNativeAiReport, report, StringComparison.Ordinal))
            {
                state.LastNativeAiReport = report;
            }
        }

        private string SuppressNativeAi(BaseNpc entity)
        {
            if (entity == null)
            {
                return "entity-null";
            }

            var actions = new List<string>();
            foreach (var component in entity.GetComponents<Component>())
            {
                if (component == null)
                {
                    continue;
                }

                var typeName = component.GetType().Name;
                if (_config.NativeAi.DisableBrain && (typeName.IndexOf("Brain", StringComparison.OrdinalIgnoreCase) >= 0 || typeName.IndexOf("FSM", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    DisableBehaviourComponent(component, typeName, actions);
                    TryInvokeNoArg(component, "DisableShouldThink", actions, typeName, "disable-think");
                    TrySetMember(component, "sleeping", true, actions, typeName, "sleep");
                    TrySetMember(component, "lastWarpTime", float.MaxValue, actions, typeName, "warp-max");
                    if (_config.NativeAi.ClearHostileTargets)
                    {
                        TrySetMember(component, "AttackTarget", null, actions, typeName, "clear-attack");
                        TrySetMember(component, "AttackTransform", null, actions, typeName, "clear-transform");
                    }
                    continue;
                }

                if (_config.NativeAi.DisableNavigator && typeName.IndexOf("Navigator", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    DisableBehaviourComponent(component, typeName, actions);
                    TryInvokeNoArg(component, "Stop", actions, typeName, "stop");
                    TryInvokeNoArg(component, "StopMoving", actions, typeName, "stop-moving");
                    TryInvokeNoArg(component, "ClearFacingDirectionOverride", actions, typeName, "clear-facing");
                }
            }

            if (_config.NativeAi.ClearHostileTargets)
            {
                TrySetMember(entity, "AttackTarget", null, actions, entity.GetType().Name, "clear-entity-attack");
            }

            return actions.Count == 0 ? "no-runtime-components" : string.Join(", ", actions.Distinct());
        }

        private void DisableBehaviourComponent(Component component, string typeName, List<string> actions)
        {
            if (!(component is Behaviour behaviour))
            {
                return;
            }

            if (!behaviour.enabled)
            {
                actions.Add($"{typeName}:already-off");
                return;
            }

            behaviour.enabled = false;
            actions.Add($"{typeName}:disabled");
        }

        private void TryInvokeNoArg(object instance, string methodName, List<string> actions, string typeName, string actionName)
        {
            var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (method == null)
            {
                return;
            }

            method.Invoke(instance, null);
            actions.Add($"{typeName}:{actionName}");
        }

        private void TrySetMember(object instance, string memberName, object value, List<string> actions, string typeName, string actionName)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var property = instance.GetType().GetProperty(memberName, flags);
            if (property != null && property.CanWrite)
            {
                property.SetValue(instance, value, null);
                actions.Add($"{typeName}:{actionName}");
                return;
            }

            var field = instance.GetType().GetField(memberName, flags);
            if (field == null)
            {
                return;
            }

            field.SetValue(instance, value);
            actions.Add($"{typeName}:{actionName}");
        }

        // Helpers de apresentacao mantêm a formatacao de UI/chat separada da logica de gameplay.
        private string GetDisplayName(string petType)
        {
            return _profiles.TryGetValue(petType, out var profile) ? profile.DisplayName : petType;
        }

        private string GetStatusMode(PetState state)
        {
            return state.Target != null ? "atacando" : FormatState(state.State);
        }

        private string FormatState(CompanionState state)
        {
            switch (state)
            {
                case CompanionState.Stay:
                    return "parado";
                case CompanionState.Guard:
                    return "guardando";
                case CompanionState.Idle:
                    return "ocioso";
                case CompanionState.Attack:
                    return "atacando";
                case CompanionState.Mounted:
                    return "montado";
                case CompanionState.Dead:
                    return "morto";
                default:
                    return "seguindo";
            }
        }

        private bool CanPetSwim(PetState state)
        {
            return state != null && _profiles.TryGetValue(state.PetType, out var profile) && profile.CanSwim;
        }

        private bool CanSafeTeleport(BasePlayer owner, PetState state)
        {
            if (owner == null || state == null)
            {
                return false;
            }

            if (state.Target != null)
            {
                return false;
            }

            if (Time.time - state.LastCombatTime < _config.Recovery.CombatBlockDuration)
            {
                return false;
            }

            var ownerPosition = owner.transform.position;
            for (var index = 0; index < BasePlayer.activePlayerList.Count; index++)
            {
                var nearbyPlayer = BasePlayer.activePlayerList[index];
                if (nearbyPlayer == null || nearbyPlayer.userID == owner.userID || !nearbyPlayer.IsConnected || nearbyPlayer.IsSleeping())
                {
                    continue;
                }

                if (IsAlliedWith(owner.userID, nearbyPlayer.userID))
                {
                    continue;
                }

                if (Vector3.Distance(ownerPosition, nearbyPlayer.transform.position) <= _config.Recovery.NearbyEnemyRange)
                {
                    return false;
                }
            }

            return true;
        }

        private float ClampGuardRadius(float guardRadius)
        {
            if (Mathf.Approximately(guardRadius, 5f) || Mathf.Approximately(guardRadius, 10f) || Mathf.Approximately(guardRadius, 20f))
            {
                return guardRadius;
            }

            return 0f;
        }

        private BaseCombatEntity FindLookTarget(BasePlayer player)
        {
            if (player == null)
            {
                return null;
            }

            var ray = player.eyes.HeadRay();
            if (Physics.Raycast(ray, out var hit, _config.Combat.ActivationRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                var directTarget = hit.GetEntity() as BaseCombatEntity;
                if (IsValidCombatTarget(directTarget) && !IsFriendly(player.userID, directTarget))
                {
                    return directTarget;
                }
            }

            BaseCombatEntity bestTarget = FindLookPlayer(player);
            var bestProjection = bestTarget != null ? Vector3.Distance(ray.origin, GetAimPoint(bestTarget)) : float.MaxValue;
            var bestOffRayDistance = bestTarget != null ? GetOffRayDistance(ray, GetAimPoint(bestTarget), out _) : float.MaxValue;

            _entityScanBuffer.Clear();
            Vis.Entities(player.transform.position, _config.Combat.ActivationRange, _entityScanBuffer);
            for (var index = 0; index < _entityScanBuffer.Count; index++)
            {
                var candidate = _entityScanBuffer[index] as BaseCombatEntity;
                if (!IsValidCombatTarget(candidate)
                    || candidate is BasePlayer
                    || candidate == player
                    || IsFriendly(player.userID, candidate)
                    || TryGetPetState(candidate, out _))
                {
                    continue;
                }

                var aimPoint = GetAimPoint(candidate);
                var offRayDistance = GetOffRayDistance(ray, aimPoint, out var projection);
                if (projection <= 0f || projection > _config.Combat.ActivationRange)
                {
                    continue;
                }

                var allowedRadius = Mathf.Lerp(0.6f, 2f, projection / _config.Combat.ActivationRange);
                if (offRayDistance > allowedRadius)
                {
                    continue;
                }

                if (offRayDistance > bestOffRayDistance + 0.01f)
                {
                    continue;
                }

                if (Mathf.Abs(offRayDistance - bestOffRayDistance) <= 0.01f && projection >= bestProjection)
                {
                    continue;
                }

                bestTarget = candidate;
                bestProjection = projection;
                bestOffRayDistance = offRayDistance;
            }

            _entityScanBuffer.Clear();
            return bestTarget;
        }

        private BasePlayer FindLookPlayer(BasePlayer owner)
        {
            var ray = owner.eyes.HeadRay();
            BasePlayer bestMatch = null;
            var bestDistance = float.MaxValue;

            for (var index = 0; index < BasePlayer.activePlayerList.Count; index++)
            {
                var candidate = BasePlayer.activePlayerList[index];
                if (candidate == null || candidate.userID == owner.userID || !candidate.IsConnected || candidate.IsSleeping())
                {
                    continue;
                }

                if (IsFriendly(owner.userID, candidate))
                {
                    continue;
                }

                var targetPoint = GetAimPoint(candidate);
                var offRayDistance = GetOffRayDistance(ray, targetPoint, out var projection);
                if (projection <= 0f || projection > _config.Combat.ActivationRange)
                {
                    continue;
                }

                var allowedRadius = Mathf.Lerp(0.75f, 1.8f, projection / _config.Combat.ActivationRange);
                if (offRayDistance > allowedRadius || projection >= bestDistance)
                {
                    continue;
                }

                bestDistance = projection;
                bestMatch = candidate;
            }

            return bestMatch;
        }

        private Vector3 GetAimPoint(BaseCombatEntity entity)
        {
            if (entity is BasePlayer player)
            {
                return player.eyes.position;
            }

            var position = entity.transform.position;
            position.y += 0.9f;
            return position;
        }

        private float GetOffRayDistance(Ray ray, Vector3 targetPoint, out float projection)
        {
            var toTarget = targetPoint - ray.origin;
            projection = Vector3.Dot(ray.direction, toTarget);
            if (projection <= 0f)
            {
                return float.MaxValue;
            }

            var closestPoint = ray.origin + ray.direction * projection;
            return Vector3.Distance(closestPoint, targetPoint);
        }

        private void TryDrawAttackTargetPreview(BasePlayer owner, PetState state)
        {
            if (owner == null || state == null || !IsValidPet(state.Entity))
            {
                return;
            }

            var target = FindLookTarget(owner);
            if (!IsValidCombatTarget(target) || IsFriendly(owner.userID, target))
            {
                return;
            }

            var drawDuration = Mathf.Max(0.08f, _config.Movement.TickInterval + 0.02f);
            var targetPoint = GetAimPoint(target) + Vector3.up * 0.55f;
            owner.SendConsoleCommand("ddraw.text", drawDuration, Color.red, targetPoint, string.Format(lang.GetMessage("PetTargetPreview", this, owner.UserIDString), GetTargetName(target)));
            owner.SendConsoleCommand("ddraw.line", drawDuration, Color.red, owner.eyes.position, GetAimPoint(target));
        }

        private bool TryAcquireAggressiveTarget(BasePlayer owner, PetState state)
        {
            var target = FindNearestHostilePlayer(owner, state);
            return target != null && _combat.TryStartAttack(owner, state, target, false);
        }

        private BasePlayer FindNearestHostilePlayer(BasePlayer owner, PetState state)
        {
            BasePlayer bestTarget = null;
            var bestDistance = float.MaxValue;
            var ownerPosition = owner.transform.position;
            var petPosition = state.Entity.transform.position;

            for (var index = 0; index < BasePlayer.activePlayerList.Count; index++)
            {
                var candidate = BasePlayer.activePlayerList[index];
                if (candidate == null || candidate.userID == owner.userID || !candidate.IsConnected || candidate.IsSleeping())
                {
                    continue;
                }

                if (IsFriendly(owner.userID, candidate))
                {
                    continue;
                }

                var ownerDistance = Vector3.Distance(ownerPosition, candidate.transform.position);
                var petDistance = Vector3.Distance(petPosition, candidate.transform.position);
                var effectiveDistance = Mathf.Min(ownerDistance, petDistance);
                if (effectiveDistance > _config.Combat.ActivationRange || effectiveDistance >= bestDistance)
                {
                    continue;
                }

                bestDistance = effectiveDistance;
                bestTarget = candidate;
            }

            return bestTarget;
        }

        private void TryDrawPetWorldUi(BasePlayer owner, PetState state, float ownerDistance, float now)
        {
            if (!_config.Ui.Enabled || owner == null || state == null || !IsValidPet(state.Entity) || ownerDistance > _config.Ui.MaxOwnerDistance)
            {
                return;
            }

            if (now - state.LastUiDrawTime < _config.Ui.DrawInterval)
            {
                return;
            }

            state.LastUiDrawTime = now;
            var progress = GetProgress(state.OwnerId);
            var drawDuration = _config.Ui.DrawInterval + 0.1f;
            var basePosition = state.Entity.transform.position + Vector3.up * _config.Ui.VerticalOffset;
            owner.SendConsoleCommand(
                "ddraw.text",
                drawDuration,
                Color.cyan,
                basePosition,
                string.Format(lang.GetMessage("PetUiNameLabel", this, owner.UserIDString), GetDisplayName(state.PetType), GetPetLevel(progress)));
        }

        private void UpdatePetVitals(PetState state, float now, float thinkInterval)
        {
            if (state == null)
            {
                return;
            }

            if (state.LastVitalsUpdateTime <= 0f)
            {
                state.LastVitalsUpdateTime = now;
                return;
            }

            var deltaTime = Mathf.Max(thinkInterval, now - state.LastVitalsUpdateTime);
            state.LastVitalsUpdateTime = now;
            state.Hunger = Mathf.Clamp(state.Hunger - (_config.Training.HungerDrainPerMinute / 60f) * deltaTime, 0f, GetPetMaxHunger(state.OwnerId));
            state.Stamina = Mathf.Clamp(state.Stamina - (_config.Training.ThirstDrainPerMinute / 60f) * deltaTime, 0f, GetPetMaxThirst(state.OwnerId));

            var moving = state.SmoothedVelocity.magnitude > 0.35f || state.State == CompanionState.Attack;
            if (moving)
            {
                state.Stamina = Mathf.Clamp(state.Stamina - _config.Training.StaminaDrainMovePerSecond * deltaTime, 0f, GetPetMaxThirst(state.OwnerId));
                RegisterSpeedTraining(state, state.SmoothedVelocity.magnitude * deltaTime);
            }

            RegisterVitalityTraining(state, deltaTime);
            TryAutoConsumeFromBag(state, now);
        }

        private PetProgress GetProgress(ulong ownerId)
        {
            if (!_storedData.ProgressByOwner.TryGetValue(ownerId, out var progress) || progress == null)
            {
                progress = new PetProgress();
                _storedData.ProgressByOwner[ownerId] = progress;
            }

            return progress;
        }

        private List<PetBagEntry> GetOrCreateBag(ulong ownerId)
        {
            if (!_storedData.BagByOwner.TryGetValue(ownerId, out var bag) || bag == null)
            {
                bag = new List<PetBagEntry>();
                _storedData.BagByOwner[ownerId] = bag;
            }

            return bag;
        }

        private bool TryAddBagItem(ulong ownerId, string shortname, int amount)
        {
            if (string.IsNullOrWhiteSpace(shortname) || amount <= 0)
            {
                return false;
            }

            var bag = GetOrCreateBag(ownerId);
            for (var index = 0; index < bag.Count; index++)
            {
                var entry = bag[index];
                if (!string.Equals(entry.Shortname, shortname, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                entry.Amount += amount;
                return true;
            }

            if (bag.Count >= _config.Inventory.Capacity)
            {
                return false;
            }

            bag.Add(new PetBagEntry
            {
                Shortname = shortname,
                Amount = amount
            });
            return true;
        }

        private bool TryRemoveBagItem(ulong ownerId, string shortname, int amount, out int removedAmount)
        {
            removedAmount = 0;
            if (string.IsNullOrWhiteSpace(shortname) || amount <= 0)
            {
                return false;
            }

            var bag = GetOrCreateBag(ownerId);
            for (var index = 0; index < bag.Count; index++)
            {
                var entry = bag[index];
                if (!string.Equals(entry.Shortname, shortname, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                removedAmount = Mathf.Clamp(amount, 1, entry.Amount);
                entry.Amount -= removedAmount;
                if (entry.Amount <= 0)
                {
                    bag.RemoveAt(index);
                }

                return true;
            }

            return false;
        }

        private bool IsSupportedBagItem(string shortname)
        {
            return _config.Inventory.FoodRestore.ContainsKey(shortname) || _config.Inventory.WaterRestore.ContainsKey(shortname);
        }

        private string GetItemDisplayName(string shortname)
        {
            var definition = ItemManager.FindItemDefinition(shortname);
            return definition?.displayName?.english ?? shortname;
        }

        private void TryAutoConsumeFromBag(PetState state, float now)
        {
            if (state == null || now - state.LastBagConsumeTime < _config.Inventory.AutoConsumeCooldown)
            {
                return;
            }

            if (TryAutoConsumeFromGround(state, now))
            {
                return;
            }

            if (!HasEquippedBag(state.OwnerId))
            {
                return;
            }

            if (GetPetHungerPercent(state) <= _config.Inventory.AutoEatThreshold && TryConsumeBagResource(state, _config.Inventory.FoodRestore, true, out var foodItem, out var foodRestore))
            {
                state.LastBagConsumeTime = now;
                NotifyBagConsumption(state.OwnerId, foodItem, $"fome +{foodRestore:0}");
                return;
            }

            if (GetPetThirstPercent(state) <= _config.Inventory.AutoDrinkThreshold && TryConsumeBagResource(state, _config.Inventory.WaterRestore, false, out var drinkItem, out var drinkRestore))
            {
                state.LastBagConsumeTime = now;
                NotifyBagConsumption(state.OwnerId, drinkItem, $"sede +{drinkRestore:0}");
            }
        }

        private bool TryConsumeBagResource(PetState state, Dictionary<string, float> restoreMap, bool isFood, out string consumedItem, out float restoredValue)
        {
            consumedItem = null;
            restoredValue = 0f;

            if (state == null || restoreMap == null || restoreMap.Count == 0)
            {
                return false;
            }

            var bag = GetOrCreateBag(state.OwnerId);
            PetBagEntry bestEntry = null;
            float bestRestore = 0f;
            for (var index = 0; index < bag.Count; index++)
            {
                var entry = bag[index];
                if (entry.Amount <= 0 || !restoreMap.TryGetValue(entry.Shortname, out var restoreValue))
                {
                    continue;
                }

                if (restoreValue <= bestRestore)
                {
                    continue;
                }

                bestEntry = entry;
                bestRestore = restoreValue;
            }

            if (bestEntry == null)
            {
                return false;
            }

            bestEntry.Amount -= 1;
            if (bestEntry.Amount <= 0)
            {
                bag.Remove(bestEntry);
            }

            if (isFood)
            {
                state.Hunger = Mathf.Clamp(state.Hunger + bestRestore, 0f, GetPetMaxHunger(state.OwnerId));
            }
            else
            {
                state.Stamina = Mathf.Clamp(state.Stamina + bestRestore, 0f, GetPetMaxThirst(state.OwnerId));
            }

            consumedItem = bestEntry.Shortname;
            restoredValue = bestRestore;
            SaveData();
            return true;
        }

        private bool TryAutoConsumeFromGround(PetState state, float now)
        {
            if (state == null)
            {
                return false;
            }

            if (GetPetHungerPercent(state) <= _config.Inventory.AutoEatThreshold && TryConsumeGroundResource(state, _config.Inventory.FoodRestore, true, out var foodItem, out var foodRestore))
            {
                state.LastBagConsumeTime = now;
                NotifyGroundConsumption(state.OwnerId, foodItem, $"fome +{foodRestore:0}");
                return true;
            }

            if (GetPetThirstPercent(state) <= _config.Inventory.AutoDrinkThreshold && TryConsumeGroundResource(state, _config.Inventory.WaterRestore, false, out var drinkItem, out var drinkRestore))
            {
                state.LastBagConsumeTime = now;
                NotifyGroundConsumption(state.OwnerId, drinkItem, $"sede +{drinkRestore:0}");
                return true;
            }

            return false;
        }

        private bool TryConsumeGroundResource(PetState state, Dictionary<string, float> restoreMap, bool isFood, out string consumedItem, out float restoredValue)
        {
            consumedItem = null;
            restoredValue = 0f;
            if (state?.Entity == null || restoreMap == null || restoreMap.Count == 0)
            {
                return false;
            }

            _entityScanBuffer.Clear();
            Vis.Entities(state.Entity.transform.position, _config.Inventory.GroundFeedRadius, _entityScanBuffer);

            DroppedItem bestDrop = null;
            var bestRestore = 0f;
            for (var index = 0; index < _entityScanBuffer.Count; index++)
            {
                var droppedItem = _entityScanBuffer[index] as DroppedItem;
                if (droppedItem?.item?.info == null)
                {
                    continue;
                }

                var shortname = droppedItem.item.info.shortname;
                if (!restoreMap.TryGetValue(shortname, out var restoreValue) || restoreValue <= bestRestore)
                {
                    continue;
                }

                bestDrop = droppedItem;
                bestRestore = restoreValue;
            }

            _entityScanBuffer.Clear();

            if (bestDrop?.item == null)
            {
                return false;
            }

            var sourceItem = bestDrop.item;
            if (isFood)
            {
                state.Hunger = Mathf.Clamp(state.Hunger + bestRestore, 0f, GetPetMaxHunger(state.OwnerId));
            }
            else
            {
                state.Stamina = Mathf.Clamp(state.Stamina + bestRestore, 0f, GetPetMaxThirst(state.OwnerId));
            }

            consumedItem = sourceItem.info.shortname;
            restoredValue = bestRestore;

            if (sourceItem.amount > 1)
            {
                sourceItem.UseItem(1);
                bestDrop.SendNetworkUpdateImmediate();
                return true;
            }

            sourceItem.RemoveFromWorld();
            sourceItem.Remove();
            if (!bestDrop.IsDestroyed)
            {
                bestDrop.Kill();
            }

            return true;
        }

        private void NotifyBagConsumption(ulong ownerId, string shortname, string effect)
        {
            var owner = BasePlayer.FindByID(ownerId);
            if (owner == null)
            {
                return;
            }

            Reply(owner, "BagAutoConsumed", GetItemDisplayName(shortname), effect);
        }

        private void NotifyGroundConsumption(ulong ownerId, string shortname, string effect)
        {
            var owner = BasePlayer.FindByID(ownerId);
            if (owner == null)
            {
                return;
            }

            Reply(owner, "GroundAutoConsumed", GetItemDisplayName(shortname), effect);
        }

        private void RegisterSpeedTraining(PetState state, float traveledDistance)
        {
            if (state == null || traveledDistance <= 0.01f)
            {
                return;
            }

            var progress = GetProgress(state.OwnerId);
            progress.SpeedXp += traveledDistance * _config.Training.SpeedXpPerMeter;
            TryLevelUpTraining(state.OwnerId, PetTrainingType.Speed, progress);
        }

        private void RegisterAttackTraining(PetState state)
        {
            if (state == null)
            {
                return;
            }

            state.Stamina = Mathf.Clamp(state.Stamina - _config.Training.StaminaDrainAttack, 0f, GetPetMaxThirst(state.OwnerId));

            var progress = GetProgress(state.OwnerId);
            progress.AttackXp += _config.Training.AttackXpPerHit;
            TryLevelUpTraining(state.OwnerId, PetTrainingType.Attack, progress);
        }

        private void RegisterDefenseTraining(PetState state, float damageTaken)
        {
            if (state == null || damageTaken <= 0.01f)
            {
                return;
            }

            var progress = GetProgress(state.OwnerId);
            progress.DefenseXp += damageTaken * _config.Training.DefenseXpPerDamage;
            TryLevelUpTraining(state.OwnerId, PetTrainingType.Defense, progress);
        }

        private void RegisterVitalityTraining(PetState state, float deltaTime)
        {
            if (state == null || deltaTime <= 0.01f)
            {
                return;
            }

            var progress = GetProgress(state.OwnerId);
            progress.VitalityXp += (deltaTime / 60f) * _config.Training.VitalityXpPerMinute;
            TryLevelUpTraining(state.OwnerId, PetTrainingType.Vitality, progress);
        }

        private void TryLevelUpTraining(ulong ownerId, PetTrainingType trainingType, PetProgress progress)
        {
            if (progress == null)
            {
                return;
            }

            var owner = BasePlayer.FindByID(ownerId);
            if (trainingType == PetTrainingType.Vitality)
            {
                while (progress.VitalityXp >= _config.Training.VitalityXpPerLevel)
                {
                    progress.VitalityXp -= _config.Training.VitalityXpPerLevel;
                    progress.VitalityLevel++;
                    if (_pets.TryGetValue(ownerId, out var vitalityState) && IsValidPet(vitalityState.Entity))
                    {
                        vitalityState.Hunger = Mathf.Min(GetPetMaxHunger(ownerId), vitalityState.Hunger + _config.Training.CapacityBonusPerLevel);
                        vitalityState.Stamina = Mathf.Min(GetPetMaxThirst(ownerId), vitalityState.Stamina + _config.Training.CapacityBonusPerLevel);
                    }

                    if (owner != null)
                    {
                        Reply(owner, "PetTrainingLevelUp", "vitalidade", progress.VitalityLevel);
                    }
                }

                return;
            }

            if (trainingType == PetTrainingType.Speed)
            {
                while (progress.SpeedXp >= _config.Training.SpeedXpPerLevel)
                {
                    progress.SpeedXp -= _config.Training.SpeedXpPerLevel;
                    progress.SpeedLevel++;
                    if (owner != null)
                    {
                        Reply(owner, "PetTrainingLevelUp", "velocidade", progress.SpeedLevel);
                    }
                }

                return;
            }

            if (trainingType == PetTrainingType.Defense)
            {
                while (progress.DefenseXp >= _config.Training.DefenseXpPerLevel)
                {
                    progress.DefenseXp -= _config.Training.DefenseXpPerLevel;
                    progress.DefenseLevel++;
                    if (owner != null)
                    {
                        Reply(owner, "PetTrainingLevelUp", "defesa", progress.DefenseLevel);
                    }
                }

                return;
            }

            while (progress.AttackXp >= _config.Training.AttackXpPerLevel)
            {
                progress.AttackXp -= _config.Training.AttackXpPerLevel;
                progress.AttackLevel++;
                if (owner != null)
                {
                    Reply(owner, "PetTrainingLevelUp", "ataque", progress.AttackLevel);
                }
            }
        }

        private float GetPetAttackDamage(PetState state)
        {
            var progress = GetProgress(state.OwnerId);
            var levelBonus = 1f + Mathf.Max(0, progress.AttackLevel - 1) * _config.Training.AttackBonusPerLevel;
            var thirstFactor = Mathf.Lerp(0.82f, 1f, GetPetThirstPercent(state) / 100f);
            var hungerPercent = GetPetHungerPercent(state);
            var hungerFactor = hungerPercent >= _config.Training.LowHungerThreshold
                ? 1f
                : Mathf.Lerp(0.85f, 1f, hungerPercent / _config.Training.LowHungerThreshold);
            return _config.Combat.Damage * levelBonus * thirstFactor * hungerFactor;
        }

        private float GetPetAttackCooldown(PetState state)
        {
            var progress = GetProgress(state.OwnerId);
            var levelFactor = 1f - Mathf.Max(0, progress.AttackLevel - 1) * _config.Training.AttackSpeedBonusPerLevel;
            var thirstFactor = Mathf.Lerp(1.12f, 1f, GetPetThirstPercent(state) / 100f);
            var hungerPercent = GetPetHungerPercent(state);
            var hungerFactor = hungerPercent >= _config.Training.LowHungerThreshold
                ? 1f
                : Mathf.Lerp(1.15f, 1f, hungerPercent / _config.Training.LowHungerThreshold);
            return Mathf.Max(0.35f, _config.Combat.Cooldown * Mathf.Max(0.45f, levelFactor) * thirstFactor * hungerFactor);
        }

        private float GetPetMoveSpeed(PetState state, float baseSpeed)
        {
            var progress = GetProgress(state.OwnerId);
            var levelBonus = 1f + Mathf.Max(0, progress.SpeedLevel - 1) * _config.Training.SpeedBonusPerLevel;
            var thirstFactor = Mathf.Lerp(0.78f, 1f, GetPetThirstPercent(state) / 100f);
            var hungerPercent = GetPetHungerPercent(state);
            var hungerFactor = hungerPercent >= _config.Training.LowHungerThreshold
                ? 1f
                : Mathf.Lerp(0.82f, 1f, hungerPercent / _config.Training.LowHungerThreshold);
            return baseSpeed * levelBonus * thirstFactor * hungerFactor;
        }

        private float GetPetDefenseReduction(PetState state)
        {
            var progress = GetProgress(state.OwnerId);
            var levelReduction = Mathf.Max(0, progress.DefenseLevel - 1) * _config.Training.DefenseBonusPerLevel;
            var sustainFactor = Mathf.Lerp(0.78f, 1f, Mathf.Min(GetPetHungerPercent(state), GetPetThirstPercent(state)) / 100f);
            return Mathf.Clamp(levelReduction * sustainFactor, 0f, 0.6f);
        }

        private float GetPetMaxHunger(ulong ownerId)
        {
            var progress = GetProgress(ownerId);
            return 100f + Mathf.Max(0, progress.VitalityLevel - 1) * _config.Training.CapacityBonusPerLevel;
        }

        private float GetPetMaxThirst(ulong ownerId)
        {
            var progress = GetProgress(ownerId);
            return 100f + Mathf.Max(0, progress.VitalityLevel - 1) * _config.Training.CapacityBonusPerLevel;
        }

        private float GetPetHungerPercent(PetState state)
        {
            return state == null ? 0f : (state.Hunger / Mathf.Max(1f, GetPetMaxHunger(state.OwnerId))) * 100f;
        }

        private float GetPetThirstPercent(PetState state)
        {
            return state == null ? 0f : (state.Stamina / Mathf.Max(1f, GetPetMaxThirst(state.OwnerId))) * 100f;
        }

        private int GetPetLevel(PetProgress progress)
        {
            if (progress == null)
            {
                return 1;
            }

            return 1
                + Mathf.Max(0, progress.SpeedLevel - 1)
                + Mathf.Max(0, progress.AttackLevel - 1)
                + Mathf.Max(0, progress.DefenseLevel - 1)
                + Mathf.Max(0, progress.VitalityLevel - 1);
        }

        private bool AddAlly(ulong ownerId, ulong allyId)
        {
            var allies = GetOrCreateAllies(ownerId);
            if (allies.Contains(allyId))
            {
                return false;
            }

            allies.Add(allyId);
            _storedData.AlliesByOwner[ownerId] = allies;
            return true;
        }

        private void RemoveAlly(ulong ownerId, ulong allyId)
        {
            var allies = GetOrCreateAllies(ownerId);
            allies.Remove(allyId);
            _storedData.AlliesByOwner[ownerId] = allies;
        }

        private List<ulong> GetOrCreateAllies(ulong ownerId)
        {
            if (!_storedData.AlliesByOwner.TryGetValue(ownerId, out var allies) || allies == null)
            {
                allies = new List<ulong>();
                _storedData.AlliesByOwner[ownerId] = allies;
            }

            return allies;
        }

        private bool IsAlliedWith(ulong ownerId, ulong otherId)
        {
            return _storedData.AlliesByOwner.TryGetValue(ownerId, out var allies) && allies != null && allies.Contains(otherId);
        }

        private BasePlayer ResolvePlayerForAllyCommand(BasePlayer owner, string[] args)
        {
            if (args.Length > 2)
            {
                return FindPlayerByNameOrId(string.Join(" ", args.Skip(2).ToArray()));
            }

            return FindLookPlayer(owner);
        }

        private BasePlayer FindPlayerByNameOrId(string search)
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                return null;
            }

            if (ulong.TryParse(search, out var playerId))
            {
                return FindPlayerById(playerId);
            }

            BasePlayer match = null;
            for (var index = 0; index < BasePlayer.activePlayerList.Count; index++)
            {
                var candidate = BasePlayer.activePlayerList[index];
                if (candidate == null)
                {
                    continue;
                }

                if (string.Equals(candidate.displayName, search, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }

                if (candidate.displayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    match = candidate;
                }
            }

            return match;
        }

        private BasePlayer FindPlayerById(ulong playerId)
        {
            return BasePlayer.FindByID(playerId) ?? BasePlayer.FindSleeping(playerId);
        }

        private string FormatAggression(PetAggression aggression)
        {
            return aggression == PetAggression.Passive ? "passivo" : "agressivo";
        }

        private string FormatAiTier(PetAiTier aiTier)
        {
            switch (aiTier)
            {
                case PetAiTier.Simplified:
                    return "simplificada";
                case PetAiTier.Sleeping:
                    return "adormecida";
                default:
                    return "completa";
            }
        }

        private string GetTargetName(BaseCombatEntity entity)
        {
            if (entity is BasePlayer player)
            {
                return player.displayName;
            }

            return entity.ShortPrefabName;
        }

        // Wrapper unico de resposta para manter todas as mensagens localizadas e com o mesmo prefixo do plugin.
        private void Reply(BasePlayer player, string key, params object[] args)
        {
            if (player == null)
            {
                return;
            }

            var message = lang.GetMessage(key, this, player.UserIDString);
            player.ChatMessage($"[MarolaPets] {string.Format(message, args)}");
        }
    }
}