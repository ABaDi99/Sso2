import { useCallback, useEffect, useState } from "react";
import {
  api,
  NotAuthenticated,
  goToLogin,
  type SuspensionType,
  type User,
  type UserSuspension,
} from "../api";
import { Select } from "./Select";
import { formatDate } from "../lib/format";

const SUSPENSION_LABELS: Record<SuspensionType, string> = {
  Conge: "Congé",
  Disciplinaire: "Disciplinaire",
  Autre: "Autre",
};

export function SuspensionsDialog({
  user,
  onCancel,
  onChanged,
}: {
  user: User;
  onCancel: () => void;
  onChanged: () => void;
}) {
  const [list, setList] = useState<UserSuspension[] | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [dateDebut, setDateDebut] = useState("");
  const [dateFin, setDateFin] = useState("");
  const [motif, setMotif] = useState("");
  const [type, setType] = useState<SuspensionType>("Conge");

  const today = new Date().toISOString().slice(0, 10);

  const load = useCallback(async () => {
    try {
      setList(await api.users.suspensions.list(user.id));
    } catch (e) {
      if (e instanceof NotAuthenticated) return goToLogin();
      setProblem(e instanceof Error ? e.message : "Chargement impossible.");
    }
  }, [user.id]);

  useEffect(() => {
    load();
  }, [load]);

  function isCurrentlyActive(s: UserSuspension): boolean {
    const now = Date.now();
    return new Date(s.dateDebut).getTime() <= now && now <= new Date(s.dateFin).getTime();
  }

  async function create() {
    if (!dateDebut || !dateFin || !motif.trim()) {
      setProblem("Dates et motif sont obligatoires.");
      return;
    }
    setProblem(null);
    setBusy(true);
    try {
      // Période couvrant des journées entières : début à 00:00, fin à 23:59.
      await api.users.suspensions.create(user.id, {
        dateDebut: `${dateDebut}T00:00:00`,
        dateFin: `${dateFin}T23:59:59`,
        motif: motif.trim(),
        type,
      });
      setDateDebut("");
      setDateFin("");
      setMotif("");
      await load();
      onChanged();
    } catch (e) {
      if (e instanceof NotAuthenticated) return goToLogin();
      setProblem(e instanceof Error ? e.message : "Création impossible.");
    } finally {
      setBusy(false);
    }
  }

  async function remove(s: UserSuspension) {
    setProblem(null);
    try {
      await api.users.suspensions.remove(user.id, s.id);
      await load();
      onChanged();
    } catch (e) {
      if (e instanceof NotAuthenticated) return goToLogin();
      setProblem(e instanceof Error ? e.message : "Suppression impossible.");
    }
  }

  return (
    <div
      className="veil"
      onClick={(e) => e.target === e.currentTarget && onCancel()}
    >
      <div className="dialog" role="dialog" aria-modal="true">
        <div className="dialog-head">
          <h2>Suspensions</h2>
          <p>{user.email}</p>
        </div>

        <div className="dialog-body">
          {problem && <div className="notice">{problem}</div>}

          {list === null ? (
            <div className="loading">Chargement…</div>
          ) : list.length === 0 ? (
            <p className="hint">Aucune période enregistrée pour ce compte.</p>
          ) : (
            <div className="rows">
              {list.map((s) => (
                <article className="row" key={s.id}>
                  <div className="row-main">
                    <div className="row-title">
                      {formatDate(s.dateDebut)} → {formatDate(s.dateFin)}
                    </div>
                    <div className="row-sub tags">
                      {isCurrentlyActive(s) && (
                        <span className="tag off">en cours</span>
                      )}
                      <span className="tag accent">
                        {SUSPENSION_LABELS[s.type]}
                      </span>
                      <span className="tag">{s.motif}</span>
                    </div>
                  </div>
                  <div className="row-actions">
                    <button
                      className="btn small danger"
                      onClick={() => remove(s)}
                    >
                      Retirer
                    </button>
                  </div>
                </article>
              ))}
            </div>
          )}

          <div className="field" style={{ marginTop: 14 }}>
            <label>Planifier une période</label>

            <div className="date-range">
              <div className="date-field">
                <span className="date-field-label">Du</span>
                <input
                  type="date"
                  value={dateDebut}
                  min={today}
                  onChange={(e) => setDateDebut(e.target.value)}
                />
              </div>
              <span className="date-range-arrow">→</span>
              <div className="date-field">
                <span className="date-field-label">au</span>
                <input
                  type="date"
                  value={dateFin}
                  min={dateDebut || today}
                  onChange={(e) => setDateFin(e.target.value)}
                />
              </div>
            </div>

            <div style={{ display: "flex", gap: 8, marginTop: 10 }}>
              <Select
                style={{ flex: 1, minWidth: 0 }}
                value={type}
                onChange={(e) => setType(e.target.value as SuspensionType)}
              >
                {Object.entries(SUSPENSION_LABELS).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </Select>
            </div>
            <input
              type="text"
              style={{ marginTop: 8 }}
              placeholder="Motif (ex : congés annuels)"
              value={motif}
              onChange={(e) => setMotif(e.target.value)}
            />
            <p className="hint">
              Peut être planifiée à l'avance : le blocage ne prend effet qu'à
              la date de début, et se lève automatiquement à la fin — aucune
              intervention nécessaire.
            </p>
            <button
              className="btn small primary"
              onClick={create}
              disabled={busy}
              style={{ marginTop: 8 }}
            >
              {busy ? "Ajout…" : "Ajouter la période"}
            </button>
          </div>
        </div>

        <div className="dialog-foot">
          <button className="btn primary" onClick={onCancel}>
            Fermer
          </button>
        </div>
      </div>
    </div>
  );
}
