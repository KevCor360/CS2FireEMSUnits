using System.Collections.Generic;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using Game.UI.Localization;
using Game.UI.Widgets;
using Unity.Mathematics;

namespace FireEMS.Settings
{
    [FileLocation("ModsSettings/FireEMS/FireEMS")]
    [SettingsUIGroupOrder("Dispatch", "Ratio")]
    [SettingsUIShowGroupName("Dispatch", "Ratio")]
    public class FireEMSSettings : ModSetting
    {
        public enum DispatchMode
        {
            Combined,
            FireBased,
            Private,
        }

        // ── Dispatch ─────────────────────────────────────────────────────────

        [SettingsUISection("General", "Dispatch")]
        [SettingsUIDropdown(typeof(FireEMSSettings), nameof(GetDispatchModeItems))]
        public DispatchMode EMSDispatchMode { get; set; } = DispatchMode.Combined;

        // ── Custom ratio ──────────────────────────────────────────────────────

        [SettingsUISection("General", "Ratio")]
        public bool UseCustomRatio { get; set; } = false;

        [SettingsUISection("General", "Ratio")]
        [SettingsUISlider(min = 0, max = 2, step = 1, scalarMultiplier = 1, unit = "integer")]
        [SettingsUIDisableByCondition(typeof(FireEMSSettings), nameof(IsDefaultRatio))]
        public int MinimumUnitsPerStation { get; set; } = 1;

        [SettingsUISection("General", "Ratio")]
        [SettingsUISlider(min = 2, max = 8, step = 1, scalarMultiplier = 1, unit = "integer")]
        [SettingsUIDisableByCondition(typeof(FireEMSSettings), nameof(IsDefaultRatio))]
        public int EnginesPerAdditionalUnit { get; set; } = 4;

        [SettingsUISection("General", "Ratio")]
        [SettingsUISlider(min = 1, max = 6, step = 1, scalarMultiplier = 1, unit = "integer")]
        [SettingsUIDisableByCondition(typeof(FireEMSSettings), nameof(IsDefaultRatio))]
        public int MaximumUnitsPerStation { get; set; } = 4;

        // ── Condition helpers ─────────────────────────────────────────────────

        public bool IsDefaultRatio() => !UseCustomRatio;

        // ── Capacity formula ──────────────────────────────────────────────────

        public int CalculateCapacity(int fireEngineCount)
        {
            if (!UseCustomRatio)
                return 1 + (fireEngineCount / 4);

            return math.clamp(
                MinimumUnitsPerStation + (fireEngineCount / math.max(1, EnginesPerAdditionalUnit)),
                MinimumUnitsPerStation,
                MaximumUnitsPerStation);
        }

        // ── Dropdown data ─────────────────────────────────────────────────────

        public DropdownItem<DispatchMode>[] GetDispatchModeItems()
        {
            return new[]
            {
                new DropdownItem<DispatchMode> { value = DispatchMode.Combined,  displayName = LocalizedString.IdWithFallback("FireEMS.DISPATCH.Combined",  "Combined")   },
                new DropdownItem<DispatchMode> { value = DispatchMode.FireBased, displayName = LocalizedString.IdWithFallback("FireEMS.DISPATCH.FireBased", "Fire-Based")  },
                new DropdownItem<DispatchMode> { value = DispatchMode.Private,   displayName = LocalizedString.IdWithFallback("FireEMS.DISPATCH.Private",   "Private")     },
            };
        }

        // ── Ctor ──────────────────────────────────────────────────────────────

        public FireEMSSettings(IMod mod) : base(mod)
        {
            SetDefaults();
        }

        public override void SetDefaults()
        {
            EMSDispatchMode          = DispatchMode.Combined;
            UseCustomRatio           = false;
            MinimumUnitsPerStation   = 1;
            EnginesPerAdditionalUnit = 4;
            MaximumUnitsPerStation   = 4;
        }
    }

    // ── Locale source (en-US) ────────────────────────────────────────────────

    public class LocaleEN : IDictionarySource
    {
        private readonly FireEMSSettings m_Settings;

        public LocaleEN(FireEMSSettings settings) => m_Settings = settings;

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(
            IList<IDictionaryEntryError> errors,
            Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Settings.GetSettingsLocaleID(),                                               "Fire EMS"                               },
                { m_Settings.GetOptionTabLocaleID("General"),                                     "General"                                },
                { m_Settings.GetOptionGroupLocaleID("Dispatch"),                                     "Dispatch"                               },
                { m_Settings.GetOptionGroupLocaleID("Ratio"),                                      "Ambulance Ratio"                        },
                { m_Settings.GetOptionLabelLocaleID(nameof(FireEMSSettings.EMSDispatchMode)),      "Dispatch Mode"                          },
                { m_Settings.GetOptionDescLocaleID(nameof(FireEMSSettings.EMSDispatchMode)),       "How ambulance dispatch is routed"       },
                { m_Settings.GetOptionLabelLocaleID(nameof(FireEMSSettings.UseCustomRatio)),       "Use Custom Ratio"                       },
                { m_Settings.GetOptionDescLocaleID(nameof(FireEMSSettings.UseCustomRatio)),        "Override the default 1-per-4-engines formula" },
                { m_Settings.GetOptionLabelLocaleID(nameof(FireEMSSettings.MinimumUnitsPerStation)),   "Minimum Units Per Station"          },
                { m_Settings.GetOptionDescLocaleID(nameof(FireEMSSettings.MinimumUnitsPerStation)),    "Fewest ambulances assigned regardless of engine count" },
                { m_Settings.GetOptionLabelLocaleID(nameof(FireEMSSettings.EnginesPerAdditionalUnit)), "Engines Per Additional Unit"        },
                { m_Settings.GetOptionDescLocaleID(nameof(FireEMSSettings.EnginesPerAdditionalUnit)),  "Fire engines required to unlock each extra ambulance slot" },
                { m_Settings.GetOptionLabelLocaleID(nameof(FireEMSSettings.MaximumUnitsPerStation)),   "Maximum Units Per Station"          },
                { m_Settings.GetOptionDescLocaleID(nameof(FireEMSSettings.MaximumUnitsPerStation)),    "Hard cap on ambulances per station" },
                { "FireEMS.DISPATCH.Combined",   "Combined"    },
                { "FireEMS.DISPATCH.FireBased",  "Fire-Based"  },
                { "FireEMS.DISPATCH.Private",    "Private"     },
                { "FireEMS.PROPERTY.EMS_UNITS",  "EMS Units"   },
            };
        }

        public void Unload() { }
    }
}
