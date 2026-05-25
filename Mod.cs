using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Colossal.Localization;
using FireEMS.Patches;
using FireEMS.Settings;
using FireEMS.Systems;
using Game;
using Game.Modding;
using Game.SceneFlow;
using HarmonyLib;
using Unity.Entities;

namespace FireEMS
{
    public class FireEMSMod : IMod
    {
        public const string HarmonyId = "KevCor360.CS2FireEMSUnits";

        public static readonly ILog Log = LogManager
            .GetLogger(typeof(FireEMSMod).Namespace)
            .SetShowsErrorsInUI(false);

        public static FireEMSSettings Settings { get; private set; }

        private Harmony m_Harmony;

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info(nameof(OnLoad));

            Settings = new FireEMSSettings(this);
            Settings.RegisterInOptionsUI();

            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Settings));

            AssetDatabase.global.LoadSettings(nameof(FireEMS), Settings, new FireEMSSettings(this));

            updateSystem.UpdateAt<FireEMSInitSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<FireEMSSystem>(SystemUpdatePhase.GameSimulation);

            // FireEMSPatchState needs a world ref; DefaultGameObjectInjectionWorld is
            // guaranteed to exist by the time OnLoad runs.
            FireEMSPatchState.Initialize(World.DefaultGameObjectInjectionWorld);

            m_Harmony = new Harmony(HarmonyId);
            m_Harmony.PatchAll(typeof(FireEMSMod).Assembly);
        }

        public void OnDispose()
        {
            Log.Info(nameof(OnDispose));

            m_Harmony?.UnpatchAll(HarmonyId);

            if (Settings != null)
            {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }
        }
    }
}
