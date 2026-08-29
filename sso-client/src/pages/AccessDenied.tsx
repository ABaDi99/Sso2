import { useSearchParams } from "react-router-dom";
import { ShieldAlert } from "lucide-react";
import { login } from "../api";
import { Button } from "@/components/ui/button";
import ThemeToggle from "../components/ThemeToggle";

// Un seul message technique (error_description de SsoServer) est affiché
// tel quel : celui d'access_denied, rédigé pour être lu par un utilisateur
// ("Aucun rôle n'est assigné..."). Les autres raisons restent volontairement
// génériques — elles viennent d'un échange de jetons qui a mal tourné, pas
// d'un message pensé pour être montré à quelqu'un.
const MESSAGES: Record<string, { title: string; body: string }> = {
  access_denied: {
    title: "Accès refusé",
    body: "Votre compte est valide, mais aucun rôle ne vous a été attribué pour cette application. Contactez un administrateur pour qu'il vous en attribue un.",
  },
  missing_code: {
    title: "Connexion interrompue",
    body: "La réponse du serveur d'identité est incomplète. Réessayez de vous connecter.",
  },
  invalid_state: {
    title: "Connexion interrompue",
    body: "Cette tentative de connexion ne correspond pas à celle qui a été initiée — par sécurité, elle a été refusée. Réessayez de vous connecter.",
  },
  missing_verifier: {
    title: "Connexion interrompue",
    body: "Les informations nécessaires pour finaliser la connexion ont expiré ou sont absentes. Réessayez de vous connecter.",
  },
  exchange_failed: {
    title: "Connexion impossible",
    body: "Le serveur d'identité n'a pas pu délivrer d'accès. Réessayez dans un instant, ou contactez un administrateur si le problème persiste.",
  },
  invalid_response: {
    title: "Connexion impossible",
    body: "La réponse du serveur d'identité est invalide. Réessayez dans un instant.",
  },
  invalid_token: {
    title: "Connexion impossible",
    body: "Le jeton reçu n'a pas pu être vérifié. Réessayez de vous connecter ; si le problème persiste, contactez un administrateur.",
  },
};

const DEFAULT_MESSAGE = {
  title: "Connexion impossible",
  body: "Une erreur inattendue est survenue pendant la connexion. Réessayez, ou contactez un administrateur si le problème persiste.",
};

export default function AccessDenied() {
  const [params] = useSearchParams();
  const reason = params.get("reason") ?? "";
  const detail = params.get("detail");
  const message = MESSAGES[reason] ?? DEFAULT_MESSAGE;

  return (
    <div className="flex min-h-screen flex-col">
      <header className="flex items-center justify-between border-b px-6 py-5 sm:px-10">
        <span className="text-lg font-semibold tracking-tight">Atrium</span>
        <ThemeToggle />
      </header>

      <main className="flex flex-1 flex-col items-center justify-center gap-6 px-6 text-center">
        <div className="max-w-md space-y-4">
          <div className="flex justify-center">
            <div className="rounded-full bg-destructive/10 p-3 text-destructive">
              <ShieldAlert className="h-8 w-8" />
            </div>
          </div>

          <h1 className="text-2xl font-semibold tracking-tight">
            {message.title}
          </h1>
          <p className="text-sm text-muted-foreground">{message.body}</p>

          {reason === "access_denied" && detail && (
            <p className="rounded-md border bg-muted/50 px-4 py-3 font-mono text-xs text-muted-foreground">
              {detail}
            </p>
          )}
        </div>

        <Button onClick={login}>Réessayer de me connecter</Button>
      </main>
    </div>
  );
}
