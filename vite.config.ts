import { resolve } from "node:path";
import { configDefaults, defineConfig } from "vitest/config";

// Two pages: the game (index.html) and the QS Data Pipeline employer page (qs/index.html, served at /qs/).
export default defineConfig({
  build: {
    rolldownOptions: {
      input: {
        main: resolve(import.meta.dirname, "index.html"),
        qs: resolve(import.meta.dirname, "qs/index.html"),
      },
    },
  },
  test: {
    // backend/ (sponsor voice server) and qs-backend/ have their own runners (node:test, pytest)
    exclude: [...configDefaults.exclude, "backend/**", "qs-backend/**"],
  },
});
