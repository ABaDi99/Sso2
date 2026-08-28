import { defineConfig, type ProxyOptions } from "vite";
import react from "@vitejs/plugin-react";

// Le proxy Vite réécrit le header Host vers sa cible (localhost:5171).
// Sans ça, SsoServer construit ses redirections (login, etc.) à partir de
// "localhost" au lieu de l'hôte réellement utilisé par le navigateur —
// cassé pour quiconque accède depuis une autre machine du réseau. On
// transmet donc l'hôte d'origine via X-Forwarded-Host ; SsoServer le lit
// via UseForwardedHeaders (Program.cs) pour construire ses URLs.
const target = "http://localhost:5171";
const proxyOptions: ProxyOptions = {
  target,
  configure: (proxy) => {
    proxy.on("proxyReq", (proxyReq, req) => {
      if (req.headers.host) proxyReq.setHeader("x-forwarded-host", req.headers.host);
      proxyReq.setHeader("x-forwarded-proto", "http");
    });
  },
};

export default defineConfig({
  plugins: [react()],

  // Indispensable : l'application sera servie sous /admin en production.
  // Sans cette ligne, les scripts seraient cherchés à la racine et l'écran
  // resterait blanc.
  base: "/admin/",

  build: {
    outDir: "../SsoServer/wwwroot/admin",
    emptyOutDir: true,
  },

  server: {
    port: 5174,
    proxy: {
      "/admin/api": proxyOptions,
      "/api": proxyOptions,
      "/Account": proxyOptions,
      "/connect": proxyOptions,
    },
  },
});
