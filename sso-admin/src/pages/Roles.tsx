import { useEffect, useState } from "react";
import { api, type Client, type Role } from "../api";
import { useApiAction } from "../hooks/useApiAction";
import { usePagination } from "../hooks/usePagination";
import { Pager } from "../components/Pager";
import { SearchSelect } from "../components/SearchSelect";

const PAGE_SIZE = 5;

export default function RolesPage() {
  const [roles, setRoles] = useState<Role[] | null>(null);
  const [clients, setClients] = useState<Client[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [clientId, setClientId] = useState("");
  const [filterClientId, setFilterClientId] = useState("");
  const [busy, setBusy] = useState(false);
  const { run } = useApiAction(setError);

  async function load() {
    const result = await run(() => api.roles.list(), "Chargement impossible.");
    if (result.success) setRoles(result.value);
  }

  useEffect(() => {
    load();
    run(() => api.clients.list(), "Chargement des applications impossible.").then(
      (r) => {
        if (r.success) {
          setClients(r.value);
          if (r.value.length > 0) setClientId((c) => c || r.value[0].clientId);
        }
      }
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function create() {
    if (!name.trim() || !clientId) return;
    setBusy(true);
    const result = await run(
      () => api.roles.create(name.trim(), clientId),
      "Création impossible."
    );
    setBusy(false);
    if (result.success) {
      setName("");
      load();
    }
  }

  async function remove(role: Role) {
    if (!window.confirm(`Supprimer le rôle « ${role.name} » ?`)) return;
    const result = await run(
      () => api.roles.remove(role.id),
      "Suppression impossible."
    );
    if (result.success) load();
  }

  const filteredRoles = (roles ?? []).filter((r) => {
    if (!filterClientId) return true;
    if (filterClientId === "global") return r.clientId === null;
    return r.clientId === filterClientId;
  });

  const { page, setPage, pageCount, pageItems } = usePagination(
    filteredRoles,
    PAGE_SIZE
  );

  return (
    <>
      <header className="top">
        <div>
          <h1>Rôles</h1>
          <p>
            Un rôle appartient à une seule application (sauf Admin, qui reste
            global) — deux applications peuvent avoir chacune un rôle du même
            nom sans se marcher dessus.
          </p>
        </div>
      </header>

      <div className="body">
        {error && <div className="notice">{error}</div>}

        {clients.length === 0 ? (
          roles !== null && (
            <p className="hint" style={{ marginBottom: 16 }}>
              Aucune application déclarée — déclarez-en une dans l'onglet
              Applications avant de créer un rôle.
            </p>
          )
        ) : (
          <div className="panel">
            <h2>Créer un rôle</h2>
            <div className="panel-fields">
              <div className="field">
                <label>Application</label>
                <SearchSelect
                  value={clientId}
                  onChange={setClientId}
                  placeholder="Choisir l'application…"
                  options={clients.map((c) => ({
                    value: c.clientId,
                    label: c.displayName ?? c.clientId,
                  }))}
                />
              </div>
              <div className="field">
                <label>Nom du rôle</label>
                <input
                  type="text"
                  placeholder="Employe, Comptable, Manager…"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  onKeyDown={(e) => e.key === "Enter" && create()}
                />
              </div>
              <button
                className="btn primary"
                onClick={create}
                disabled={busy || !name.trim() || !clientId}
              >
                Créer
              </button>
            </div>
            <p className="hint" style={{ marginTop: 10, marginBottom: 0 }}>
              Le rôle sera propre à l'application choisie — une autre
              application peut avoir un rôle du même nom sans conflit.
            </p>
          </div>
        )}

        {roles !== null && roles.length > 0 && (
          <div className="field" style={{ maxWidth: 320 }}>
            <label>Filtrer la liste par application</label>
            <SearchSelect
              value={filterClientId}
              onChange={setFilterClientId}
              placeholder="Toutes les applications…"
              options={[
                { value: "", label: "Toutes les applications" },
                { value: "global", label: "Admin (global)" },
                ...clients.map((c) => ({
                  value: c.clientId,
                  label: c.displayName ?? c.clientId,
                })),
              ]}
            />
          </div>
        )}

        {roles === null ? (
          <div className="loading">Chargement…</div>
        ) : roles.length === 0 ? (
          <div className="empty">
            <h2>Aucun rôle</h2>
            <p>
              Créez un rôle pour une application : Employe, Comptable,
              Manager. Gardez-les larges — les permissions fines appartiennent
              à chaque application.
            </p>
          </div>
        ) : filteredRoles.length === 0 ? (
          <div className="empty">
            <h2>Aucun rôle pour ce filtre</h2>
            <p>Cette application n'a aucun rôle — changez de filtre ou créez-en un ci-dessus.</p>
          </div>
        ) : (
          <>
          <div className="rows">
            {pageItems.map((r) => (
              <article className="row" key={r.id}>
                <div className="row-main">
                  <div className="row-title">{r.name}</div>
                  <div className="row-sub tags">
                    <span className={"tag" + (r.clientId ? " accent" : "")}>
                      {r.clientDisplayName ?? "global"}
                    </span>
                    <span className="tag">
                      {r.userCount === 0
                        ? "aucun compte"
                        : `${r.userCount} compte${r.userCount > 1 ? "s" : ""}`}
                    </span>
                  </div>
                </div>

                <div className="row-actions">
                  {r.name === "Admin" ? (
                    <span className="tag">protégé</span>
                  ) : (
                    <button
                      className="btn small danger"
                      onClick={() => remove(r)}
                    >
                      Supprimer
                    </button>
                  )}
                </div>
              </article>
            ))}
          </div>

          <Pager page={page} pageCount={pageCount} onChange={setPage} />
          </>
        )}
      </div>
    </>
  );
}
