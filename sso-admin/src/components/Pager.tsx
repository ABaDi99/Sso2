/** Partie rendu de la pagination — commune à la pagination côté client
 *  (Clients, Roles) et côté serveur (Users), qui ne partagent que ça. */
export function Pager({
  page,
  pageCount,
  onChange,
}: {
  page: number;
  pageCount: number;
  onChange: (page: number) => void;
}) {
  if (pageCount <= 1) return null;

  return (
    <div className="pager">
      <button
        className="btn small"
        disabled={page <= 1}
        onClick={() => onChange(Math.max(1, page - 1))}
      >
        Précédent
      </button>
      <span className="pager-label">
        page {page} sur {pageCount}
      </span>
      <button
        className="btn small"
        disabled={page >= pageCount}
        onClick={() => onChange(Math.min(pageCount, page + 1))}
      >
        Suivant
      </button>
    </div>
  );
}
