import { useEffect, useRef, useState } from "react";

export interface SearchSelectOption {
  value: string;
  label: string;
  /** Texte secondaire affiché sous le libellé (ex. l'application d'un rôle). */
  hint?: string;
}

/**
 * Remplace un <select> natif quand la liste est longue : on tape pour
 * filtrer au lieu de faire défiler toutes les options une par une.
 * Garde la même sémantique contrôlée qu'un select (value/onChange par
 * valeur), pas par option complète, pour rester un remplacement direct.
 */
export function SearchSelect({
  value,
  onChange,
  options,
  placeholder = "Rechercher…",
  style,
}: {
  value: string;
  onChange: (value: string) => void;
  options: SearchSelectOption[];
  placeholder?: string;
  style?: React.CSSProperties;
}) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const rootRef = useRef<HTMLDivElement>(null);

  const selected = options.find((o) => o.value === value) ?? null;

  useEffect(() => {
    function onClickOutside(e: MouseEvent) {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) {
        setOpen(false);
        setQuery("");
      }
    }
    document.addEventListener("mousedown", onClickOutside);
    return () => document.removeEventListener("mousedown", onClickOutside);
  }, []);

  const term = query.trim().toLowerCase();
  const filtered = term
    ? options.filter(
        (o) =>
          o.label.toLowerCase().includes(term) ||
          (o.hint ?? "").toLowerCase().includes(term)
      )
    : options;

  function pick(v: string) {
    onChange(v);
    setOpen(false);
    setQuery("");
  }

  return (
    <div className="search-select" ref={rootRef} style={style}>
      <input
        type="text"
        className="search-select-input"
        placeholder={placeholder}
        value={open ? query : selected?.label ?? ""}
        onFocus={() => {
          setOpen(true);
          setQuery("");
        }}
        onChange={(e) => setQuery(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === "Escape") {
            setOpen(false);
            setQuery("");
            (e.target as HTMLInputElement).blur();
          }
          if (e.key === "Enter" && filtered.length > 0) {
            pick(filtered[0].value);
          }
        }}
      />
      {open && (
        <div className="search-select-menu">
          {filtered.length === 0 ? (
            <div className="search-select-empty">Aucun résultat</div>
          ) : (
            filtered.map((o) => (
              <button
                type="button"
                key={o.value}
                className={
                  "search-select-item" + (o.value === value ? " active" : "")
                }
                onMouseDown={(e) => {
                  // onMouseDown plutôt que onClick : évite que le blur de
                  // l'input (déclenché avant le click) ne ferme le menu
                  // avant que la sélection ne soit prise en compte.
                  e.preventDefault();
                  pick(o.value);
                }}
              >
                <span>{o.label}</span>
                {o.hint && <span className="search-select-hint">{o.hint}</span>}
              </button>
            ))
          )}
        </div>
      )}
    </div>
  );
}
