import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: "wwwroot",
    emptyOutDir: true
  },
  server: {
    proxy: {
      "/api": "http://127.0.0.1:5035",
      "/health": "http://127.0.0.1:5035",
      "/webhook": "http://127.0.0.1:5035"
    }
  }
});
