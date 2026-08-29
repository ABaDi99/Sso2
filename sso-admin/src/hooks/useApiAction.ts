import { useCallback } from "react";
import { NotAuthenticated, goToLogin } from "../api";

export type ActionResult<T> =
  | { success: true; value: T }
  | { success: false };

/**
 * Enveloppe un appel API : redirige vers le login si la session a expiré,
 * sinon pose le message d'erreur sur le setter fourni par l'appelant.
 * Remplace le bloc `if (e instanceof NotAuthenticated) return goToLogin();
 * setError(...)` autrement recopié à l'identique dans chaque page/dialogue.
 *
 * Renvoie { success, value } plutôt que simplement `T | undefined` : une
 * action qui réussit et renvoie `void` (ex: DELETE) doit rester
 * distinguable d'un échec, ce qu'une simple comparaison à `undefined` ne
 * permettrait pas.
 *
 * `run` est mémoïsé (useCallback) : les appelants s'en servent souvent
 * comme dépendance d'un autre useCallback/useEffect (voir Users.tsx), une
 * nouvelle identité à chaque rendu y déclencherait une boucle de rechargement.
 */
export function useApiAction(setError: (message: string | null) => void) {
  const run = useCallback(
    async function run<T>(
      action: () => Promise<T>,
      fallbackMessage: string
    ): Promise<ActionResult<T>> {
      try {
        const value = await action();
        setError(null);
        return { success: true, value };
      } catch (e) {
        if (e instanceof NotAuthenticated) {
          goToLogin();
          return { success: false };
        }
        setError(e instanceof Error ? e.message : fallbackMessage);
        return { success: false };
      }
    },
    [setError]
  );

  return { run };
}
