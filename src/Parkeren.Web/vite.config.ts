import { execFileSync } from "node:child_process";
import { copyFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { VitePWA } from "vite-plugin-pwa";

function resolveBuildId(): string {
  const configuredBuildId = process.env.PARKEREN_BUILD_ID ?? process.env.GITHUB_SHA;
  if (configuredBuildId) return configuredBuildId;

  try {
    return execFileSync("git", ["rev-parse", "HEAD"], { encoding: "utf8" }).trim();
  } catch {
    return "local";
  }
}

function resolveAppVersion(): string {
  const configured = process.env.PARKEREN_APP_VERSION;
  if (configured) return configured;
  return ["dev", "acc"].includes(process.env.PARKEREN_APP_VARIANT ?? "") ? process.env.PARKEREN_APP_VARIANT! : "local";
}

const appVariant = process.env.PARKEREN_APP_VARIANT === "acc" ? "acc" : process.env.PARKEREN_APP_VARIANT === "dev" ? "dev" : "production";
const apiProxyTarget = process.env.PARKEREN_API_PROXY_TARGET ?? "http://localhost:5080";
const pwaDevEnabled = process.env.PARKEREN_PWA_DEV === "true";
const appName = appVariant === "acc" ? "Parkeren ACC" : appVariant === "dev" ? "Parkeren Dev" : "Parkeren";
const appIcon192 = appVariant === "dev" ? "/pwa-192x192-dev.png" : "/pwa-192x192.png";
const appIcon512 = appVariant === "dev" ? "/pwa-512x512-dev.png" : "/pwa-512x512.png";
const notificationBadge = "/notification-badge.png";

export default defineConfig({
  define: {
    __PARKEREN_BUILD_ID__: JSON.stringify(resolveBuildId()),
    __PARKEREN_APP_VERSION__: JSON.stringify(resolveAppVersion()),
    __PARKEREN_NOTIFICATION_ICON__: JSON.stringify(appIcon192),
    __PARKEREN_NOTIFICATION_BADGE__: JSON.stringify(notificationBadge)
  },
  plugins: [
    react(),
    VitePWA({
      registerType: "autoUpdate",
      strategies: "injectManifest",
      srcDir: "src",
      filename: "sw.ts",
      injectManifest: {
        // Never precache the SPA shell. Navigations must fetch the current
        // index.html so a deployment cannot revive an older asset graph.
        globIgnores: ["**/index.html"]
      },
      devOptions: {
        enabled: pwaDevEnabled,
        type: "module"
      },
      manifest: {
        name: appName,
        short_name: appName,
        lang: "nl",
        display: "standalone",
        start_url: "/",
        scope: "/",
        theme_color: "#ffffff",
        background_color: "#ffffff",
        icons: [
          {
            src: appIcon192,
            sizes: "192x192",
            type: "image/png",
            purpose: "any maskable"
          },
          {
            src: appIcon512,
            sizes: "512x512",
            type: "image/png",
            purpose: "any maskable"
          }
        ]
      }
    }),
    {
      name: "parkeren-app-variant",
      transformIndexHtml(html) {
        return html
          .replace("<title>Parkeren</title>", `<title>${appName}</title>`)
          .replace(
            "</head>",
            `    <link rel="apple-touch-icon" sizes="192x192" href="${appIcon192}" />\n  </head>`
          );
      },
      async closeBundle() {
        if (appVariant === "production") return;

        await copyFile(
          fileURLToPath(new URL("./public/pwa-192x192-dev.png", import.meta.url)),
          fileURLToPath(new URL("./dist/pwa-192x192.png", import.meta.url))
        );
      }
    }
  ],
  server: {
    proxy: {
      "/api": apiProxyTarget
    }
  },
  build: {
    outDir: "dist"
  }
});
