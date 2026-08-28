import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { getCurrentUser, type User } from "../api";

/** Charge l'utilisateur courant ; redirige vers l'accueil si aucune session n'est active. */
export function useCurrentUser() {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);
  const navigate = useNavigate();

  useEffect(() => {
    getCurrentUser().then((result) => {
      setLoading(false);
      if (result === null) {
        navigate("/", { replace: true });
        return;
      }
      setUser(result);
    });
  }, [navigate]);

  return { user, loading };
}
