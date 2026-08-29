import { useCallback, useEffect, useState } from "react";
import {
  api,
  type Client,
  type Role,
  type User,
  type UserList,
} from "../api";
import { ActionsMenu } from "../components/ActionsMenu";
import { CreateUserDialog } from "../components/CreateUserDialog";
import { ApplicationRolesDialog } from "../components/ApplicationRolesDialog";
import { SuspensionsDialog } from "../components/SuspensionsDialog";
import { PasswordDialog } from "../components/PasswordDialog";
import { formatDate } from "../lib/format";
import { useApiAction } from "../hooks/useApiAction";
import { Pager } from "../components/Pager";

// Un seul dialogue ouvert à la fois : un état structuré plutôt que 4
// useState<User | null> indépendants, qui n'excluaient pas techniquement
// (dans l'état, pas dans l'UI) que plusieurs soient "ouverts" ensemble.
type DialogState =
  | { kind: "create" }
  | { kind: "password"; user: User }
  | { kind: "appRoles"; user: User }
  | { kind: "suspensions"; user: User }
  | null;

export default function UsersPage() {
  const [data, setData] = useState<UserList | null>(null);
  const [roles, setRoles] = useState<Role[]>([]);
  const [clients, setClients] = useState<Client[]>([]);
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [error, setError] = useState<string | null>(null);
  const [dialog, setDialog] = useState<DialogState>(null);
  const { run } = useApiAction(setError);

  const load = useCallback(async () => {
    const result = await run(
      () => api.users.list(search || undefined, page),
      "Chargement impossible."
    );
    if (result.success) setData(result.value);
  }, [search, page, run]);

  useEffect(() => {
    load();
  }, [load]);

  useEffect(() => {
    run(() => api.roles.list(), "Chargement des rôles impossible.").then(
      (r) => r.success && setRoles(r.value)
    );
    run(() => api.clients.list(), "Chargement des applications impossible.").then(
      (r) => r.success && setClients(r.value)
    );
    // Volontairement hors de `run` défini ci-dessus (dépendrait de `search`/
    // `page` via sa fermeture) : ce chargement ne dépend que du montage.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function act(action: () => Promise<unknown>) {
    const result = await run(action, "Action impossible.");
    if (result.success) load();
  }

  async function remove(user: User) {
    const typed = window.prompt(
      `Saisissez « ${user.email} » pour confirmer la suppression.\n\n` +
        `Préférez la désactivation : elle bloque la connexion sans effacer l'historique.`
    );
    if (typed !== user.email) return;
    act(() => api.users.remove(user.id));
  }

  const users = data?.items ?? [];
  const activeCount = users.filter((u) => u.isActive).length;
  const adminCount = users.filter(
    (u) => u.roles.includes("Admin") && u.isActive
  ).length;
  const pageCount = data
    ? Math.max(1, Math.ceil(data.total / data.pageSize))
    : 1;

  return (
    <>
      <header className="top">
        <div>
          <h1>Comptes</h1>
          <p>
            Un compte n'a accès à une application que si un rôle lui y est
            assigné — voir "Rôles applicatifs".
          </p>
        </div>
        <button className="btn primary" onClick={() => setDialog({ kind: "create" })}>
          Créer un compte
        </button>
      </header>

      <div className="body">
        {error && <div className="notice">{error}</div>}

        {/* Combien de personnes peuvent encore entrer, et combien peuvent
            administrer. C'est ce qu'on veut savoir avant de désactiver
            quelqu'un. */}
        {data && (
          <div className="stats">
            <div className="stat">
              <span className="stat-value">{data.total}</span>
              <span className="stat-label">comptes</span>
            </div>
            <div className="stat">
              <span className="stat-value">{activeCount}</span>
              <span className="stat-label">actifs sur cette page</span>
            </div>
            <div className="stat">
              <span className="stat-value">{adminCount}</span>
              <span className="stat-label">
                administrateur{adminCount > 1 ? "s" : ""}
              </span>
            </div>
          </div>
        )}

        <div className="toolbar">
          <input
            type="text"
            placeholder="Rechercher par adresse électronique"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(1);
            }}
          />
        </div>

        {data === null ? (
          <div className="loading">Chargement…</div>
        ) : users.length === 0 ? (
          <div className="empty">
            <h2>{search ? "Aucun résultat" : "Aucun compte"}</h2>
            <p>
              {search
                ? `Rien ne correspond à « ${search} ». Essayez une autre recherche.`
                : "Créez un compte pour donner à quelqu'un l'accès aux applications."}
            </p>
            {!search && (
              <button className="btn primary" onClick={() => setDialog({ kind: "create" })}>
                Créer un compte
              </button>
            )}
          </div>
        ) : (
          <>
            <div className="rows">
              {users.map((u) => (
                <article className="row" key={u.id}>
                  <div className="row-main">
                    <div className="row-title">{u.email}</div>

                    <div className="row-sub tags">
                      <span className={"tag " + (u.isActive ? "ok" : "off")}>
                        {u.isActive ? "actif" : "désactivé"}
                      </span>
                      {u.isActive && u.isSuspended && (
                        <span className="tag off">
                          suspendu
                          {u.suspendedUntil
                            ? ` jusqu'au ${formatDate(u.suspendedUntil)}`
                            : ""}
                        </span>
                      )}
                      {u.roles.length === 0 ? (
                        <span className="tag">aucun rôle</span>
                      ) : (
                        u.roles.map((r) => (
                          <span className="tag accent" key={r}>
                            {r}
                          </span>
                        ))
                      )}
                    </div>
                  </div>

                  <div className="row-actions">
                    <ActionsMenu
                      items={[
                        u.roles.includes("Admin")
                          ? {
                              label: "Retirer le rôle Admin",
                              onClick: () =>
                                act(() =>
                                  api.users.setRoles(
                                    u.id,
                                    u.roles.filter((r) => r !== "Admin")
                                  )
                                ),
                            }
                          : {
                              label: "Rendre admin",
                              onClick: () =>
                                act(() =>
                                  api.users.setRoles(u.id, [...u.roles, "Admin"])
                                ),
                            },
                        {
                          label: "Rôles applicatifs",
                          onClick: () => setDialog({ kind: "appRoles", user: u }),
                        },
                        {
                          label: "Mot de passe",
                          onClick: () => setDialog({ kind: "password", user: u }),
                        },
                        "separator",
                        {
                          label: "Suspensions",
                          onClick: () => setDialog({ kind: "suspensions", user: u }),
                        },
                        u.isActive
                          ? {
                              label: "Désactiver",
                              onClick: () => act(() => api.users.disable(u.id)),
                            }
                          : {
                              label: "Réactiver",
                              onClick: () => act(() => api.users.enable(u.id)),
                            },
                        {
                          label: "Supprimer",
                          onClick: () => remove(u),
                          danger: true,
                        },
                      ]}
                    />
                  </div>
                </article>
              ))}
            </div>

            <Pager page={page} pageCount={pageCount} onChange={setPage} />
          </>
        )}
      </div>

      {dialog?.kind === "create" && (
        <CreateUserDialog
          onCancel={() => setDialog(null)}
          onDone={() => {
            setDialog(null);
            load();
          }}
        />
      )}

      {dialog?.kind === "password" && (
        <PasswordDialog
          user={dialog.user}
          onCancel={() => setDialog(null)}
          onDone={() => setDialog(null)}
        />
      )}

      {dialog?.kind === "appRoles" && (
        <ApplicationRolesDialog
          user={dialog.user}
          roles={roles}
          clients={clients}
          onCancel={() => setDialog(null)}
        />
      )}

      {dialog?.kind === "suspensions" && (
        <SuspensionsDialog
          user={dialog.user}
          onCancel={() => setDialog(null)}
          onChanged={load}
        />
      )}
    </>
  );
}
