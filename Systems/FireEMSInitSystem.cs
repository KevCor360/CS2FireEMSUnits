using FireEMS.Components;
using FireEMS.Settings;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Objects;
using Game.Vehicles;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace FireEMS.Systems
{
    [BurstCompile]
    public partial class FireEMSInitSystem : GameSystemBase
    {
        private EntityQuery m_UninitializedQuery;
        private EntityQuery m_AllActiveQuery;
        private EndFrameBarrier m_EndFrameBarrier;
        private TypeHandle m_TypeHandle;
        private bool m_SettingsDirty;

        [Preserve]
        public FireEMSInitSystem() { }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();

            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();

            // OwnedVehicle required in both queries so we only touch stations that
            // have completed construction and have their vehicle buffer allocated.
            m_UninitializedQuery = GetEntityQuery(new EntityQueryDesc
            {
                All  = new[]
                {
                    ComponentType.ReadOnly<FireStation>(),
                    ComponentType.ReadOnly<OwnedVehicle>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<FireEMSData>(),
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            });

            m_AllActiveQuery = GetEntityQuery(new EntityQueryDesc
            {
                All  = new[]
                {
                    ComponentType.ReadOnly<FireStation>(),
                    ComponentType.ReadOnly<OwnedVehicle>(),
                    ComponentType.ReadWrite<FireEMSData>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            });

            m_TypeHandle.__AssignHandles(ref CheckedStateRef);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            bool dirty = m_SettingsDirty;
            m_SettingsDirty = false;

            bool hasUninitialized = !m_UninitializedQuery.IsEmptyIgnoreFilter;

            if (!dirty && !hasUninitialized)
                return;

            FireEMSSettings settings = FireEMSMod.Settings;
            bool useCustom    = settings.UseCustomRatio;
            int  minUnits     = settings.MinimumUnitsPerStation;
            int  enginesPerU  = math.max(1, settings.EnginesPerAdditionalUnit);
            int  maxUnits     = settings.MaximumUnitsPerStation;

            m_TypeHandle.__Unity_Entities_Entity_TypeHandle.Update(ref CheckedStateRef);
            m_TypeHandle.__Game_Objects_OwnedVehicle_RO_BufferTypeHandle.Update(ref CheckedStateRef);
            m_TypeHandle.__FireEMS_Components_FireEMSData_RW_ComponentTypeHandle.Update(ref CheckedStateRef);
            m_TypeHandle.__Game_Vehicles_FireEngine_RO_ComponentLookup.Update(ref CheckedStateRef);

            if (hasUninitialized)
            {
                EntityCommandBuffer ecb = m_EndFrameBarrier.CreateCommandBuffer();

                var initJob = new FireEMSAddDataJob
                {
                    m_EntityType       = m_TypeHandle.__Unity_Entities_Entity_TypeHandle,
                    m_OwnedVehicleType = m_TypeHandle.__Game_Objects_OwnedVehicle_RO_BufferTypeHandle,
                    m_FireEngineData   = m_TypeHandle.__Game_Vehicles_FireEngine_RO_ComponentLookup,
                    m_CommandBuffer    = ecb.AsParallelWriter(),
                    m_UseCustomRatio   = useCustom,
                    m_MinUnits         = minUnits,
                    m_EnginesPerUnit   = enginesPerU,
                    m_MaxUnits         = maxUnits,
                };

                JobHandle handle = initJob.ScheduleParallel(m_UninitializedQuery, Dependency);
                m_EndFrameBarrier.AddJobHandleForProducer(handle);
                Dependency = handle;
            }

            if (dirty && !m_AllActiveQuery.IsEmptyIgnoreFilter)
            {
                var updateJob = new FireEMSUpdateCapacityJob
                {
                    m_OwnedVehicleType = m_TypeHandle.__Game_Objects_OwnedVehicle_RO_BufferTypeHandle,
                    m_FireEMSDataType  = m_TypeHandle.__FireEMS_Components_FireEMSData_RW_ComponentTypeHandle,
                    m_FireEngineData   = m_TypeHandle.__Game_Vehicles_FireEngine_RO_ComponentLookup,
                    m_UseCustomRatio   = useCustom,
                    m_MinUnits         = minUnits,
                    m_EnginesPerUnit   = enginesPerU,
                    m_MaxUnits         = maxUnits,
                };

                Dependency = updateJob.ScheduleParallel(m_AllActiveQuery, Dependency);
            }
        }

        [Preserve]
        protected override void OnDestroy()
        {
            base.OnDestroy();
        }

        public void MarkSettingsDirty() => m_SettingsDirty = true;

        // ── Jobs ──────────────────────────────────────────────────────────────

        [BurstCompile]
        private struct FireEMSAddDataJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                   m_EntityType;
            [ReadOnly] public BufferTypeHandle<OwnedVehicle>     m_OwnedVehicleType;
            [ReadOnly] public ComponentLookup<FireEngine>        m_FireEngineData;
            public EntityCommandBuffer.ParallelWriter            m_CommandBuffer;
            public bool m_UseCustomRatio;
            public int  m_MinUnits;
            public int  m_EnginesPerUnit;
            public int  m_MaxUnits;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 enabledMask)
            {
                NativeArray<Entity>           entities     = chunk.GetNativeArray(m_EntityType);
                BufferAccessor<OwnedVehicle>  ownedBuffers = chunk.GetBufferAccessor(ref m_OwnedVehicleType);

                for (int i = 0; i < chunk.Count; i++)
                {
                    int engineCount = CountFireEngines(ownedBuffers[i]);
                    int capacity    = CalcCapacity(engineCount, m_UseCustomRatio, m_MinUnits, m_EnginesPerUnit, m_MaxUnits);

                    m_CommandBuffer.AddComponent(unfilteredChunkIndex, entities[i], new FireEMSData
                    {
                        m_AmbulanceCapacity  = capacity,
                        m_AvailableAmbulances = 0,
                        m_TargetRequest      = Entity.Null,
                        m_Flags              = FireEMSFlags.None,
                    });
                }
            }

            private int CountFireEngines(DynamicBuffer<OwnedVehicle> owned)
            {
                int count = 0;
                for (int j = 0; j < owned.Length; j++)
                {
                    if (m_FireEngineData.HasComponent(owned[j].m_Vehicle))
                        count++;
                }
                return count;
            }
        }

        [BurstCompile]
        private struct FireEMSUpdateCapacityJob : IJobChunk
        {
            [ReadOnly] public BufferTypeHandle<OwnedVehicle>  m_OwnedVehicleType;
            public ComponentTypeHandle<FireEMSData>            m_FireEMSDataType;
            [ReadOnly] public ComponentLookup<FireEngine>      m_FireEngineData;
            public bool m_UseCustomRatio;
            public int  m_MinUnits;
            public int  m_EnginesPerUnit;
            public int  m_MaxUnits;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 enabledMask)
            {
                NativeArray<FireEMSData>     datas       = chunk.GetNativeArray(ref m_FireEMSDataType);
                BufferAccessor<OwnedVehicle> ownedBuffers = chunk.GetBufferAccessor(ref m_OwnedVehicleType);

                for (int i = 0; i < chunk.Count; i++)
                {
                    int engineCount = CountFireEngines(ownedBuffers[i]);
                    FireEMSData data = datas[i];
                    data.m_AmbulanceCapacity = CalcCapacity(engineCount, m_UseCustomRatio, m_MinUnits, m_EnginesPerUnit, m_MaxUnits);
                    datas[i] = data;
                }
            }

            private int CountFireEngines(DynamicBuffer<OwnedVehicle> owned)
            {
                int count = 0;
                for (int j = 0; j < owned.Length; j++)
                {
                    if (m_FireEngineData.HasComponent(owned[j].m_Vehicle))
                        count++;
                }
                return count;
            }
        }

        // Shared formula — inlined by Burst from both job contexts.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static int CalcCapacity(int engineCount, bool useCustom, int minUnits, int enginesPerUnit, int maxUnits)
        {
            if (!useCustom)
                return 1 + (engineCount / 4);

            return math.clamp(minUnits + (engineCount / enginesPerUnit), minUnits, maxUnits);
        }

        // ── TypeHandle ────────────────────────────────────────────────────────

        private struct TypeHandle
        {
            [ReadOnly] public EntityTypeHandle                   __Unity_Entities_Entity_TypeHandle;
            [ReadOnly] public BufferTypeHandle<OwnedVehicle>     __Game_Objects_OwnedVehicle_RO_BufferTypeHandle;
            public ComponentTypeHandle<FireEMSData>               __FireEMS_Components_FireEMSData_RW_ComponentTypeHandle;
            [ReadOnly] public ComponentLookup<FireEngine>         __Game_Vehicles_FireEngine_RO_ComponentLookup;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void __AssignHandles(ref SystemState state)
            {
                __Unity_Entities_Entity_TypeHandle                        = state.GetEntityTypeHandle();
                __Game_Objects_OwnedVehicle_RO_BufferTypeHandle           = state.GetBufferTypeHandle<OwnedVehicle>(isReadOnly: true);
                __FireEMS_Components_FireEMSData_RW_ComponentTypeHandle   = state.GetComponentTypeHandle<FireEMSData>(isReadOnly: false);
                __Game_Vehicles_FireEngine_RO_ComponentLookup             = state.GetComponentLookup<FireEngine>(isReadOnly: true);
            }
        }
    }
}
