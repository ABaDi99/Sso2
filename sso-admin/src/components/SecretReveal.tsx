import { useState } from "react";
import type { ClientCreated } from "../api";

/* SIGNATURE — la révélation du secret
   OpenIddict hache le secret avant de l'écrire : c'est la seule
   occasion de le lire. L'interface doit rendre ce moment
   impossible à manquer. */
export function SecretReveal({
  data,
  onClose,
}: {
  data: ClientCreated;
  onClose: () => void;
}) {
  const [copied, setCopied] = useState(false);

  async function copy() {
    if (!data.clientSecret) return;
    try {
      await navigator.clipboard.writeText(data.clientSecret);
      setCopied(true);
      setTimeout(() => setCopied(false), 2200);
    } catch {
      /* le texte reste sélectionnable à la main */
    }
  }

  if (!data.clientSecret) {
    return (
      <div className="reveal">
        <div className="reveal-band">Application publique créée</div>
        <div className="reveal-body">
          <p className="reveal-note" style={{ margin: 0 }}>
            {data.notice}
          </p>
          <div style={{ marginTop: 16 }}>
            <button className="btn" onClick={onClose}>
              J'ai compris
            </button>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="reveal">
      <div className="reveal-band">
        Copiez ce secret maintenant — il ne sera plus jamais affiché
      </div>

      <div className="reveal-body">
        <p className="reveal-label">client_secret</p>

        <div className="secret">
          <code>{data.clientSecret}</code>
          <button className="btn small" onClick={copy}>
            {copied ? "Copié" : "Copier"}
          </button>
        </div>

        <p className="reveal-note">
          Le serveur ne conserve qu'une empreinte du secret, jamais sa valeur.
          Personne ne pourra le retrouver — ni vous, ni un administrateur. En
          cas de perte, il faudra en régénérer un et mettre à jour
          l'application.
        </p>

        <dl className="reveal-pairs">
          <dt>client_id</dt>
          <dd>{data.client.clientId}</dd>
          <dt>Retour</dt>
          <dd>{data.client.redirectUris.join(", ")}</dd>
          <dt>Autorisation</dt>
          <dd>{window.location.origin}/connect/authorize</dd>
          <dt>Jeton</dt>
          <dd>{window.location.origin}/connect/token</dd>
        </dl>

        <div style={{ marginTop: 18 }}>
          <button className="btn" onClick={onClose}>
            J'ai copié le secret
          </button>
        </div>
      </div>
    </div>
  );
}
