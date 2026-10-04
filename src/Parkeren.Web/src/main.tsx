import React from "react";
import ReactDOM from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import App from "./App";
import { registerSW } from "virtual:pwa-register";
import { applyTheme, getThemePreference } from "./design/theme/theme";
import { NetworkStatusBanner } from "./components/NetworkStatusBanner";
import "./design/styles/global.css";

applyTheme(getThemePreference());

const serviceWorkerUpdateIntervalMs = 60 * 60 * 1000;
const foregroundUpdateThrottleMs = 30 * 1000;
let refreshingForUpdate = false;
let serviceWorkerUpdateInFlight = false;
let lastServiceWorkerUpdateCheck = 0;

async function checkForServiceWorkerUpdate(
  swUrl: string,
  registration: ServiceWorkerRegistration,
  force = false
) {
  if (serviceWorkerUpdateInFlight || registration.installing || !navigator.onLine) return;

  const now = Date.now();
  if (!force && now - lastServiceWorkerUpdateCheck < foregroundUpdateThrottleMs) return;

  lastServiceWorkerUpdateCheck = now;
  serviceWorkerUpdateInFlight = true;

  try {
    const response = await fetch(swUrl, {
      cache: "no-store",
      headers: {
        cache: "no-store",
        "cache-control": "no-cache"
      }
    });

    if (response.ok) {
      await registration.update();
    }
  } catch {
    // An update check is best effort. The current app keeps working offline
    // and another check runs when the app returns to the foreground.
  } finally {
    serviceWorkerUpdateInFlight = false;
  }
}

if ("serviceWorker" in navigator) {
  navigator.serviceWorker.addEventListener("controllerchange", () => {
    if (refreshingForUpdate) return;
    refreshingForUpdate = true;
    window.location.reload();
  });

  registerSW({
    immediate: true,
    onRegisteredSW(swUrl, registration) {
      if (!registration) return;

      void checkForServiceWorkerUpdate(swUrl, registration, true);

      window.setInterval(
        () => void checkForServiceWorkerUpdate(swUrl, registration, true),
        serviceWorkerUpdateIntervalMs
      );

      document.addEventListener("visibilitychange", () => {
        if (document.visibilityState === "visible") {
          void checkForServiceWorkerUpdate(swUrl, registration);
        }
      });

      window.addEventListener("focus", () => {
        void checkForServiceWorkerUpdate(swUrl, registration);
      });

      window.addEventListener("pageshow", () => {
        void checkForServiceWorkerUpdate(swUrl, registration);
      });
    }
  });
}

const media = window.matchMedia("(prefers-color-scheme: dark)");
media.addEventListener("change", () => {
  if (getThemePreference() === "system") applyTheme("system");
});

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode><NetworkStatusBanner/><BrowserRouter><App /></BrowserRouter></React.StrictMode>
);
