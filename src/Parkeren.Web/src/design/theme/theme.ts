export type ThemePreference = "system" | "light" | "dark";

const storageKey = "parkeren.theme";

export function getThemePreference(): ThemePreference {
  const value = localStorage.getItem(storageKey);
  return value === "light" || value === "dark" || value === "system" ? value : "system";
}

export function resolveTheme(preference: ThemePreference): "light" | "dark" {
  if (preference !== "system") return preference;
  return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

export function applyTheme(preference: ThemePreference): void {
  document.documentElement.dataset.theme = resolveTheme(preference);
}

export function saveThemePreference(preference: ThemePreference): void {
  localStorage.setItem(storageKey, preference);
  applyTheme(preference);
}
