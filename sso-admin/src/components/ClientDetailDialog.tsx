import { useCallback, useEffect, useState } from "react";
import {
  api,
  NotAuthenticated,
  goToLogin,
  type Client,
  type ClientRoleAssignment,
} from "../api";

/* Détail d'une application — infos + rôles qui lui sont assignés */
export function ClientDetailDialog({
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
