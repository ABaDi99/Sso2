import { useState } from "react";
import {
  api,
  ApiError,
  NotAuthenticated,
  goToLogin,
  type ClientCreated,
} from "../api";

const SCOPES = ["openid", "profile", "email", "roles", "offline_access"];

export function CreateClientDialog({
  onCancel,
  onCreated,
}: {
  onCancel: () => void;
  onCreated: (result: ClientCreated) => void;
}) {
  const [clientId, setClientId] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [confidential, setConfidential] = useState(true);
  const [uris, setUris] = useState("");
  const [postLogoutUris, setPostLogoutUris] = useState("");
  const [scopes, setScopes] = useState<string[]>([
    "openid",
    "profile",
    "email",
  ]);
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

    const redirectUris = uris
      .split("\n")
      .map((u) => u.trim())
      .filter(Boolean);

    const postLogoutRedirectUris = postLogoutUris
      .split("\n")
      .map((u) => u.trim())
      .filter(Boolean);

    try {
      const result = await api.clients.create({
        clientId: clientId.trim(),
        displayName: displayName.trim() || undefined,
        clientType: confidential ? "confidential" : "public",
        redirectUris,
        postLogoutRedirectUris,
        scopes,
      });
      onCreated(result);
    } catch (e) {
      if (e instanceof NotAuthenticated) return goToLogin();
      if (e instanceof ApiError && e.problems.length > 0)
        setProblems(e.problems);
      else
        setProblems([e instanceof Error ? e.message : "Création impossible."]);
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
          <h2>Déclarer une application</h2>
          <p>
            Elle pourra ensuite envoyer ses utilisateurs se connecter ici, sans
            jamais voir leur mot de passe.
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
            <label htmlFor="cid">Identifiant de l'application</label>
            <input
              id="cid"
              type="text"
              value={clientId}
              onChange={(e) => setClientId(e.target.value)}
              placeholder="application-comptabilite"
              autoFocus
            />
            <p className="hint">
              Le client_id qu'elle utilisera. Choisissez-le lisible : il
              s'affiche sur la page de connexion.
            </p>
          </div>

          <div className="field">
            <label htmlFor="name">Nom affiché</label>
            <input
              id="name"
              type="text"
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              placeholder="Facultatif"
            />
          </div>

          <div className="field">
            <label>Type</label>
            <div className="checks">
              <label className={"check" + (confidential ? " on" : "")}>
                <input
                  type="radio"
                  checked={confidential}
                  onChange={() => setConfidential(true)}
                />
                Confidentielle
              </label>
              <label className={"check" + (!confidential ? " on" : "")}>
                <input
                  type="radio"
                  checked={!confidential}
                  onChange={() => setConfidential(false)}
                />
                Publique
              </label>
            </div>
            <p className="hint">
              {confidential
                ? "Elle possède un serveur capable de garder un secret. Recevra un client_secret."
                : "Application front seule, sans serveur. Aucun secret : la protection repose sur PKCE."}
            </p>
          </div>

          <div className="field">
            <label htmlFor="uris">Adresses de retour</label>
            <textarea
              id="uris"
              value={uris}
              onChange={(e) => setUris(e.target.value)}
              placeholder={"http://localhost:5200/auth/callback"}
            />
            <p className="hint">
              Une par ligne. Le serveur les compare au caractère près : une
              barre oblique en trop suffit à faire échouer la connexion.
            </p>
          </div>

          <div className="field">
            <label htmlFor="post-logout-uris">
              Adresses de retour après déconnexion
            </label>
            <textarea
              id="post-logout-uris"
              value={postLogoutUris}
              onChange={(e) => setPostLogoutUris(e.target.value)}
              placeholder={"http://localhost:5173"}
            />
            <p className="hint">
              Une par ligne, facultatif. Sans adresse enregistrée ici,
              l'application ne pourra pas renvoyer l'utilisateur chez elle
              après une déconnexion — il restera sur une page de SsoServer.
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
              offline_access autorise le renouvellement des jetons sans nouvelle
              connexion.
            </p>
          </div>
        </div>

        <div className="dialog-foot">
          <button className="btn" onClick={onCancel} disabled={busy}>
            Annuler
          </button>
          <button className="btn primary" onClick={submit} disabled={busy}>
            {busy ? "Création…" : "Créer l'application"}
          </button>
        </div>
      </div>
    </div>
  );
}
