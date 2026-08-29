import { useEffect, useState } from "react";
import { api, type Role } from "../api";
import { useApiAction } from "../hooks/useApiAction";
import { usePagination } from "../hooks/usePagination";
import { Pager } from "../components/Pager";

const PAGE_SIZE = 5;

export default function RolesPage() {
  const [roles, setRoles] = useState<Role[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [busy, setBusy] = useState(false);
  const { run } = useApiAction(setError);

  async function load() {
    const result = await run(() => api.roles.list(), "Chargement impossible.");
    if (result.success) setRoles(result.value);
  }

  useEffect(() => {
    load();
  }, []);

  async function create() {
    if (!name.trim()) return;
    setBusy(true);
    const result = await run(
      () => api.roles.create(name.trim()),
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
      () => api.roles.remove(role.name),
      "Suppression impossible."
    );
    if (result.success) load();
  }

  const { page, setPage, pageCount, pageItems } = usePagination(
    roles ?? [],
    PAGE_SIZE
  );

  return (
    <>
      <header className="top">
        <div>
          <h1>Rôles</h1>
          <p>
            Ce que la personne est dans l'organisation. Chaque application
            décide ensuite de ce que ça permet chez elle.
          </p>
        </div>
      </header>

      <div className="body">
        {error && <div className="notice">{error}</div>}

        <div className="toolbar">
          <input
            type="text"
            placeholder="Nom du nouveau rôle"
            value={name}
            onChange={(e) => setName(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && create()}
          />
          <button
            className="btn primary"
            onClick={create}
            disabled={busy || !name.trim()}
          >
            Créer
          </button>
        </div>

        {roles === null ? (
          <div className="loading">Chargement…</div>
        ) : roles.length === 0 ? (
          <div className="empty">
            <h2>Aucun rôle</h2>
            <p>
              Créez un rôle pour distinguer les profils : Employe, Comptable,
              Manager. Gardez-les larges — les permissions fines appartiennent à
              chaque application.
            </p>
          </div>
        ) : (
          <>
          <div className="rows">
            {pageItems.map((r) => (
              <article className="row" key={r.id}>
                <div className="row-main">
                  <div className="row-title">{r.name}</div>
                  <div className="row-sub">
                    {r.userCount === 0
                      ? "aucun compte"
                      : `${r.userCount} compte${r.userCount > 1 ? "s" : ""}`}
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
