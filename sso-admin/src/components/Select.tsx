/* Enveloppe un <select> natif avec une flèche superposée en vrai SVG,
   plutôt qu'une image de fond CSS — voir styles.css pour pourquoi. */
export function Select({
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
