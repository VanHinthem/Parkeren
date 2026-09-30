import { useState } from "react";
import { getThemePreference, saveThemePreference, type ThemePreference } from "../design/theme/theme";
import "./ThemeSelector.css";

const options: Array<{value:ThemePreference;label:string}> = [
  {value:"light",label:"Licht"},
  {value:"dark",label:"Donker"},
  {value:"system",label:"Automatisch"}
];

export function ThemeSelector(){
  const[value,setValue]=useState<ThemePreference>(()=>getThemePreference());

  function selectTheme(preference:ThemePreference){
    setValue(preference);
    saveThemePreference(preference);
  }

  return <div className="theme-selector">
    <div className="theme-selector__copy">
      <strong>Thema</strong>
      <span>Automatisch volgt de instelling van je apparaat.</span>
    </div>
    <div className="theme-selector__options" role="radiogroup" aria-label="Thema">
      {options.map(option=>
        <button
          key={option.value}
          type="button"
          role="radio"
          aria-checked={value===option.value}
          className={value===option.value?"theme-selector__option theme-selector__option--active":"theme-selector__option"}
          onClick={()=>selectTheme(option.value)}
        >
          {option.label}
        </button>)}
    </div>
  </div>;
}
