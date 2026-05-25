import React from "react";
import { useLocalization } from "cs2/l10n";
import { Q } from "cs2/api";

// ── Component ──────────────────────────────────────────────────────────────

interface FireEMSPropertyFieldProps {
  value: number;
}

const FireEMSPropertyField = ({ value }: FireEMSPropertyFieldProps) => {
  const { translate } = useLocalization();

  // Hidden when value is 0 (Private dispatch mode omits the binding entirely
  // on the C# side so this guard is belt-and-suspenders only).
  if (value <= 0) return null;

  const label = translate("FireEMS.PROPERTY.EMS_UNITS") ?? "EMS Units";

  // HTML structure must exactly match vanilla stat chip markup confirmed via
  // DevTools inspection of the live fire station asset info panel.
  return (
    <div className="field_rIn">
      <div className="header_lrj">
        <div>{label}</div>
      </div>
      <div className="content_RIT">{value}</div>
    </div>
  );
};

// ── Registration ──────────────────────────────────────────────────────────

// Intercept the propertyFieldComponents setter on the property-field module
// so we can add our entry without replacing vanilla entries.  The setter may
// fire more than once during initialisation, so always spread the received
// map rather than a cached reference.
Q.add(
  "game-ui/game/components/asset-menu/asset-detail-panel/property-field.tsx",
  "propertyFieldComponents",
  (current: Record<string, React.ComponentType<any>>) => ({
    ...current,
    "FireEMS.EMS_UNITS": FireEMSPropertyField,
  })
);

export default FireEMSPropertyField;
