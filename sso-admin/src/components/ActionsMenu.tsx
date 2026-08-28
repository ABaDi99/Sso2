import { useEffect, useRef, useState } from "react";

/* ============================================================
   Menu d'actions — regroupe les actions d'une ligne derrière un
   seul bouton, plutôt qu'une rangée qui s'allonge à chaque
   nouvelle fonctionnalité. Composant partagé (pas propre à une
   page) : label personnalisable, s'ouvre vers le haut s'il manque
   de place en dessous (bas de liste, bas d'écran), se ferme au
   scroll pour ne jamais rester flottant au mauvais endroit.
   ============================================================ */
export interface MenuAction {
  label: string;
  onClick: () => void;
  danger?: boolean;
  disabled?: boolean;
}
export type MenuEntry = MenuAction | "separator";

export function ActionsMenu({
  items,
  label = "Actions",
}: {
  items: MenuEntry[];
  label?: string;
}) {
  const [open, setOpen] = useState(false);
  const [openUp, setOpenUp] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;

    const rect = ref.current?.getBoundingClientRect();
    if (rect) {
      const estimatedHeight = 40 * items.length + 20;
      const spaceBelow = window.innerHeight - rect.bottom;
      setOpenUp(spaceBelow < estimatedHeight && rect.top > estimatedHeight);
    }

    function onDocPointerDown(e: PointerEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    }
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") setOpen(false);
    }
    function onScrollOrResize() {
      setOpen(false);
    }

    document.addEventListener("pointerdown", onDocPointerDown);
    document.addEventListener("keydown", onKeyDown);
    window.addEventListener("scroll", onScrollOrResize, true);
    window.addEventListener("resize", onScrollOrResize);
    return () => {
      document.removeEventListener("pointerdown", onDocPointerDown);
      document.removeEventListener("keydown", onKeyDown);
      window.removeEventListener("scroll", onScrollOrResize, true);
      window.removeEventListener("resize", onScrollOrResize);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  return (
    <div className="menu" ref={ref}>
      <button
        type="button"
        className="btn small menu-btn"
        onClick={() => setOpen((o) => !o)}
        aria-haspopup="menu"
        aria-expanded={open}
      >
        {label}
        <svg
          width="11"
          height="11"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2.4"
          strokeLinecap="round"
          strokeLinejoin="round"
        >
          <path d="M6 9l6 6 6-6" />
        </svg>
      </button>

      {open && (
        <div className={"menu-panel" + (openUp ? " up" : "")} role="menu">
          {items.map((item, i) =>
            item === "separator" ? (
              <div className="menu-sep" key={`sep-${i}`} />
            ) : (
              <button
                key={item.label}
                type="button"
                role="menuitem"
                className={"menu-item" + (item.danger ? " danger" : "")}
                disabled={item.disabled}
                onClick={() => {
                  setOpen(false);
                  item.onClick();
                }}
              >
                {item.label}
              </button>
            )
          )}
        </div>
      )}
    </div>
  );
}
