import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { VitePWA } from "vite-plugin-pwa";

export default defineConfig({
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
