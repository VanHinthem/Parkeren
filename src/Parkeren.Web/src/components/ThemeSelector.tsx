import { useEffect, useState } from "react";
import { getThemePreference, saveThemePreference, type ThemePreference } from "../design/theme/theme";
import "./ThemeSelector.css";

export function ThemeSelector() {
  const [value, setValue] = useState<ThemePreference>(() => getThemePreference());
  useEffect(() => saveThemePreference(value), [value]);
  return (
    <label className="theme-selector">
      <span>Weergave</span>
      <select value={value} onChange={e => setValue(e.target.value as ThemePreference)}>
        <option value="system">Systeem</option><option value="light">Licht</option><option value="dark">Donker</option>
      </select>
    </label>
  );
}
