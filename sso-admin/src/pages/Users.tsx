import { useCallback, useEffect, useState } from "react";
import {
  api,
  NotAuthenticated,
  goToLogin,
  type Client,
  type Role,
  type SuspensionType,
  type User,
  type UserApplicationRole,
  type UserList,
  type UserSuspension,
} from "../api";
import { ActionsMenu } from "../components/ActionsMenu";

const SUSPENSION_LABELS: Record<SuspensionType, string> = {
  Conge: "Congé",
  Disciplinaire: "Disciplinaire",
  Autre: "Autre",
};

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("fr-FR");
}

/* Enveloppe un <select> natif avec une flèche superposée en vrai SVG,
   plutôt qu'une image de fond CSS — voir styles.css pour pourquoi. */
function Select({
  value,
  onChange,
  children,
  style,
}: {
  value: string;
  onChange: (e: React.ChangeEvent<HTMLSelectElement>) => void;
  children: React.ReactNode;
  style?: React.CSSProperties;
}) {
  return (
    <div className="select-wrap" style={style}>
      <select value={value} onChange={onChange}>
        {children}
      </select>
      <svg
        className="select-chevron"
        width="13"
        height="13"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2.4"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M6 9l6 6 6-6" />
      </svg>
    </div>
  );
}

export default function UsersPage() {
  const [data, setData] = useState<UserList | null>(null);
  const [roles, setRoles] = useState<Role[]>([]);
  const [clients, setClients] = useState<Client[]>([]);
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [error, setError] = useState<string | null>(null);

  const [creating, setCreating] = useState(false);
  const [editingPassword, setEditingPassword] = useState<User | null>(null);
  const [editingAppRoles, setEditingAppRoles] = useState<User | null>(null);
  const [editingSuspensions, setEditingSuspensions] = useState<User | null>(
    null
  );

  const load = useCallback(async () => {
    try {
      setData(await api.users.list(search || undefined, page));
      setError(null);
    } catch (e) {
      if (e instanceof NotAuthenticated) return goToLogin();
      setError(e instanceof Error ? e.message : "Chargement impossible.");
    }
  }, [search, page]);

  useEffect(() => {
    load();
  }, [load]);

  useEffect(() => {
    api.roles
      .list()
      .then(setRoles)
      .catch(() => {});
    api.clients
      .list()
      .then(setClients)
      .catch(() => {});
  }, []);

  /** Enveloppe les actions : un refus métier du serveur s'affiche tel quel. */
  async function run(action: () => Promise<unknown>) {
    try {
      await action();
      setError(null);
      load();
    } catch (e) {
      if (e instanceof NotAuthenticated) return goToLogin();
      setError(e instanceof Error ? e.message : "Action impossible.");
    }
  }

  async function remove(user: User) {
    const typed = window.prompt(
      `Saisissez « ${user.email} » pour confirmer la suppression.\n\n` +
        `Préférez la désactivation : elle bloque la connexion sans effacer l'historique.`
    );
    if (typed !== user.email) return;
    run(() => api.users.remove(user.id));
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
        <button className="btn primary" onClick={() => setCreating(true)}>
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
              <button className="btn primary" onClick={() => setCreating(true)}>
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
                                run(() =>
                                  api.users.setRoles(
                                    u.id,
                                    u.roles.filter((r) => r !== "Admin")
                                  )
                                ),
                            }
                          : {
                              label: "Rendre admin",
                              onClick: () =>
                                run(() =>
                                  api.users.setRoles(u.id, [...u.roles, "Admin"])
                                ),
                            },
                        {
                          label: "Rôles applicatifs",
                          onClick: () => setEditingAppRoles(u),
                        },
                        {
                          label: "Mot de passe",
                          onClick: () => setEditingPassword(u),
                        },
                        "separator",
                        {
                          label: "Suspensions",
                          onClick: () => setEditingSuspensions(u),
                        },
                        u.isActive
                          ? {
                              label: "Désactiver",
                              onClick: () => run(() => api.users.disable(u.id)),
                            }
                          : {
                              label: "Réactiver",
                              onClick: () => run(() => api.users.enable(u.id)),
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

            {pageCount > 1 && (
              <div className="pager">
                <button
                  className="btn small"
                  disabled={page <= 1}
                  onClick={() => setPage((p) => p - 1)}
                >
                  Précédent
                </button>
                <span className="pager-label">
                  page {page} sur {pageCount}
                </span>
                <button
                  className="btn small"
                  disabled={page >= pageCount}
                  onClick={() => setPage((p) => p + 1)}
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
          onDone={() => {
            setCreating(false);
            load();
          }}
        />
      )}

      {editingPassword && (
        <PasswordDialog
          user={editingPassword}
          onCancel={() => setEditingPassword(null)}
          onDone={() => setEditingPassword(null)}
        />
      )}

      {editingAppRoles && (
        <ApplicationRolesDialog
          user={editingAppRoles}
          roles={roles}
          clients={clients}
          onCancel={() => setEditingAppRoles(null)}
        />
      )}

      {editingSuspensions && (
        <SuspensionsDialog
          user={editingSuspensions}
          onCancel={() => setEditingSuspensions(null)}
          onChanged={load}
        />
      )}
    </>
  );
}

/* ============================================================
   Création
   ============================================================ */
function CreateDialog({
  onCancel,
  onDone,
}: {
  onCancel: () => void;
  onDone: () => void;
}) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit() {
    setProblem(null);
    setBusy(true);
    try {
      await api.users.create({ email: email.trim(), password });
      onDone();
    } catch (e) {
      if (e instanceof NotAuthenticated) return goToLogin();
      setProblem(e instanceof Error ? e.message : "Création impossible.");
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
          <h2>Créer un compte</h2>
          <p>
            Sans rôle pour l'instant — assignez-les une fois que vous savez
            à quelle(s) application(s) cette personne doit accéder, et avec
            quel rôle.
          </p>
        </div>

        <div className="dialog-body">
          {problem && <div className="notice">{problem}</div>}

          <div className="field">
            <label htmlFor="new-email">Adresse électronique</label>
            <input
              id="new-email"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="prenom.nom@entreprise.com"
              autoFocus
            />
          </div>

          <div className="field">
            <label htmlFor="new-pwd">Mot de passe initial</label>
            <input
              id="new-pwd"
              type="text"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
            <p className="hint">
              Visible volontairement : vous devez pouvoir le transmettre. Huit
              caractères au minimum, avec majuscule, chiffre et caractère
              spécial.
            </p>
          </div>

        </div>

        <div className="dialog-foot">
          <button className="btn" onClick={onCancel} disabled={busy}>
            Annuler
          </button>
          <button className="btn primary" onClick={submit} disabled={busy}>
            {busy ? "Création…" : "Créer le compte"}
          </button>
        </div>
      </div>
    </div>
  );
}

/* ============================================================
   Rôles applicatifs (par application cliente)
   ============================================================ */
function ApplicationRolesDialog({
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

/* ============================================================
   Suspensions temporaires datées (congés, etc.)
   ============================================================ */
function SuspensionsDialog({
  user,
  onCancel,
  onChanged,
}: {
  user: User;
  onCancel: () => void;
  onChanged: () => void;
}) {
  const [list, setList] = useState<UserSuspension[] | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [dateDebut, setDateDebut] = useState("");
  const [dateFin, setDateFin] = useState("");
  const [motif, setMotif] = useState("");
  const [type, setType] = useState<SuspensionType>("Conge");

  const today = new Date().toISOString().slice(0, 10);

  const load = useCallback(async () => {
    try {
      setList(await api.users.suspensions.list(user.id));
    } catch (e) {
      if (e instanceof NotAuthenticated) return goToLogin();
      setProblem(e instanceof Error ? e.message : "Chargement impossible.");
    }
  }, [user.id]);

  useEffect(() => {
    load();
  }, [load]);

  function isCurrentlyActive(s: UserSuspension): boolean {
    const now = Date.now();
    return new Date(s.dateDebut).getTime() <= now && now <= new Date(s.dateFin).getTime();
  }

  async function create() {
    if (!dateDebut || !dateFin || !motif.trim()) {
      setProblem("Dates et motif sont obligatoires.");
      return;
    }
    setProblem(null);
    setBusy(true);
    try {
      // Période couvrant des journées entières : début à 00:00, fin à 23:59.
      await api.users.suspensions.create(user.id, {
        dateDebut: `${dateDebut}T00:00:00`,
        dateFin: `${dateFin}T23:59:59`,
        motif: motif.trim(),
        type,
      });
      setDateDebut("");
      setDateFin("");
      setMotif("");
      await load();
      onChanged();
    } catch (e) {
      if (e instanceof NotAuthenticated) return goToLogin();
      setProblem(e instanceof Error ? e.message : "Création impossible.");
    } finally {
      setBusy(false);
    }
  }

  async function remove(s: UserSuspension) {
    setProblem(null);
    try {
      await api.users.suspensions.remove(user.id, s.id);
      await load();
      onChanged();
    } catch (e) {
      if (e instanceof NotAuthenticated) return goToLogin();
      setProblem(e instanceof Error ? e.message : "Suppression impossible.");
    }
  }

  return (
    <div
      className="veil"
      onClick={(e) => e.target === e.currentTarget && onCancel()}
    >
      <div className="dialog" role="dialog" aria-modal="true">
        <div className="dialog-head">
          <h2>Suspensions</h2>
          <p>{user.email}</p>
        </div>

        <div className="dialog-body">
          {problem && <div className="notice">{problem}</div>}

          {list === null ? (
            <div className="loading">Chargement…</div>
          ) : list.length === 0 ? (
            <p className="hint">Aucune période enregistrée pour ce compte.</p>
          ) : (
            <div className="rows">
              {list.map((s) => (
                <article className="row" key={s.id}>
                  <div className="row-main">
                    <div className="row-title">
                      {formatDate(s.dateDebut)} → {formatDate(s.dateFin)}
                    </div>
                    <div className="row-sub tags">
                      {isCurrentlyActive(s) && (
                        <span className="tag off">en cours</span>
                      )}
                      <span className="tag accent">
                        {SUSPENSION_LABELS[s.type]}
                      </span>
                      <span className="tag">{s.motif}</span>
                    </div>
                  </div>
                  <div className="row-actions">
                    <button
                      className="btn small danger"
                      onClick={() => remove(s)}
                    >
                      Retirer
                    </button>
                  </div>
                </article>
              ))}
            </div>
          )}

          <div className="field" style={{ marginTop: 14 }}>
            <label>Planifier une période</label>

            <div className="date-range">
              <div className="date-field">
                <span className="date-field-label">Du</span>
                <input
                  type="date"
                  value={dateDebut}
                  min={today}
                  onChange={(e) => setDateDebut(e.target.value)}
                />
              </div>
              <span className="date-range-arrow">→</span>
              <div className="date-field">
                <span className="date-field-label">au</span>
                <input
                  type="date"
                  value={dateFin}
                  min={dateDebut || today}
                  onChange={(e) => setDateFin(e.target.value)}
                />
              </div>
            </div>

            <div style={{ display: "flex", gap: 8, marginTop: 10 }}>
              <Select
                style={{ flex: 1, minWidth: 0 }}
                value={type}
                onChange={(e) => setType(e.target.value as SuspensionType)}
              >
                {Object.entries(SUSPENSION_LABELS).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </Select>
            </div>
            <input
              type="text"
              style={{ marginTop: 8 }}
              placeholder="Motif (ex : congés annuels)"
              value={motif}
              onChange={(e) => setMotif(e.target.value)}
            />
            <p className="hint">
              Peut être planifiée à l'avance : le blocage ne prend effet qu'à
              la date de début, et se lève automatiquement à la fin — aucune
              intervention nécessaire.
            </p>
            <button
              className="btn small primary"
              onClick={create}
              disabled={busy}
              style={{ marginTop: 8 }}
            >
              {busy ? "Ajout…" : "Ajouter la période"}
            </button>
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

/* ============================================================
   Mot de passe
   ============================================================ */
function PasswordDialog({
  user,
  onCancel,
  onDone,
}: {
  user: User;
  onCancel: () => void;
  onDone: () => void;
}) {
  const [password, setPassword] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState(false);

  async function submit() {
    setProblem(null);
    setBusy(true);
    try {
      await api.users.setPassword(user.id, password);
      setDone(true);
      setBusy(false);
    } catch (e) {
      if (e instanceof NotAuthenticated) return goToLogin();
      setProblem(e instanceof Error ? e.message : "Modification impossible.");
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
          <h2>Nouveau mot de passe</h2>
          <p>{user.email}</p>
        </div>

        <div className="dialog-body">
          {problem && <div className="notice">{problem}</div>}

          {done ? (
            <p className="hint" style={{ fontSize: 13 }}>
              Mot de passe changé. Transmettez-le à la personne concernée — il
              n'est stocké nulle part en clair et ne pourra pas être relu.
            </p>
          ) : (
            <div className="field">
              <label htmlFor="pwd">Mot de passe</label>
              <input
                id="pwd"
                type="text"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoFocus
              />
              <p className="hint">
                Les sessions déjà ouvertes de cette personne seront invalidées.
              </p>
            </div>
          )}
        </div>

        <div className="dialog-foot">
          {done ? (
            <button className="btn primary" onClick={onDone}>
              Fermer
            </button>
          ) : (
            <>
              <button className="btn" onClick={onCancel} disabled={busy}>
                Annuler
              </button>
              <button className="btn primary" onClick={submit} disabled={busy}>
                {busy ? "Enregistrement…" : "Changer le mot de passe"}
              </button>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
