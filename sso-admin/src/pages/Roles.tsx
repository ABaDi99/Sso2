import { useEffect, useState } from "react";
import { api, NotAuthenticated, goToLogin, type Role } from "../api";

export default function RolesPage() {
  const [roles, setRoles] = useState<Role[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [busy, setBusy] = useState(false);

  async function load() {
    try {
      setRoles(await api.roles.list());
      setError(null);
    } catch (e) {
      if (e instanceof NotAuthenticated) return goToLogin();
      setError(e instanceof Error ? e.message : "Chargement impossible.");
    }
  }

  useEffect(() => {
    load();
  }, []);

  async function create() {
    if (!name.trim()) return;
    setBusy(true);
    try {
      await api.roles.create(name.trim());
      setName("");
      setError(null);
      load();
    } catch (e) {
      if (e instanceof NotAuthenticated) return goToLogin();
      setError(e instanceof Error ? e.message : "Création impossible.");
    } finally {
      setBusy(false);
    }
  }

  async function remove(role: Role) {
    if (!window.confirm(`Supprimer le rôle « ${role.name} » ?`)) return;
    try {
      await api.roles.remove(role.name);
      setError(null);
      load();
    } catch (e) {
      if (e instanceof NotAuthenticated) return goToLogin();
      setError(e instanceof Error ? e.message : "Suppression impossible.");
    }
  }

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
          <div className="rows">
            {roles.map((r) => (
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
        )}
      </div>
    </>
  );
}
