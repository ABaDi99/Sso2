import { useState } from "react";
import { api, NotAuthenticated, goToLogin } from "../api";

export function CreateUserDialog({
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
