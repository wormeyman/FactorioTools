import { defineConfig } from "vitest/config"

// Intentionally standalone (not extending vite.config.ts): the Vite config runs
// git/execSync and reads sample-blueprint files at load time, none of which the
// unit tests need. These tests are pure logic and run in the node environment.
export default defineConfig({
  test: {
    environment: "node",
    include: ["src/**/*.test.ts"],
  },
})
