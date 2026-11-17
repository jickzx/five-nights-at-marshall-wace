import { configDefaults, defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    // backend/ (sponsor voice server) has its own runner (node:test)
    exclude: [...configDefaults.exclude, "backend/**"],
  },
});
