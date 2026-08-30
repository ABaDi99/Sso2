import { useState } from "react";
import { api, ApiError, NotAuthenticated, goToLogin, type Client } from "../api";

const SCOPES = ["profile", "email", "roles", "offline_access"];

function scopesFromPermissions(permissions: string[]): string[] {
  return permissions
    .filter((p) => p.startsWith("scp:"))
    .map((p) => p.slice(4));
}

export function EditClientDialog({
  client,
  onCancel,
  onSaved,
}: {
  client: Client;
  onCancel: () => void;
  onSaved: (updated: Client) => void;
}) {
  const [displayName, setDisplayName] = useState(client.displayName ?? "");
  const [uris, setUris] = useState(client.redirectUris.join("\n"));
  const [postLogoutUris, setPostLogoutUris] = useState(
    client.postLogoutRedirectUris.join("\n")
  );
  const [scopes, setScopes] = useState<string[]>(
    scopesFromPermissions(client.permissions)
  );
  const [problems, setProblems] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);

  function toggleScope(scope: string) {
    setScopes((current) =>
      current.includes(scope)
        ? current.filter((s) => s !== scope)
        : [...current, scope]
    );
  }

  async function submit() {
    setProblems([]);
    setBusy(true);

    const redirectUris = uris.split("\n").map((u) => u.trim()).filter(Boolean);
    const postLogoutRedirectUris = postLogoutUris
      .split("\n")
      .map((u) => u.trim())
      .filter(Boolean);

    try {
      const updated = await api.clients.update(client.id, {
        displayName: displayName.trim() || undefined,
        redirectUris,
        postLogoutRedirectUris,
        scopes,
      });
      onSaved(updated);
    } catch (e) {
      if (e instanceof NotAuthenticated) return goToLogin();
      if (e instanceof ApiError && e.problems.length > 0) setProblems(e.problems);
      else setProblems([e instanceof Error ? e.message : "Modification impossible."]);
      setBusy(false);
    }
  }

  return (
    <div
      className="veil"
      onClick={(e) => e.target === e.currentTarget && onCancel()}
    >
      <div className="dialog" role="dialog" aria-modal="true">
        <div className="dialog-head">
          <h2>Modifier {client.clientId}</h2>
          <p>
            Le client_id et le type (confidentiel/public) ne peuvent pas être
            changés après création.
          </p>
        </div>

        <div className="dialog-body">
          {problems.length > 0 && (
            <div className="notice">
              {problems.length === 1 ? (
                problems[0]
              ) : (
                <ul>
                  {problems.map((p) => (
                    <li key={p}>{p}</li>
                  ))}
                </ul>
              )}
            </div>
          )}

          <div className="field">
            <label htmlFor="edit-name">Nom affiché</label>
            <input
              id="edit-name"
              type="text"
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              autoFocus
            />
          </div>

          <div className="field">
            <label htmlFor="edit-uris">Adresses de retour</label>
            <textarea
              id="edit-uris"
              value={uris}
              onChange={(e) => setUris(e.target.value)}
            />
            <p className="hint">
              Une par ligne. Comparées au caractère près par le serveur.
            </p>
          </div>

          <div className="field">
            <label htmlFor="edit-post-logout-uris">
              Adresses de retour après déconnexion
            </label>
            <textarea
              id="edit-post-logout-uris"
              value={postLogoutUris}
              onChange={(e) => setPostLogoutUris(e.target.value)}
              placeholder={"http://localhost:5173"}
            />
            <p className="hint">
              Une par ligne, facultatif. Sans adresse enregistrée ici,
              l'application ne pourra pas renvoyer l'utilisateur chez elle
              après une déconnexion.
            </p>
          </div>

          <div className="field">
            <label>Portées</label>
            <div className="checks">
              {SCOPES.map((scope) => (
                <label
                  key={scope}
                  className={"check" + (scopes.includes(scope) ? " on" : "")}
                >
                  <input
                    type="checkbox"
                    checked={scopes.includes(scope)}
                    onChange={() => toggleScope(scope)}
                  />
                  {scope}
                </label>
              ))}
            </div>
            <p className="hint">
              openid est toujours implicite, pas besoin de le cocher.
              offline_access autorise le renouvellement des jetons sans
              nouvelle connexion.
            </p>
          </div>
        </div>

        <div className="dialog-foot">
          <button className="btn" onClick={onCancel} disabled={busy}>
            Annuler
          </button>
          <button className="btn primary" onClick={submit} disabled={busy}>
            {busy ? "Enregistrement…" : "Enregistrer"}
          </button>
        </div>
      </div>
    </div>
  );
}
