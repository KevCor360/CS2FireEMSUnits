using FireEMS.Components;
using FireEMS.Settings;
using Game.Areas;
using Game.Net;
using Game.Pathfind;
using HarmonyLib;
using System.Runtime.CompilerServices;
using Unity.Burst;
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
    // Postfix — FireBased or Combined mode: append a job that inserts fire stations
    //           with available ambulances into the same target-seeker pass.

    [HarmonyPatch(typeof(Game.Simulation.HealthcarePathfindSetup), "SetupAmbulances")]
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
            Game.Simulation.HealthcarePathfindSetup __instance,
            ref JobHandle __result)
        {
            if (FireEMSMod.Settings == null ||
                FireEMSMod.Settings.EMSDispatchMode == DispatchMode.Private)
            {
                return;
            }

            FireEMSPatchState.Update(__instance);

            if (FireEMSPatchState.FireStationQuery.IsEmptyIgnoreFilter)
                return;

            var job = new FireStationAmbulanceSetupJob
            {
                m_EntityType      = FireEMSPatchState.EntityType,
                m_FireEMSDataType = FireEMSPatchState.FireEMSDataType,
                m_ServiceDistricts = FireEMSPatchState.ServiceDistricts,
                // TargetSeeker and AmbulanceSetupData are retrieved from the system.
                // Verify exact field/property names against HealthcarePathfindSetup
                // via ILSpy if this fails to compile; the fields below match the
                // naming convention seen in other CS2 pathfind setup systems.
                m_TargetSeeker    = __instance.m_TargetSeeker,
                m_SetupItems      = __instance.m_AmbulanceSetupItems,
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

            // These fields mirror the corresponding fields used in the vanilla
            // SetupAmbulancesJob for the hospital branch.  ILSpy the
            // HealthcarePathfindSetup class to confirm member names if the build
            // fails to resolve them; they follow standard CS2 naming patterns.
            public PathTargetSeeker                             m_TargetSeeker;
            [ReadOnly] public NativeList<AmbulanceSetupItem>   m_SetupItems;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 enabledMask)
            {
                NativeArray<Entity>      entities  = chunk.GetNativeArray(m_EntityType);
                NativeArray<FireEMSData> emsData   = chunk.GetNativeArray(ref m_FireEMSDataType);

                for (int i = 0; i < chunk.Count; i++)
                {
                    if ((emsData[i].m_Flags & FireEMSFlags.HasAvailableAmbulances) == 0)
                        continue;

                    Entity stationEntity = entities[i];

                    for (int j = 0; j < m_SetupItems.Length; j++)
                    {
                        AmbulanceSetupItem item = m_SetupItems[j];

                        // If the fire station has service districts, only cover matching areas.
                        // If there are no ServiceDistrict entries the check returns true (citywide).
                        // NOTE: verify CheckServiceDistrict signature vs AreaUtils if needed.
                        if (!AreaUtils.CheckServiceDistrict(
                                m_ServiceDistricts,
                                stationEntity,
                                item.m_Area))
                        {
                            continue;
                        }

                        // Mask allowed road types against the setup item.
                        RoadTypes maskedRoadTypes = item.m_RoadTypes & m_TargetSeeker.GetAllowedRoadTypes();

                        // Register this station as a candidate source for pathfinding.
                        m_TargetSeeker.FindTargets(stationEntity, item.m_Cost, maskedRoadTypes);
                    }
                }
            }
        }
    }
}
