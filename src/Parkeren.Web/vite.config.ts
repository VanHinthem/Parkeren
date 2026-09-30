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
      manifest: {
        name: "Parkeren",
        short_name: "Parkeren",
        lang: "nl",
        display: "standalone",
        start_url: "/",
        theme_color: "#ffffff",
        background_color: "#ffffff"
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
