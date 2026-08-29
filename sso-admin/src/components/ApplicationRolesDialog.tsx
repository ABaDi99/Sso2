import { useCallback, useEffect, useState } from "react";
import {
  api,
  NotAuthenticated,
  goToLogin,
  type Client,
  type Role,
  type User,
  type UserApplicationRole,
} from "../api";
import { Select } from "./Select";

export function ApplicationRolesDialog({
  user,
  roles,
  clients,
  onCancel,
}: {
  user: User;
  roles: Role[];
  clients: Client[];
  onCancel: () => void;
}) {
  const [assignments, setAssignments] = useState<UserApplicationRole[] | null>(
    null
  );
  const [clientId, setClientId] = useState(clients[0]?.clientId ?? "");
  const [roleName, setRoleName] = useState(roles[0]?.name ?? "");
  const [problem, setProblem] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      setAssignments(await api.users.appRoles.list(user.id));
    } catch (e) {
      if (e instanceof NotAuthenticated) return goToLogin();
      setProblem(e instanceof Error ? e.message : "Chargement impossible.");
    }
  }, [user.id]);

  useEffect(() => {
    load();
  }, [load]);

  async function assign() {
    if (!clientId || !roleName) return;
    setProblem(null);
    setBusy(true);
    try {
      await api.users.appRoles.assign(user.id, clientId, roleName);
      await load();
    } catch (e) {
      if (e instanceof NotAuthenticated) return goToLogin();
      setProblem(e instanceof Error ? e.message : "Assignation impossible.");
    } finally {
      setBusy(false);
    }
  }

  async function remove(assignment: UserApplicationRole) {
    setProblem(null);
    try {
      await api.users.appRoles.remove(user.id, assignment.id);
      await load();
    } catch (e) {
      if (e instanceof NotAuthenticated) return goToLogin();
      setProblem(e instanceof Error ? e.message : "Retrait impossible.");
    }
  }

  return (
    <div
      className="veil"
      onClick={(e) => e.target === e.currentTarget && onCancel()}
    >
      <div className="dialog" role="dialog" aria-modal="true">
        <div className="dialog-head">
          <h2>Rôles applicatifs</h2>
          <p>{user.email}</p>
        </div>

        <div className="dialog-body">
          {problem && <div className="notice">{problem}</div>}

          {assignments === null ? (
            <div className="loading">Chargement…</div>
          ) : assignments.length === 0 ? (
            <p className="hint">
              Aucun rôle applicatif. Cette personne n'a que ses rôles globaux
              (ci-dessus), identiques quelle que soit l'application.
            </p>
          ) : (
            <div className="rows">
              {assignments.map((a) => (
                <article className="row" key={a.id}>
                  <div className="row-main">
                    <div className="row-title">{a.clientDisplayName}</div>
                    <div className="row-sub tags">
                      <span className="tag accent">{a.roleName}</span>
                    </div>
                  </div>
                  <div className="row-actions">
                    <button
                      className="btn small danger"
                      onClick={() => remove(a)}
                    >
                      Retirer
                    </button>
                  </div>
                </article>
              ))}
            </div>
          )}

          {clients.length === 0 || roles.length === 0 ? (
            <p className="hint" style={{ marginTop: 14 }}>
              {clients.length === 0
                ? "Aucune application déclarée."
                : "Aucun rôle n'existe encore — créez-en un dans l'onglet Rôles."}
            </p>
          ) : (
            <div className="field" style={{ marginTop: 14 }}>
              <label>Ajouter un rôle pour une application</label>
              <div style={{ display: "flex", gap: 8 }}>
                <Select
                  style={{ flex: 1, minWidth: 0 }}
                  value={clientId}
                  onChange={(e) => setClientId(e.target.value)}
                >
                  {clients.map((c) => (
                    <option key={c.clientId} value={c.clientId}>
                      {c.displayName ?? c.clientId}
                    </option>
                  ))}
                </Select>
                <Select
                  style={{ flex: 1, minWidth: 0 }}
                  value={roleName}
                  onChange={(e) => setRoleName(e.target.value)}
                >
                  {roles.map((r) => (
                    <option key={r.name} value={r.name}>
                      {r.name}
                    </option>
                  ))}
                </Select>
                <button
                  className="btn small primary"
                  onClick={assign}
                  disabled={busy}
                  style={{ flexShrink: 0 }}
                >
                  Ajouter
                </button>
              </div>
              <p className="hint">
                Ce rôle ne s'appliquera que dans les jetons émis pour cette
                application précise — il s'ajoute aux rôles globaux, sans les
                remplacer.
              </p>
            </div>
          )}
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
