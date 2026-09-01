// OpenIddict encode ses permissions comme des codes préfixés
// (ept:, gt:, rst:, scp:) plutôt lisibles pour qui connaît le protocole,
// mais pas pour un administrateur non technique. On les traduit ici,
// avec un repli sur le code brut pour tout ce qu'on ne reconnaît pas.
const LABELS: Record<string, string> = {
  "ept:authorization": "Point d'autorisation",
  "ept:token": "Point d'échange de jeton",
  "ept:logout": "Point de déconnexion",
  "ept:introspection": "Introspection de jeton",
  "ept:revocation": "Révocation de jeton",
  "ept:userinfo": "Informations utilisateur",
  "ept:device": "Connexion sur appareil restreint",
  "gt:authorization_code": "Connexion via code d'autorisation",
  "gt:client_credentials": "Connexion machine-à-machine",
  "gt:refresh_token": "Renouvellement automatique de session",
  "gt:implicit": "Connexion implicite (déconseillée)",
  "gt:password": "Connexion par mot de passe direct (déconseillée)",
  "gt:device_code": "Connexion sur appareil restreint",
  "rst:code": "Réponse : code d'autorisation",
  "rst:code id_token": "Réponse : code + jeton d'identité",
  "rst:code id_token token": "Réponse : code + jeton d'identité + jeton d'accès",
  "rst:code token": "Réponse : code + jeton d'accès",
  "rst:id_token": "Réponse : jeton d'identité",
  "rst:id_token token": "Réponse : jeton d'identité + jeton d'accès",
  "rst:token": "Réponse : jeton d'accès",
  "scp:profile": "Accès au profil (nom, etc.)",
  "scp:email": "Accès à l'adresse électronique",
  "scp:roles": "Accès aux rôles",
  "scp:offline_access": "Renouvellement en arrière-plan (offline)",
  "scp:address": "Accès à l'adresse postale",
  "scp:phone": "Accès au numéro de téléphone",
};

const GROUP_LABELS: Record<string, string> = {
  ept: "Points d'accès",
  gt: "Méthodes de connexion",
  rst: "Types de réponse",
  scp: "Données demandées (scopes)",
};

export function permissionLabel(code: string): string {
  return LABELS[code] ?? code;
}

export interface PermissionGroup {
  key: string;
  label: string;
  items: { code: string; label: string }[];
}

/** Regroupe les codes de permission par préfixe (ept/gt/rst/scp) pour un affichage en catégories plutôt qu'en liste brute. */
export function groupPermissions(codes: string[]): PermissionGroup[] {
  const byPrefix = new Map<string, string[]>();

  for (const code of codes) {
    const prefix = code.split(":")[0];
    const list = byPrefix.get(prefix) ?? [];
    list.push(code);
    byPrefix.set(prefix, list);
  }

  return [...byPrefix.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([key, items]) => ({
      key,
      label: GROUP_LABELS[key] ?? key,
      items: items.map((code) => ({ code, label: permissionLabel(code) })),
    }));
}
