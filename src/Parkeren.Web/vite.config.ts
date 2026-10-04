import { execFileSync } from "node:child_process";
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

export default defineConfig({
  define: {
    __PARKEREN_BUILD_ID__: JSON.stringify(resolveBuildId())
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
      manifest: {
        name: "Parkeren",
        short_name: "Parkeren",
        lang: "nl",
        display: "standalone",
        start_url: "/",
        scope: "/",
        theme_color: "#ffffff",
        background_color: "#ffffff",
        icons: [
          {
            src: "/pwa-192x192.png",
            sizes: "192x192",
            type: "image/png",
            purpose: "any maskable"
          },
          {
            src: "/pwa-512x512.png",
            sizes: "512x512",
            type: "image/png",
            purpose: "any maskable"
          }
        ]
      }
    })
  ],
  server: {
    proxy: {
      "/api": "http://localhost:5080"
    }
  },
  build: {
    outDir: "dist"
  }
});
