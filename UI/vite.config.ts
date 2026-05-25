import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { resolve } from "path";

// Output a single IIFE bundle consumed by the PDX mod loader.
// cs2/* and react are provided by the game host; mark them external so they
// are not bundled and reference the globals the game exposes.
export default defineConfig({
  plugins: [react()],
  build: {
    target: "es2017",
    lib: {
      entry: resolve(__dirname, "src/index.ts"),
      formats: ["iife"],
      name: "FireEMS",
      fileName: () => "index.js",
    },
    outDir: resolve(__dirname, "../dist/UI"),
    emptyOutDir: true,
    rollupOptions: {
      external: [
        "react",
        "react-dom",
        "cs2/api",
        "cs2/l10n",
        "cs2/bindings",
        "cs2/ui",
      ],
      output: {
        globals: {
          react:          "React",
          "react-dom":    "ReactDOM",
          "cs2/api":      "cs2_api",
          "cs2/l10n":     "cs2_l10n",
          "cs2/bindings": "cs2_bindings",
          "cs2/ui":       "cs2_ui",
        },
        // Ensures React hooks work when the bundle shares the host's React instance.
        interop: "auto",
      },
    },
    // Source maps help in-game debugging without impacting runtime perf.
    sourcemap: true,
    minify: false,
  },
});
