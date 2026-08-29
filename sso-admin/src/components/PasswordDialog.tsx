import { useState } from "react";
import { api, NotAuthenticated, goToLogin, type User } from "../api";

export function PasswordDialog({
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
