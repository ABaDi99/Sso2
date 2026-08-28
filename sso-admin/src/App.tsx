import { useEffect, useState } from "react";
import {
  BrowserRouter,
  Routes,
  Route,
  NavLink,
  Navigate,
} from "react-router-dom";
import { api, goToLogin, type Session } from "./api";
import ClientsPage from "./pages/Clients";
import UsersPage from "./pages/Users";
import RolesPage from "./pages/Roles";
import "./styles.css";

/* ============================================================
   Thème — partage la clé de la page de connexion, pour que le
   choix suive l'utilisateur d'un écran à l'autre.
   ============================================================ */
function applyStoredTheme() {
  try {
    const saved = localStorage.getItem("sso-theme");
    const dark = saved
      ? saved === "dark"
      : window.matchMedia("(prefers-color-scheme: dark)").matches;
    if (dark) document.documentElement.setAttribute("data-theme", "dark");
  } catch {
    /* stockage indisponible */
  }
}
applyStoredTheme();

function ThemeToggle() {
  const [dark, setDark] = useState(
    () => document.documentElement.getAttribute("data-theme") === "dark"
  );

  function flip() {
    const next = !dark;
    if (next) document.documentElement.setAttribute("data-theme", "dark");
    else document.documentElement.removeAttribute("data-theme");
    try {
      localStorage.setItem("sso-theme", next ? "dark" : "light");
    } catch {
      /* stockage indisponible */
    }
    setDark(next);
  }

  return (
    <button
      className="icon-btn"
      onClick={flip}
      aria-label="Basculer le thème sombre"
      aria-pressed={dark}
    >
      <svg
        className="sun"
        width="15"
        height="15"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
      >
        <circle cx="12" cy="12" r="4.2" />
        <path d="M12 2.6v2M12 19.4v2M2.6 12h2M19.4 12h2M5.4 5.4l1.4 1.4M17.2 17.2l1.4 1.4M18.6 5.4l-1.4 1.4M6.8 17.2l-1.4 1.4" />
      </svg>
      <svg
        className="moon"
        width="15"
        height="15"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M20.5 14.3A8.5 8.5 0 1 1 9.7 3.5a6.9 6.9 0 0 0 10.8 10.8z" />
      </svg>
    </button>
  );
}

/* ============================================================
   Application
   ============================================================ */
export default function App() {
  const [session, setSession] = useState<Session | null>(null);
  const [checking, setChecking] = useState(true);

  useEffect(() => {
    api
      .session()
      .then(setSession)
      .catch(() => setSession({ authenticated: false }))
      .finally(() => setChecking(false));
  }, []);

  if (checking) {
    return <div className="loading">Vérification de la session…</div>;
  }

  const isAdmin = session?.authenticated && session.roles?.includes("Admin");

  if (!session?.authenticated) {
    return (
      <div className="gate">
        <div className="gate-inner">
          <h1>Session absente</h1>
          <p>
            Connectez-vous sur le serveur d'identité pour accéder à
            l'administration.
          </p>
          <button className="btn primary" onClick={goToLogin}>
            Aller à la connexion
          </button>
        </div>
      </div>
    );
  }

  if (!isAdmin) {
    return (
      <div className="gate">
        <div className="gate-inner">
          <h1>Accès refusé</h1>
          <p>
            Le compte {session.email} n'a pas le rôle Admin. Demandez-le à un
            administrateur, puis reconnectez-vous — le rôle n'est lu qu'à
            l'ouverture de session.
          </p>
          <button
            className="btn"
            onClick={async () => {
              await api.logout().catch(() => {});
              goToLogin();
            }}
          >
            Changer de compte
          </button>
        </div>
      </div>
    );
  }

  return (
    <BrowserRouter basename="/admin">
      <div className="shell">
        <aside className="side">
          <div className="logotype">
            S<em>so</em>
          </div>

          <nav className="nav">
            <NavLink
              to="/clients"
              className={({ isActive }) => (isActive ? "on" : "")}
            >
              Applications
            </NavLink>
            <NavLink
              to="/users"
              className={({ isActive }) => (isActive ? "on" : "")}
            >
              Comptes
            </NavLink>
            <NavLink
              to="/roles"
              className={({ isActive }) => (isActive ? "on" : "")}
            >
              Rôles
            </NavLink>
          </nav>

          <div className="side-foot">
            <div className="who">{session.email}</div>
            <div style={{ display: "flex", gap: 8 }}>
              <ThemeToggle />
              <button
                className="btn small"
                onClick={async () => {
                  await api.logout().catch(() => {});
                  goToLogin();
                }}
              >
                Se déconnecter
              </button>
            </div>
          </div>
        </aside>

        <main className="main">
          <Routes>
            <Route path="/" element={<Navigate to="/clients" replace />} />
            <Route path="/clients" element={<ClientsPage />} />
            <Route path="/users" element={<UsersPage />} />
            <Route path="/roles" element={<RolesPage />} />
          </Routes>
        </main>
      </div>
    </BrowserRouter>
  );
}
