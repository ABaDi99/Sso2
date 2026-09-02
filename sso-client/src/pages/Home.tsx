import { useEffect, useState } from "react";
import { login, getHealth } from "../api";
import { Button } from "@/components/ui/button";
import ThemeToggle from "../components/ThemeToggle";

export default function Home() {
  const [health, setHealth] = useState<string | null>(null);

  useEffect(() => {
    getHealth().then((r) => setHealth(r?.status ?? null));
  }, []);

  return (
    <div className="flex min-h-screen flex-col">
      <header className="flex items-center justify-between border-b px-6 py-5 sm:px-10">
        <span className="text-lg font-semibold tracking-tight">Atrium</span>
        <ThemeToggle />
      </header>

      <main className="flex flex-1 flex-col items-center justify-center gap-6 px-6 text-center">
        <div className="max-w-sm space-y-3">
          <h1 className="text-3xl font-semibold tracking-tight">
            Connexion requise
          </h1>
          <p className="text-sm text-muted-foreground">
            Vous serez redirigé vers le serveur d'identité pour vous authentifier.
          </p>
        </div>
        <Button onClick={login}>Se connecter</Button>
      </main>

      {health && (
        <div className="fixed bottom-4 right-5 flex items-center gap-2 font-mono text-xs text-muted-foreground">
          <span className="size-1.5 rounded-full bg-emerald-500" />
          {health}
        </div>
      )}
    </div>
  );
}
