import React from "react";
import ReactDOM from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import App from "./App";
import { applyTheme, getThemePreference } from "./design/theme/theme";
import "./design/styles/global.css";

applyTheme(getThemePreference());

const media = window.matchMedia("(prefers-color-scheme: dark)");
media.addEventListener("change", () => {
  if (getThemePreference() === "system") applyTheme("system");
});

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode><BrowserRouter><App /></BrowserRouter></React.StrictMode>
);
