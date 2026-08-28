import { useCallback, useEffect, useState } from "react";
import {
  api,
  ApiError,
  NotAuthenticated,
  goToLogin,
  type Client,
  type ClientCreated,
  type ClientRoleAssignment,
} from "../api";
import { ActionsMenu } from "../components/ActionsMenu";

const SCOPES = ["openid", "profile", "email", "roles", "offline_access"];

const PAGE_SIZE = 5;

export default function ClientsPage() {
  const [clients, setClients] = useState<Client[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [revealed, setRevealed] = useState<ClientCreated | null>(null);
  const [viewing, setViewing] = useState<Client | null>(null);
  const [page, setPage] = useState(1);

  async function load() {
    try {
      setClients(await api.clients.list());
      setError(null);
    } catch (e) {
      if (e instanceof NotAuthenticated) return goToLogin();
      setError(e instanceof Error ? e.message : "Chargement impossible.");
    }
  }

  useEffect(() => {
    load();
  }, []);

  async function remove(client: Client) {
    const typed = window.prompt(
      `Saisissez « ${client.clientId} » pour confirmer la suppression.\n\n` +
        `Cette application ne pourra plus obtenir de jetons.`
    );

    if (typed !== client.clientId) return;

    try {
      await api.clients.remove(client.id);
      load();
    } catch (e) {
      if (e instanceof NotAuthenticated) return goToLogin();
      setError(e instanceof Error ? e.message : "Suppression impossible.");
    }
  }

  async function rotate(client: Client) {
    if (
      !window.confirm(
        `Régénérer le secret de ${client.clientId} ?\n\n` +
          `L'ancien cessera immédiatement de fonctionner.`
      )
    )
      return;

    try {
      const result = await api.clients.rotateSecret(client.id);
      setRevealed({
        client,
        clientSecret: result.clientSecret,
        notice: result.notice,
      });
    } catch (e) {
      if (e instanceof NotAuthenticated) return goToLogin();
      setError(e instanceof Error ? e.message : "Régénération impossible.");
    }
  }

  const pageCount = Math.max(1, Math.ceil((clients?.length ?? 0) / PAGE_SIZE));
  // Ne reste jamais sur une page vidée par une suppression.
  const currentPage = Math.min(page, pageCount);

  return (
    <>
      <header className="top">
        <div>
          <h1>Applications</h1>
          <p>
            Les applications autorisées à déléguer leur connexion à ce serveur.
          </p>
        </div>
        <button className="btn primary" onClick={() => setCreating(true)}>
          Déclarer une application
        </button>
      </header>

      <div className="body">
        {error && <div className="notice">{error}</div>}

        {revealed && (
          <div style={{ marginBottom: 22 }}>
            <SecretReveal data={revealed} onClose={() => setRevealed(null)} />
          </div>
        )}

        {clients === null ? (
          <div className="loading">Chargement…</div>
        ) : clients.length === 0 ? (
          <div className="empty">
            <h2>Aucune application déclarée</h2>
            <p>
              Déclarez une application pour lui donner un client_id et
              l'autoriser à demander des connexions à ce serveur.
            </p>
            <button className="btn primary" onClick={() => setCreating(true)}>
              Déclarer une application
            </button>
          </div>
        ) : (
          <>
          <div className="rows">
            {clients
              .slice((currentPage - 1) * PAGE_SIZE, currentPage * PAGE_SIZE)
              .map((c) => (
              <article className="row" key={c.id}>
                <div className="row-main">
                  <button
                    type="button"
                    className="row-title row-title-btn"
                    onClick={() => setViewing(c)}
                  >
                    {c.clientId}
                  </button>

                  <div className="row-sub">
                    {c.displayName && c.displayName !== c.clientId && (
                      <>{c.displayName} · </>
                    )}
                    <span className="tag accent">
                      {c.clientType === "confidential"
                        ? "confidentiel"
                        : "public"}
                    </span>
                  </div>

                  <div className="uris">
                    {c.redirectUris.map((u) => (
                      <code key={u}>{u}</code>
                    ))}
                  </div>
                </div>

                <div className="row-actions">
                  <ActionsMenu
                    items={[
                      { label: "Voir les détails", onClick: () => setViewing(c) },
                      ...(c.hasSecret
                        ? [{ label: "Nouveau secret", onClick: () => rotate(c) }]
                        : []),
                      "separator" as const,
                      { label: "Supprimer", onClick: () => remove(c), danger: true },
                    ]}
                  />
                </div>
              </article>
            ))}
          </div>

          {clients.length > PAGE_SIZE && (
            <div className="pager">
              <button
                className="btn small"
                disabled={currentPage <= 1}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
              >
                Précédent
              </button>
              <span className="pager-label">
                page {currentPage} sur {pageCount}
              </span>
              <button
                className="btn small"
                disabled={currentPage >= pageCount}
                onClick={() => setPage((p) => Math.min(pageCount, p + 1))}
              >
                Suivant
              </button>
            </div>
          )}
          </>
        )}
      </div>

      {creating && (
        <CreateDialog
          onCancel={() => setCreating(false)}
          onCreated={(result) => {
            setCreating(false);
            setRevealed(result);
            load();
          }}
        />
      )}

      {viewing && (
        <ClientDetailDialog client={viewing} onCancel={() => setViewing(null)} />
      )}
    </>
  );
}

/* ============================================================
   SIGNATURE — la révélation du secret
   OpenIddict hache le secret avant de l'écrire : c'est la seule
   occasion de le lire. L'interface doit rendre ce moment
   impossible à manquer.
   ============================================================ */
function SecretReveal({
  data,
  onClose,
}: {
  data: ClientCreated;
  onClose: () => void;
}) {
  const [copied, setCopied] = useState(false);

  async function copy() {
    if (!data.clientSecret) return;
    try {
      await navigator.clipboard.writeText(data.clientSecret);
      setCopied(true);
      setTimeout(() => setCopied(false), 2200);
    } catch {
      /* le texte reste sélectionnable à la main */
    }
  }

  if (!data.clientSecret) {
    return (
      <div className="reveal">
        <div className="reveal-band">Application publique créée</div>
        <div className="reveal-body">
          <p className="reveal-note" style={{ margin: 0 }}>
            {data.notice}
          </p>
          <div style={{ marginTop: 16 }}>
            <button className="btn" onClick={onClose}>
              J'ai compris
            </button>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="reveal">
      <div className="reveal-band">
        Copiez ce secret maintenant — il ne sera plus jamais affiché
      </div>

      <div className="reveal-body">
        <p className="reveal-label">client_secret</p>

        <div className="secret">
          <code>{data.clientSecret}</code>
          <button className="btn small" onClick={copy}>
            {copied ? "Copié" : "Copier"}
          </button>
        </div>

        <p className="reveal-note">
          Le serveur ne conserve qu'une empreinte du secret, jamais sa valeur.
          Personne ne pourra le retrouver — ni vous, ni un administrateur. En
          cas de perte, il faudra en régénérer un et mettre à jour
          l'application.
        </p>

        <dl className="reveal-pairs">
          <dt>client_id</dt>
          <dd>{data.client.clientId}</dd>
          <dt>Retour</dt>
          <dd>{data.client.redirectUris.join(", ")}</dd>
          <dt>Autorisation</dt>
          <dd>{window.location.origin}/connect/authorize</dd>
          <dt>Jeton</dt>
          <dd>{window.location.origin}/connect/token</dd>
        </dl>

        <div style={{ marginTop: 18 }}>
          <button className="btn" onClick={onClose}>
            J'ai copié le secret
          </button>
        </div>
      </div>
    </div>
  );
}

/* ============================================================
   Formulaire de déclaration
   ============================================================ */
function CreateDialog({
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

    try {
      const result = await api.clients.create({
        clientId: clientId.trim(),
        displayName: displayName.trim() || undefined,
        clientType: confidential ? "confidential" : "public",
        redirectUris,
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

/* ============================================================
   Détail d'une application — infos + rôles qui lui sont assignés
   ============================================================ */
function ClientDetailDialog({
  client,
  onCancel,
}: {
  client: Client;
  onCancel: () => void;
}) {
  const [roles, setRoles] = useState<ClientRoleAssignment[] | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      setRoles(await api.clients.appRoles(client.id));
    } catch (e) {
      if (e instanceof NotAuthenticated) return goToLogin();
      setProblem(e instanceof Error ? e.message : "Chargement impossible.");
    }
  }, [client.id]);

  useEffect(() => {
    load();
  }, [load]);

  // Groupé par utilisateur : plus lisible qu'une liste plate quand
  // quelqu'un a plusieurs rôles pour la même application.
  const byUser = new Map<string, ClientRoleAssignment[]>();
  for (const r of roles ?? []) {
    const list = byUser.get(r.userEmail) ?? [];
    list.push(r);
    byUser.set(r.userEmail, list);
  }

  return (
    <div
      className="veil"
      onClick={(e) => e.target === e.currentTarget && onCancel()}
    >
      <div className="dialog" role="dialog" aria-modal="true">
        <div className="dialog-head">
          <h2>{client.displayName || client.clientId}</h2>
          <p>{client.clientId}</p>
        </div>

        <div className="dialog-body">
          {problem && <div className="notice">{problem}</div>}

          <dl className="reveal-pairs" style={{ paddingTop: 0, borderTop: "none" }}>
            <dt>Type</dt>
            <dd>
              {client.clientType === "confidential" ? "confidentiel" : "public"}
              {client.hasSecret ? " · avec secret" : " · sans secret (PKCE)"}
            </dd>
            <dt>Retour</dt>
            <dd>{client.redirectUris.join(", ") || "—"}</dd>
            {client.postLogoutRedirectUris.length > 0 && (
              <>
                <dt>Après déconnexion</dt>
                <dd>{client.postLogoutRedirectUris.join(", ")}</dd>
              </>
            )}
            <dt>Permissions</dt>
            <dd>{client.permissions.join(", ")}</dd>
          </dl>

          <div style={{ marginTop: 18, paddingTop: 16, borderTop: "1px solid var(--line-soft)" }}>
            <label>Rôles assignés pour cette application</label>

            {roles === null ? (
              <div className="loading">Chargement…</div>
            ) : byUser.size === 0 ? (
              <p className="hint">
                Personne n'a de rôle spécifique à cette application — voir
                l'onglet Comptes, "Rôles applicatifs" pour en assigner.
              </p>
            ) : (
              <div className="rows">
                {[...byUser.entries()].map(([email, assignments]) => (
                  <article className="row" key={email}>
                    <div className="row-main">
                      <div className="row-title">{email}</div>
                      <div className="row-sub tags">
                        {assignments.map((a) => (
                          <span className="tag accent" key={a.id}>
                            {a.roleName}
                          </span>
                        ))}
                      </div>
                    </div>
                  </article>
                ))}
              </div>
            )}
          </div>
        </div>

        <div className="dialog-foot">
          <button className="btn primary" onClick={onCancel}>
            Fermer
          </button>
        </div>
      </div>
    </div>
  );
}
