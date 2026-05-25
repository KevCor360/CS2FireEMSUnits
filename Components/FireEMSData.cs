using System;
using Unity.Entities;

namespace FireEMS.Components
{
    public struct FireEMSData : IComponentData
    {
        public int m_AmbulanceCapacity;
        public int m_AvailableAmbulances;
        public Entity m_TargetRequest;
        public FireEMSFlags m_Flags;
    }

    [Flags]
    public enum FireEMSFlags : byte
    {
        None                  = 0,
        HasAvailableAmbulances = 1,
    }
}
