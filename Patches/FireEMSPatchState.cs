using FireEMS.Components;
using Game.Areas;
using Game.Pathfind;
using System.Runtime.CompilerServices;
using Unity.Entities;

namespace FireEMS.Patches
{
    // Static state shared by Harmony patches that need ECS access inside postfix
    // methods.  Initialize() is called once from Mod.OnLoad; Update() is called
    // at the start of every postfix that schedules jobs so type handles are always
    // current for the frame.
    //
    // Type handles must be retrieved through a SystemBase — we re-fetch them via
    // the Get...() helpers rather than .Update() so we never operate on a stale
    // or uninitialized handle regardless of which system calls into this class.
    public static class FireEMSPatchState
    {
        public static EntityQuery                    FireStationQuery;
        public static EntityTypeHandle               EntityType;
        public static ComponentTypeHandle<FireEMSData> FireEMSDataType;
        public static BufferLookup<ServiceDistrict>  ServiceDistricts;

        private static bool                s_Initialized;
        private static PathfindSetupSystem s_PathfindSetupSystem;

        public static void Initialize(World world)
        {
            if (s_Initialized)
                return;

            s_Initialized = true;

            s_PathfindSetupSystem = world.GetOrCreateSystemManaged<PathfindSetupSystem>();

            // Query: active fire stations that have ambulances available, excluding
            // entities mid-construction, removal, or already destroyed.
            FireStationQuery = world.EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All  = new[]
                {
                    ComponentType.ReadOnly<Game.Buildings.FireStation>(),
                    ComponentType.ReadOnly<FireEMSData>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Game.Common.Deleted>(),
                    ComponentType.ReadOnly<Game.Common.Destroyed>(),
                    ComponentType.ReadOnly<Game.Common.Temp>(),
                },
            });

            // Handles are left default here; the first Update() call initializes them
            // via SystemBase.Get...() which both creates and marks them current.
        }

        // Called at the top of every Harmony postfix that schedules jobs.
        // system is the PathfindSetupSystem injected by Harmony via parameter name match.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Update(Unity.Entities.SystemBase system)
        {
            // Re-fetch via Get...() on every call.  This is equivalent to calling
            // .Update() but also handles the initial-creation case, keeping the
            // static handles valid without a separate initialization pass.
            EntityType       = system.GetEntityTypeHandle();
            FireEMSDataType  = system.GetComponentTypeHandle<FireEMSData>(isReadOnly: true);
            ServiceDistricts = system.GetBufferLookup<ServiceDistrict>(isReadOnly: true);
        }
    }
}
