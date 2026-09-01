import { useCallback, useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import {
  api,
  NotAuthenticated,
  goToLogin,
  type Client,
  type ClientRoleAssignment,
} from "../api";
import { EditClientDialog } from "../components/EditClientDialog";
import { groupPermissions } from "../lib/permissions";

export default function ClientDetailPage() {
  const { id = "" } = useParams();
  const navigate = useNavigate();

  const [client, setClient] = useState<Client | null>(null);
  const [roles, setRoles] = useState<ClientRoleAssignment[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState(false);

  const load = useCallback(async () => {
    try {
      const [c, r] = await Promise.all([api.clients.get(id), api.clients.appRoles(id)]);
      setClient(c);
      setRoles(r);
      setError(null);
    } catch (e) {
      if (e instanceof NotAuthenticated) return goToLogin();
      setError(e instanceof Error ? e.message : "Chargement impossible.");
    }
  }, [id]);

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
    <>
      <header className="top">
        <div>
          <button
            type="button"
            className="row-title-btn"
            style={{ marginBottom: 8, fontSize: 13, color: "var(--muted)" }}
            onClick={() => navigate("/clients")}
          >
            ← Applications
          </button>
          <h1>{client?.displayName || client?.clientId || "…"}</h1>
          {client && <p>{client.clientId}</p>}
        </div>
        {client && (
          <button className="btn primary" onClick={() => setEditing(true)}>
            Modifier
          </button>
        )}
      </header>

      <div className="body">
        {error && <div className="notice">{error}</div>}

        {client === null ? (
          <div className="loading">Chargement…</div>
        ) : (
          <>
            <dl className="reveal-pairs" style={{ paddingTop: 0, borderTop: "none" }}>
              <dt>Type</dt>
              <dd>
                {client.clientType === "confidential" ? "confidentiel" : "public"}
                {client.hasSecret ? " · avec secret" : " · sans secret (PKCE)"}
              </dd>
              <dt>Retour</dt>
              <dd>{client.redirectUris.join(", ") || "—"}</dd>
              <dt>Après déconnexion</dt>
              <dd>{client.postLogoutRedirectUris.join(", ") || "—"}</dd>
            </dl>

            <div style={{ marginTop: 24, paddingTop: 20, borderTop: "1px solid var(--line-soft)" }}>
              <label>Permissions techniques (OpenIddict)</label>
              {groupPermissions(client.permissions).map((group) => (
                <div key={group.key} style={{ marginTop: 10 }}>
                  <div className="row-sub" style={{ marginBottom: 6 }}>{group.label}</div>
                  <div className="tags">
                    {group.items.map((p) => (
                      <span className="tag" key={p.code} title={p.code}>
                        {p.label}
                      </span>
                    ))}
                  </div>
                </div>
              ))}
            </div>

            <div style={{ marginTop: 24, paddingTop: 20, borderTop: "1px solid var(--line-soft)" }}>
              <label>Comptes reliés à cette application, avec leur rôle</label>

              {roles === null ? (
                <div className="loading">Chargement…</div>
              ) : byUser.size === 0 ? (
                <p className="hint">
                  Personne n'a de rôle spécifique à cette application — voir
                  l'onglet Comptes, "Rôles applicatifs" pour en assigner.
                </p>
              ) : (
                <div className="rows" style={{ marginTop: 10 }}>
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
          </>
        )}
      </div>

      {editing && client && (
        <EditClientDialog
          client={client}
          onCancel={() => setEditing(false)}
          onSaved={() => {
            setEditing(false);
            load();
          }}
        />
      )}
    </>
  );
}
