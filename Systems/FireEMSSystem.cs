using FireEMS.Components;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Events;
using Game.Healthcare;
using Game.Objects;
using Game.Simulation;
using Game.Vehicles;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using math = Unity.Mathematics.math;

namespace FireEMS.Systems
{
    [BurstCompile]
    public partial class FireEMSSystem : GameSystemBase
    {
        private const uint k_UpdateInterval = 256u;
        private const uint k_UpdateOffset   = 32u;

        private SimulationSystem              m_SimulationSystem;
        private EndFrameBarrier              m_EndFrameBarrier;
        private HealthcareVehicleSelectData  m_VehicleSelectData;
        private NativeQueue<FireEMSAction>   m_ActionQueue;
        private TypeHandle                   m_TypeHandle;
        private EntityQuery                  m_StationQuery;

        [Preserve]
        public FireEMSSystem() { }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();

            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_EndFrameBarrier  = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_VehicleSelectData = new HealthcareVehicleSelectData(this);
            m_ActionQueue = new NativeQueue<FireEMSAction>(Allocator.Persistent);

            m_StationQuery = GetEntityQuery(new EntityQueryDesc
            {
                All  = new[]
                {
                    ComponentType.ReadOnly<FireStation>(),
                    ComponentType.ReadWrite<FireEMSData>(),
                    ComponentType.ReadWrite<ServiceDispatch>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            });

            RequireForUpdate(m_StationQuery);

            m_TypeHandle.__AssignHandles(ref CheckedStateRef);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if ((m_SimulationSystem.frameIndex % k_UpdateInterval) != k_UpdateOffset)
                return;

            m_TypeHandle.__Unity_Entities_Entity_TypeHandle.Update(ref CheckedStateRef);
            m_TypeHandle.__Game_Objects_Transform_RO_ComponentTypeHandle.Update(ref CheckedStateRef);
            m_TypeHandle.__FireEMS_Components_FireEMSData_RW_ComponentTypeHandle.Update(ref CheckedStateRef);
            m_TypeHandle.__Game_Objects_OwnedVehicle_RO_BufferTypeHandle.Update(ref CheckedStateRef);
            m_TypeHandle.__Game_Buildings_ServiceDispatch_RW_BufferTypeHandle.Update(ref CheckedStateRef);
            m_TypeHandle.__Game_Vehicles_Ambulance_RO_ComponentLookup.Update(ref CheckedStateRef);
            m_TypeHandle.__Game_Vehicles_ParkedCar_RO_ComponentLookup.Update(ref CheckedStateRef);
            m_TypeHandle.__Game_Common_Target_RO_ComponentLookup.Update(ref CheckedStateRef);
            m_TypeHandle.__Game_Healthcare_HealthcareRequest_RO_ComponentLookup.Update(ref CheckedStateRef);
            m_TypeHandle.__Game_Objects_Owner_RO_ComponentLookup.Update(ref CheckedStateRef);

            m_VehicleSelectData.PreUpdate(this, Dependency, out JobHandle selectPreHandle);

            EntityCommandBuffer.ParallelWriter parallelEcb = m_EndFrameBarrier.CreateCommandBuffer().AsParallelWriter();

            var tickJob = new FireEMSTickJob
            {
                m_EntityType                = m_TypeHandle.__Unity_Entities_Entity_TypeHandle,
                m_TransformType             = m_TypeHandle.__Game_Objects_Transform_RO_ComponentTypeHandle,
                m_FireEMSDataType           = m_TypeHandle.__FireEMS_Components_FireEMSData_RW_ComponentTypeHandle,
                m_OwnedVehicleType          = m_TypeHandle.__Game_Objects_OwnedVehicle_RO_BufferTypeHandle,
                m_ServiceDispatchType       = m_TypeHandle.__Game_Buildings_ServiceDispatch_RW_BufferTypeHandle,
                m_AmbulanceData             = m_TypeHandle.__Game_Vehicles_Ambulance_RO_ComponentLookup,
                m_ParkedCarData             = m_TypeHandle.__Game_Vehicles_ParkedCar_RO_ComponentLookup,
                m_TargetData                = m_TypeHandle.__Game_Common_Target_RO_ComponentLookup,
                m_HealthcareRequestData     = m_TypeHandle.__Game_Healthcare_HealthcareRequest_RO_ComponentLookup,
                m_OwnerData                 = m_TypeHandle.__Game_Objects_Owner_RO_ComponentLookup,
                m_CommandBuffer             = parallelEcb,
                m_ActionQueue               = m_ActionQueue.AsParallelWriter(),
                m_VehicleSelectData         = m_VehicleSelectData,
                m_SimulationFrame           = m_SimulationSystem.frameIndex,
            };

            JobHandle tickHandle = tickJob.ScheduleParallel(m_StationQuery, JobHandle.CombineDependencies(Dependency, selectPreHandle));

            m_VehicleSelectData.PostUpdate(this, tickHandle, out JobHandle selectPostHandle);

            // Non-parallel ECB for the action drain job (structural changes only, from queue).
            EntityCommandBuffer actionEcb = m_EndFrameBarrier.CreateCommandBuffer();

            var actionJob = new FireEMSActionJob
            {
                m_ActionQueue  = m_ActionQueue,
                m_CommandBuffer = actionEcb,
                m_AmbulanceData = m_TypeHandle.__Game_Vehicles_Ambulance_RO_ComponentLookup,
            };

            JobHandle actionHandle = actionJob.Schedule(JobHandle.CombineDependencies(tickHandle, selectPostHandle));
            m_EndFrameBarrier.AddJobHandleForProducer(actionHandle);

            Dependency = actionHandle;
        }

        [Preserve]
        protected override void OnDestroy()
        {
            if (m_ActionQueue.IsCreated)
                m_ActionQueue.Dispose();
            base.OnDestroy();
        }

        // ── FireEMSAction ─────────────────────────────────────────────────────

        public struct FireEMSAction
        {
            public Entity m_Vehicle;
            public bool   m_Enable;
        }

        // ── FireEMSTickJob ────────────────────────────────────────────────────

        [BurstCompile]
        private unsafe struct FireEMSTickJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                       m_EntityType;
            [ReadOnly] public ComponentTypeHandle<Game.Objects.Transform> m_TransformType;
            public ComponentTypeHandle<FireEMSData>                   m_FireEMSDataType;
            [ReadOnly] public BufferTypeHandle<OwnedVehicle>          m_OwnedVehicleType;
            public BufferTypeHandle<ServiceDispatch>                   m_ServiceDispatchType;

            [ReadOnly] public ComponentLookup<Ambulance>              m_AmbulanceData;
            [ReadOnly] public ComponentLookup<ParkedCar>              m_ParkedCarData;
            [ReadOnly] public ComponentLookup<Target>                 m_TargetData;
            [ReadOnly] public ComponentLookup<HealthcareRequest>      m_HealthcareRequestData;
            [ReadOnly] public ComponentLookup<Owner>                  m_OwnerData;

            public EntityCommandBuffer.ParallelWriter                 m_CommandBuffer;
            public NativeQueue<FireEMSAction>.ParallelWriter          m_ActionQueue;
            public HealthcareVehicleSelectData                        m_VehicleSelectData;
            public uint m_SimulationFrame;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 enabledMask)
            {
                NativeArray<Entity>               entities         = chunk.GetNativeArray(m_EntityType);
                NativeArray<Game.Objects.Transform> transforms      = chunk.GetNativeArray(ref m_TransformType);
                NativeArray<FireEMSData>           emsDataArray    = chunk.GetNativeArray(ref m_FireEMSDataType);
                BufferAccessor<OwnedVehicle>       ownedAccessor   = chunk.GetBufferAccessor(ref m_OwnedVehicleType);
                BufferAccessor<ServiceDispatch>    dispatchAccessor = chunk.GetBufferAccessor(ref m_ServiceDispatchType);

                for (int i = 0; i < chunk.Count; i++)
                {
                    Entity                        stationEntity    = entities[i];
                    Game.Objects.Transform        stationTransform = transforms[i];
                    FireEMSData                   emsData          = emsDataArray[i];
                    DynamicBuffer<OwnedVehicle>   ownedVehicles    = ownedAccessor[i];
                    DynamicBuffer<ServiceDispatch> dispatches       = dispatchAccessor[i];

                    // ── Build parked-ambulance list ───────────────────────────
                    Entity* parked = stackalloc Entity[ownedVehicles.Length];
                    int parkedCount = 0;

                    for (int j = 0; j < ownedVehicles.Length; j++)
                    {
                        Entity vehicle = ownedVehicles[j].m_Vehicle;
                        if (m_AmbulanceData.HasComponent(vehicle) && m_ParkedCarData.HasComponent(vehicle))
                            parked[parkedCount++] = vehicle;
                    }

                    // ── Trim excess parked ambulances ─────────────────────────
                    // Vehicles parked beyond capacity are disabled via the action queue.
                    for (int j = emsData.m_AmbulanceCapacity; j < parkedCount; j++)
                    {
                        m_ActionQueue.Enqueue(new FireEMSAction { m_Vehicle = parked[j], m_Enable = false });
                    }
                    // Clamp available count to capacity.
                    parkedCount = math.min(parkedCount, emsData.m_AmbulanceCapacity);

                    // ── Process service dispatch requests ─────────────────────
                    int dispatched = 0;
                    for (int j = dispatches.Length - 1; j >= 0 && dispatched < parkedCount; j--)
                    {
                        Entity requestEntity = dispatches[j].m_Request;

                        // Skip stale/fulfilled requests.
                        if (!m_HealthcareRequestData.HasComponent(requestEntity))
                        {
                            dispatches.RemoveAt(j);
                            continue;
                        }

                        // Pop a parked ambulance.
                        Entity ambulanceEntity = parked[--parkedCount];
                        dispatched++;

                        // ── Set Ambulance flags on the vehicle ────────────────
                        Ambulance ambulance = m_AmbulanceData[ambulanceEntity];
                        ambulance.m_Flags |= AmbulanceFlags.Dispatched | AmbulanceFlags.AnyHospital;
                        m_CommandBuffer.SetComponent(unfilteredChunkIndex, ambulanceEntity, ambulance);

                        // ── Target → the healthcare request entity ─────────────
                        m_CommandBuffer.SetComponent(unfilteredChunkIndex, ambulanceEntity, new Target
                        {
                            m_Target = requestEntity,
                        });

                        // ── Write ServiceDispatch buffer on the vehicle ────────
                        DynamicBuffer<ServiceDispatch> vehicleDispatches =
                            m_CommandBuffer.SetBuffer<ServiceDispatch>(unfilteredChunkIndex, ambulanceEntity);
                        vehicleDispatches.Add(new ServiceDispatch { m_Request = requestEntity });

                        // ── HandleRequest event ───────────────────────────────
                        Entity handleEvent = m_CommandBuffer.CreateEntity(unfilteredChunkIndex);
                        m_CommandBuffer.AddComponent(unfilteredChunkIndex, handleEvent, new HandleRequest
                        {
                            m_Request = requestEntity,
                            m_Controller = ambulanceEntity,
                        });

                        // Remove the fulfilled dispatch entry.
                        dispatches.RemoveAt(j);
                    }

                    // ── Spawn new ambulances via CreateVehicle when queue remains ─
                    // (fires when parkedCount hit 0 but there are still dispatches)
                    for (int j = dispatches.Length - 1; j >= 0; j--)
                    {
                        Entity requestEntity = dispatches[j].m_Request;
                        if (!m_HealthcareRequestData.HasComponent(requestEntity))
                        {
                            dispatches.RemoveAt(j);
                            continue;
                        }

                        // CreateVehicle returns the newly spawned entity; Owner + Ambulance
                        // component + Target are set up here to mirror HospitalAISystem.
                        Entity newVehicle = m_VehicleSelectData.CreateVehicle(
                            m_CommandBuffer,
                            unfilteredChunkIndex,
                            stationEntity,
                            stationTransform.m_Position,
                            stationTransform.m_Rotation);

                        if (newVehicle == Entity.Null)
                            break; // Prefab not resolved this frame; retry next tick.

                        // Owner ties the ambulance back to the fire station.
                        m_CommandBuffer.SetComponent(unfilteredChunkIndex, newVehicle, new Owner
                        {
                            m_Owner = stationEntity,
                        });

                        Ambulance newAmbulance = default;
                        newAmbulance.m_Flags = AmbulanceFlags.Dispatched | AmbulanceFlags.AnyHospital;
                        m_CommandBuffer.AddComponent(unfilteredChunkIndex, newVehicle, newAmbulance);

                        m_CommandBuffer.SetComponent(unfilteredChunkIndex, newVehicle, new Target
                        {
                            m_Target = requestEntity,
                        });

                        DynamicBuffer<ServiceDispatch> newDispatches =
                            m_CommandBuffer.SetBuffer<ServiceDispatch>(unfilteredChunkIndex, newVehicle);
                        newDispatches.Add(new ServiceDispatch { m_Request = requestEntity });

                        Entity handleEvent = m_CommandBuffer.CreateEntity(unfilteredChunkIndex);
                        m_CommandBuffer.AddComponent(unfilteredChunkIndex, handleEvent, new HandleRequest
                        {
                            m_Request    = requestEntity,
                            m_Controller = newVehicle,
                        });

                        dispatches.RemoveAt(j);
                    }

                    // ── Update FireEMSData availability ───────────────────────
                    emsData.m_AvailableAmbulances = parkedCount;
                    if (parkedCount > 0)
                        emsData.m_Flags |= FireEMSFlags.HasAvailableAmbulances;
                    else
                        emsData.m_Flags &= ~FireEMSFlags.HasAvailableAmbulances;

                    // ── Reverse HealthcareRequest: advertise availability ──────
                    // When the station has ambulances and no live outbound request, create one
                    // so the pathfinding setup jobs can route patients to this station.
                    bool targetLive = emsData.m_TargetRequest != Entity.Null &&
                                      m_HealthcareRequestData.HasComponent(emsData.m_TargetRequest);

                    if ((emsData.m_Flags & FireEMSFlags.HasAvailableAmbulances) != 0 && !targetLive)
                    {
                        Entity reqEntity = m_CommandBuffer.CreateEntity(unfilteredChunkIndex);
                        m_CommandBuffer.AddComponent(unfilteredChunkIndex, reqEntity, new HealthcareRequest
                        {
                            m_SourceBuilding    = stationEntity,
                            m_SimulationFrame   = m_SimulationFrame,
                            m_Capacity          = emsData.m_AvailableAmbulances,
                        });
                        emsData.m_TargetRequest = reqEntity;
                    }
                    else if (!targetLive)
                    {
                        emsData.m_TargetRequest = Entity.Null;
                    }

                    emsDataArray[i] = emsData;
                }
            }
        }

        // ── FireEMSActionJob ──────────────────────────────────────────────────

        [BurstCompile]
        private struct FireEMSActionJob : IJob
        {
            public NativeQueue<FireEMSAction>             m_ActionQueue;
            public EntityCommandBuffer                    m_CommandBuffer;
            [ReadOnly] public ComponentLookup<Ambulance>  m_AmbulanceData;

            public void Execute()
            {
                while (m_ActionQueue.TryDequeue(out FireEMSAction action))
                {
                    if (!m_AmbulanceData.HasComponent(action.m_Vehicle))
                        continue;

                    if (action.m_Enable)
                        m_CommandBuffer.RemoveComponent<Disabled>(action.m_Vehicle);
                    else
                        m_CommandBuffer.AddComponent<Disabled>(action.m_Vehicle);
                }
            }
        }

        // ── TypeHandle ────────────────────────────────────────────────────────

        private struct TypeHandle
        {
            [ReadOnly] public EntityTypeHandle                           __Unity_Entities_Entity_TypeHandle;
            [ReadOnly] public ComponentTypeHandle<Game.Objects.Transform>  __Game_Objects_Transform_RO_ComponentTypeHandle;
            public ComponentTypeHandle<FireEMSData>                       __FireEMS_Components_FireEMSData_RW_ComponentTypeHandle;
            [ReadOnly] public BufferTypeHandle<OwnedVehicle>              __Game_Objects_OwnedVehicle_RO_BufferTypeHandle;
            public BufferTypeHandle<ServiceDispatch>                       __Game_Buildings_ServiceDispatch_RW_BufferTypeHandle;
            [ReadOnly] public ComponentLookup<Ambulance>                  __Game_Vehicles_Ambulance_RO_ComponentLookup;
            [ReadOnly] public ComponentLookup<ParkedCar>                  __Game_Vehicles_ParkedCar_RO_ComponentLookup;
            [ReadOnly] public ComponentLookup<Target>                     __Game_Common_Target_RO_ComponentLookup;
            [ReadOnly] public ComponentLookup<HealthcareRequest>          __Game_Healthcare_HealthcareRequest_RO_ComponentLookup;
            [ReadOnly] public ComponentLookup<Owner>                      __Game_Objects_Owner_RO_ComponentLookup;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void __AssignHandles(ref SystemState state)
            {
                __Unity_Entities_Entity_TypeHandle                       = state.GetEntityTypeHandle();
                __Game_Objects_Transform_RO_ComponentTypeHandle          = state.GetComponentTypeHandle<Game.Objects.Transform>(isReadOnly: true);
                __FireEMS_Components_FireEMSData_RW_ComponentTypeHandle  = state.GetComponentTypeHandle<FireEMSData>(isReadOnly: false);
                __Game_Objects_OwnedVehicle_RO_BufferTypeHandle          = state.GetBufferTypeHandle<OwnedVehicle>(isReadOnly: true);
                __Game_Buildings_ServiceDispatch_RW_BufferTypeHandle     = state.GetBufferTypeHandle<ServiceDispatch>(isReadOnly: false);
                __Game_Vehicles_Ambulance_RO_ComponentLookup             = state.GetComponentLookup<Ambulance>(isReadOnly: true);
                __Game_Vehicles_ParkedCar_RO_ComponentLookup             = state.GetComponentLookup<ParkedCar>(isReadOnly: true);
                __Game_Common_Target_RO_ComponentLookup                  = state.GetComponentLookup<Target>(isReadOnly: true);
                __Game_Healthcare_HealthcareRequest_RO_ComponentLookup   = state.GetComponentLookup<HealthcareRequest>(isReadOnly: true);
                __Game_Objects_Owner_RO_ComponentLookup                  = state.GetComponentLookup<Owner>(isReadOnly: true);
            }
        }
    }
}
