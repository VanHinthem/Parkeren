import { execFileSync } from "node:child_process";
import { readdir, readFile, stat } from "node:fs/promises";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const webRoot = fileURLToPath(new URL("../", import.meta.url));
const distRoot = join(webRoot, "dist");

function ensure(condition, message) {
  if (!condition) throw new Error(message);
}

function expectedBuildId() {
  const configured = process.env.PARKEREN_BUILD_ID ?? process.env.GITHUB_SHA;
  if (configured) return configured;

  try {
    return execFileSync("git", ["rev-parse", "HEAD"], { cwd: webRoot, encoding: "utf8" }).trim();
  } catch {
    return "local";
  }
}

function expectedAppVersion() {
  const configured = process.env.PARKEREN_APP_VERSION;
  if (configured) return configured;
  return ["dev", "acc"].includes(process.env.PARKEREN_APP_VARIANT ?? "") ? process.env.PARKEREN_APP_VARIANT : "local";
}

const manifest = JSON.parse(await readFile(join(distRoot, "manifest.webmanifest"), "utf8"));
const appVariant = process.env.PARKEREN_APP_VARIANT === "acc" ? "acc" : process.env.PARKEREN_APP_VARIANT === "dev" ? "dev" : "production";
const expectedAppName = appVariant === "acc" ? "Parkeren ACC" : appVariant === "dev" ? "Parkeren Dev" : "Parkeren";
const iconSuffix = appVariant === "production" ? "" : `-${appVariant}`;
ensure(manifest.name === expectedAppName, `PWA name must be '${expectedAppName}'.`);
ensure(manifest.short_name === expectedAppName, `PWA short_name must be '${expectedAppName}'.`);
ensure(manifest.start_url === "/", "PWA start_url must be '/'.");
ensure(manifest.scope === "/", "PWA scope must be explicitly set to '/'.");
ensure(manifest.display === "standalone", "PWA display must be standalone.");
ensure(/^#[\da-f]{6}$/i.test(manifest.theme_color), "PWA theme_color must be a six-digit hex color.");
ensure(/^#[\da-f]{6}$/i.test(manifest.background_color), "PWA background_color must be a six-digit hex color.");
ensure(Array.isArray(manifest.icons) && manifest.icons.length >= 2, "PWA needs at least two app icons.");

for (const expectedSize of [192, 512]) {
  const icon = manifest.icons.find(item => item.sizes === `${expectedSize}x${expectedSize}`);
  ensure(icon, `PWA ${expectedSize}px icon is missing.`);
  ensure(icon.src === `/pwa-${expectedSize}x${expectedSize}${iconSuffix}.png`, `PWA ${expectedSize}px icon does not match the ${appVariant} variant.`);
  ensure(icon.type === "image/png", `PWA ${expectedSize}px icon must be PNG.`);
  ensure(icon.purpose.split(/\s+/).includes("maskable"), `PWA ${expectedSize}px icon must support maskable rendering.`);

  const image = await readFile(join(distRoot, icon.src.replace(/^\/+/, "")));
  ensure(image.subarray(1, 4).toString("ascii") === "PNG", `PWA ${expectedSize}px icon is not a PNG image.`);
  ensure(image.readUInt32BE(16) === expectedSize && image.readUInt32BE(20) === expectedSize, `PWA ${expectedSize}px icon dimensions do not match the manifest.`);
}

const expectedAppShellIcon = await readFile(join(distRoot, `pwa-192x192${iconSuffix}.png`));
const appShellIcon = await readFile(join(distRoot, "pwa-192x192.png"));
ensure(appShellIcon.equals(expectedAppShellIcon), `App shell icon does not match the ${appVariant} variant.`);

const html = await readFile(join(distRoot, "index.html"), "utf8");
ensure(html.includes('lang="nl"'), "Built app must declare Dutch document language.");
ensure(html.includes(`<title>${expectedAppName}</title>`), `Built app title must be '${expectedAppName}'.`);
ensure(/<meta\s+name="viewport"[^>]*content="[^"]+"/.test(html), "Built app must declare a viewport for mobile devices.");
ensure(html.includes("manifest.webmanifest"), "Built app must link its web manifest.");
await stat(join(distRoot, "sw.js"));

const appEntry = await readFile(join(webRoot, "src", "main.tsx"), "utf8");
ensure(/import\s*\{\s*registerSW\s*\}\s*from\s*["']virtual:pwa-register["']/.test(appEntry), "App entry must import the PWA service-worker registrar.");
ensure(/registerSW\s*\(/.test(appEntry), "App entry must register the service worker.");

const workerSource = await readFile(join(webRoot, "src", "sw.ts"), "utf8");
ensure(/precacheAndRoute\s*\(/.test(workerSource), "Service worker must install the generated static precache.");
ensure(!/\bregisterRoute\s*\(/.test(workerSource), "Runtime service-worker routes are not allowed; API data must remain network-only.");
ensure(!/\bCacheFirst\b/.test(workerSource), "CacheFirst is not allowed in the service worker because it can serve stale API data.");

const offlineStatusSource = await readFile(join(webRoot, "src", "components", "NetworkStatusBanner.tsx"), "utf8");
ensure(/role="status"/.test(offlineStatusSource) && /aria-live="polite"/.test(offlineStatusSource), "Offline status must be announced to assistive technology.");
const globalStyles = await readFile(join(webRoot, "src", "design", "styles", "global.css"), "utf8");
ensure(globalStyles.includes(":focus-visible"), "Keyboard users must have a visible focus indicator.");

const assetNames = await readdir(join(distRoot, "assets"));
const javascript = assetNames.filter(name => name.endsWith(".js"));
const stylesheets = assetNames.filter(name => name.endsWith(".css"));
ensure(javascript.length > 0, "Production JavaScript assets are missing.");

const javascriptSizes = await Promise.all(javascript.map(async name => (await stat(join(distRoot, "assets", name))).size));
const stylesheetSizes = await Promise.all(stylesheets.map(async name => (await stat(join(distRoot, "assets", name))).size));
const bundledStylesheets = await Promise.all(stylesheets.map(name => readFile(join(distRoot, "assets", name), "utf8")));
const totalJavascript = javascriptSizes.reduce((total, size) => total + size, 0);
const largestJavascript = Math.max(...javascriptSizes);
const totalStylesheets = stylesheetSizes.reduce((total, size) => total + size, 0);
const kibibytes = value => `${(value / 1024).toFixed(1)} KiB`;
const bundledJavaScript = await Promise.all(javascript.map(name => readFile(join(distRoot, "assets", name), "utf8")));
const bundle = bundledJavaScript.join("\n");

ensure(bundle.includes("serviceWorker.register"), "Production JavaScript must register the service worker.");
ensure(bundle.includes("aria-live") && bundle.includes("aria-label"), "Production UI must retain live announcements and accessible control names.");
ensure(bundledStylesheets.some(stylesheet => stylesheet.includes(":focus-visible")), "Production CSS must retain a visible keyboard focus indicator.");

ensure(totalJavascript <= 480 * 1024, `JavaScript budget exceeded: ${kibibytes(totalJavascript)} > 480 KiB.`);
ensure(largestJavascript <= 450 * 1024, `Largest JavaScript chunk budget exceeded: ${kibibytes(largestJavascript)} > 450 KiB.`);
ensure(totalStylesheets <= 80 * 1024, `CSS budget exceeded: ${kibibytes(totalStylesheets)} > 80 KiB.`);

const buildId = expectedBuildId();
ensure(bundledJavaScript.some(asset => asset.includes(buildId)), "Configured build ID is missing from production JavaScript assets.");
const appVersion = expectedAppVersion();
ensure(bundledJavaScript.some(asset => asset.includes(appVersion)), "Configured app version is missing from production JavaScript assets.");

console.log(`PWA checks passed. JavaScript ${kibibytes(totalJavascript)} total / ${kibibytes(largestJavascript)} max chunk; CSS ${kibibytes(totalStylesheets)}.`);