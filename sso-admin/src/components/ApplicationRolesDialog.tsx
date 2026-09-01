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
import { SearchSelect } from "./SearchSelect";

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
  const rolesForClient = roles.filter((r) => r.clientId === clientId);
  const [roleName, setRoleName] = useState(rolesForClient[0]?.name ?? "");
  const [problem, setProblem] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // Un rôle appartient à une seule application : changer d'application dans
  // le sélecteur doit repartir sur un rôle qui existe réellement pour elle,
  // pas garder la sélection précédente qui n'a plus de sens.
  useEffect(() => {
    setRoleName(rolesForClient[0]?.name ?? "");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [clientId]);

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

          {clients.length === 0 ? (
            <p className="hint" style={{ marginTop: 14 }}>
              Aucune application déclarée.
            </p>
          ) : (
            <div className="field" style={{ marginTop: 14 }}>
              <label>Ajouter un rôle pour une application</label>
              <div style={{ display: "flex", gap: 8 }}>
                <SearchSelect
                  style={{ flex: 1, minWidth: 0 }}
                  value={clientId}
                  onChange={setClientId}
                  placeholder="Application…"
                  options={clients.map((c) => ({
                    value: c.clientId,
                    label: c.displayName ?? c.clientId,
                  }))}
                />
                {rolesForClient.length === 0 ? (
                  <p className="hint" style={{ flex: 1, margin: 0, alignSelf: "center" }}>
                    Aucun rôle pour cette application — créez-en un dans
                    l'onglet Rôles.
                  </p>
                ) : (
                  <SearchSelect
                    style={{ flex: 1, minWidth: 0 }}
                    value={roleName}
                    onChange={setRoleName}
                    placeholder="Rôle…"
                    options={rolesForClient.map((r) => ({
                      value: r.name,
                      label: r.name,
                    }))}
                  />
                )}
                <button
                  className="btn small primary"
                  onClick={assign}
                  disabled={busy || rolesForClient.length === 0}
                  style={{ flexShrink: 0 }}
                >
                  Ajouter
                </button>
              </div>
              <p className="hint">
                Seuls les rôles créés pour l'application choisie sont
                proposés — un rôle appartient à une seule application.
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
