using FireEMS.Components;
using FireEMS.Settings;
using static FireEMS.Settings.FireEMSSettings;
using Game.Areas;
using Game.Pathfind;
using Game.Simulation;
using HarmonyLib;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace FireEMS.Patches
{
    // One file, one target class: HealthcarePathfindSetup.SetupAmbulances.
    //
    // Prefix  — FireBased mode only: suppress the vanilla hospital sweep entirely,
    //           returning inputDeps unchanged so downstream work still has a handle.
    //
    // Postfix — FireBased or Combined mode: append a job that registers fire stations
    //           with available ambulances into the same PathfindSetupSystem pass.

    [HarmonyPatch(typeof(HealthcarePathfindSetup), "SetupAmbulances")]
    public static class HealthcarePathfindSetupPatch
    {
        // ── Prefix ───────────────────────────────────────────────────────────

        [HarmonyPrefix]
        static bool SetupAmbulances_Prefix(ref JobHandle __result, JobHandle inputDeps)
        {
            if (FireEMSMod.Settings == null ||
                FireEMSMod.Settings.EMSDispatchMode != DispatchMode.FireBased)
            {
                return true; // run original
            }

            // Fire-Based: skip hospital sweep entirely.
            __result = inputDeps;
            return false;
        }

        // ── Postfix ───────────────────────────────────────────────────────────

        [HarmonyPostfix]
        static void SetupAmbulances_Postfix(
            ref HealthcarePathfindSetup __instance,
            PathfindSetupSystem system,
            PathfindSetupSystem.SetupData setupData,
            JobHandle inputDeps,
            ref JobHandle __result)
        {
            if (FireEMSMod.Settings == null ||
                FireEMSMod.Settings.EMSDispatchMode == DispatchMode.Private)
            {
                return;
            }

            FireEMSPatchState.Update(system);

            if (FireEMSPatchState.FireStationQuery.IsEmptyIgnoreFilter)
                return;

            var job = new FireStationAmbulanceSetupJob
            {
                m_EntityType       = FireEMSPatchState.EntityType,
                m_FireEMSDataType  = FireEMSPatchState.FireEMSDataType,
                m_ServiceDistricts = FireEMSPatchState.ServiceDistricts,
                m_SetupData        = setupData,
            };

            JobHandle fireJob = job.ScheduleParallel(
                FireEMSPatchState.FireStationQuery,
                __result);

            __result = JobHandle.CombineDependencies(__result, fireJob);
        }

        // ── FireStationAmbulanceSetupJob ──────────────────────────────────────

        [BurstCompile]
        private struct FireStationAmbulanceSetupJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                  m_EntityType;
            [ReadOnly] public ComponentTypeHandle<FireEMSData>  m_FireEMSDataType;
            [ReadOnly] public BufferLookup<ServiceDistrict>     m_ServiceDistricts;
            public PathfindSetupSystem.SetupData                m_SetupData;

            void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
            }

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 enabledMask)
            {
                NativeArray<Entity>      entities = chunk.GetNativeArray(m_EntityType);
                NativeArray<FireEMSData> emsData  = chunk.GetNativeArray(ref m_FireEMSDataType);

                for (int i = 0; i < chunk.Count; i++)
                {
                    if ((emsData[i].m_Flags & FireEMSFlags.HasAvailableAmbulances) == 0)
                        continue;

                    Entity stationEntity = entities[i];

                    for (int j = 0; j < m_SetupData.Length; j++)
                    {
                        m_SetupData.GetItem(j, out Entity districtEntity, out var targetSeeker);

                        if (!AreaUtils.CheckServiceDistrict(
                                m_ServiceDistricts,
                                stationEntity,
                                districtEntity))
                        {
                            continue;
                        }

                        targetSeeker.FindTargets(stationEntity);
                    }
                }
            }
        }
    }
}
