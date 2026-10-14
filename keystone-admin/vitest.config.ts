import { defineConfig, mergeConfig } from "vitest/config";
import viteConfig from "./vite.config";

export default mergeConfig(
  viteConfig,
  defineConfig({
    test: {
      environment: "jsdom",
      setupFiles: ["./tests/setup.ts"],
      include: ["tests/**/*.test.{ts,tsx}"],
      // jsdom start-up and the first render of a page are slow when many test files run in parallel.
      testTimeout: 15000,
      restoreMocks: true,
      unstubGlobals: true,
    },
  })
);