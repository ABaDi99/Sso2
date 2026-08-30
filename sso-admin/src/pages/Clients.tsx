import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { api, type Client, type ClientCreated } from "../api";
import { ActionsMenu } from "../components/ActionsMenu";
import { SecretReveal } from "../components/SecretReveal";
import { CreateClientDialog } from "../components/CreateClientDialog";
import { EditClientDialog } from "../components/EditClientDialog";
import { useApiAction } from "../hooks/useApiAction";
import { usePagination } from "../hooks/usePagination";
import { Pager } from "../components/Pager";

const PAGE_SIZE = 5;

export default function ClientsPage() {
  const navigate = useNavigate();
  const [clients, setClients] = useState<Client[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [revealed, setRevealed] = useState<ClientCreated | null>(null);
  const [editing, setEditing] = useState<Client | null>(null);
  const { run } = useApiAction(setError);

  async function load() {
    const result = await run(() => api.clients.list(), "Chargement impossible.");
    if (result.success) setClients(result.value);
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

    const result = await run(
      () => api.clients.remove(client.id),
      "Suppression impossible."
    );
    if (result.success) load();
  }

  async function rotate(client: Client) {
    if (
      !window.confirm(
        `Régénérer le secret de ${client.clientId} ?\n\n` +
          `L'ancien cessera immédiatement de fonctionner.`
      )
    )
      return;

    const result = await run(
      () => api.clients.rotateSecret(client.id),
      "Régénération impossible."
    );
    if (result.success)
      setRevealed({
        client,
        clientSecret: result.value.clientSecret,
        notice: result.value.notice,
      });
  }

  const { page, setPage, pageCount, pageItems } = usePagination(
    clients ?? [],
    PAGE_SIZE
  );

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
            {pageItems.map((c) => (
              <article className="row" key={c.id}>
                <div className="row-main">
                  <button
                    type="button"
                    className="row-title row-title-btn"
                    onClick={() => navigate(`/clients/${c.id}`)}
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
                      { label: "Voir les détails", onClick: () => navigate(`/clients/${c.id}`) },
                      { label: "Modifier", onClick: () => setEditing(c) },
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

          <Pager page={page} pageCount={pageCount} onChange={setPage} />
          </>
        )}
      </div>

      {creating && (
        <CreateClientDialog
          onCancel={() => setCreating(false)}
          onCreated={(result) => {
            setCreating(false);
            setRevealed(result);
            load();
          }}
        />
      )}

      {editing && (
        <EditClientDialog
          client={editing}
          onCancel={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            load();
          }}
        />
      )}
    </>
  );
}
