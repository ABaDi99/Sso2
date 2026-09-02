import { useEffect, useState } from "react";
import Layout from "../components/Layout";
import { useCurrentUser } from "../hooks/useCurrentUser";
import { getHealth, getSecret, type SecretData } from "../api";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

export default function Dashboard() {
  const { user, loading } = useCurrentUser();
  const [health, setHealth] = useState<string | null>(null);
  const [secret, setSecret] = useState<SecretData | null>(null);

  useEffect(() => {
    getHealth().then((r) => setHealth(r?.status ?? null));
    getSecret().then(setSecret);
  }, []);

  if (loading || !user)
    return (
      <p className="p-10 text-center font-mono text-xs text-muted-foreground">
        vérification de la session
      </p>
    );

  return (
    <Layout user={user}>
      <h1 className="mb-2 text-2xl font-semibold tracking-tight">
        Vous êtes connecté
      </h1>
      <p className="mb-6 text-sm text-muted-foreground">
        Identité transmise par le serveur d'identité, sans mot de passe.
      </p>

      <Card className="mb-4">
        <CardContent className="divide-y">
          <Row label="email" value={user.email} />
          <Row label="sub" value={user.id} />
          <Row label="rôles" value={user.roles.join(", ") || "—"} />
        </CardContent>
      </Card>

      {secret && (
        <Card className="mb-4">
          <CardHeader>
            <CardTitle>{secret.message}</CardTitle>
          </CardHeader>
          <CardContent className="divide-y">
            <Row label="généré" value={secret.genere} />
          </CardContent>
        </Card>
      )}

      {health && (
        <div className="fixed bottom-4 right-5 flex items-center gap-2 font-mono text-xs text-muted-foreground">
          <span className="size-1.5 rounded-full bg-emerald-500" />
          {health}
        </div>
      )}
    </Layout>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-baseline justify-between gap-4 py-3 first:pt-0 last:pb-0">
      <span className="shrink-0 text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {label}
      </span>
      <span className="min-w-0 break-all text-right font-mono text-sm">{value}</span>
    </div>
  );
}
