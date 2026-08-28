# SSO Platform

Système d'authentification unique (SSO) basé sur OAuth2/OIDC, composé de
quatre projets :

| Projet | Rôle | Port par défaut |
|---|---|---|
| `SsoServer` | Serveur d'identité (OpenIddict + ASP.NET Identity) | 5171 |
| `ClientApi` | BFF (Backend For Frontend) OAuth2 | 5200 |
| `sso-client` | Application de démonstration (React) | 5173 |
| `sso-admin` | Interface d'administration (React) | 5174 (dev) ou servie par SsoServer sous `/admin/` |

Flow : `sso-client` → `ClientApi` (garde le `client_secret` et les jetons)
→ `SsoServer` (authentification, émission des jetons). PKCE obligatoire,
rotation des refresh tokens, rôles globaux + rôles par application.

---

## 1. Prérequis

- **.NET SDK 8.0**
- **Node.js 18+** (et npm)
- **SQL Server** accessible (une des options suivantes) :
  - SQL Server Express / Developer déjà installé localement, ou
  - Docker : `docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=VotreMotDePasse123!" -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest`
- `dotnet-ef` (outil CLI) : `dotnet tool install --global dotnet-ef` si vous ne l'avez pas déjà

---

## 2. Cloner et configurer SsoServer

```bash
git clone https://github.com/ABaDi99/Sso2.git
cd Sso2/SsoServer
```

### 2.1 Secrets (jamais commités, à définir localement)

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=SsoServerDb;User Id=sa;Password=VotreMotDePasse123!;TrustServerCertificate=True"
dotnet user-secrets set "Bootstrap:AdminPassword" "AdminMotDePasse123!"
```

Adaptez le mot de passe SQL au vôtre s'il diffère. `Bootstrap:AdminPassword`
est le mot de passe du **premier compte administrateur**, créé
automatiquement au démarrage avec l'email défini dans
`appsettings.Development.json` (`admin@entreprise.com` par défaut).

### 2.2 Adapter l'hôte réseau

Ouvrez `appsettings.Development.json` et réglez `Network:Host` :

- **Pour tester seul, tout sur la même machine** : supprimez la clé
  `Network:Host` (ou mettez `"localhost"`) — le code retombe sur
  `localhost` par défaut.
- **Pour tester avec un binôme sur le même réseau local** : mettez
  **votre propre IP locale** (`ipconfig` → adresse IPv4, ex.
  `192.168.1.72`), et remplissez `Network:BinomeHost` avec l'IP de
  l'autre machine si vous voulez qu'un troisième client OAuth
  (`app-binome`) soit créé automatiquement pour elle.

Cette valeur sert à préconfigurer les `redirect_uri` des applications
OAuth de démonstration (voir `Data/DevClientSeeder.cs`) — si elle ne
correspond pas à l'adresse réellement utilisée dans le navigateur, la
connexion échouera avec une erreur `redirect_uri invalide`.

### 2.3 Créer la base de données

```bash
dotnet ef database update
```

Ça crée le schéma (Identity + OpenIddict). Au premier démarrage, le
serveur amorce aussi automatiquement le rôle `Admin`, le compte
administrateur, les clients OAuth de démonstration et deux comptes de
test (voir section 6).

### 2.4 Lancer

```bash
dotnet run
```

Vérifiez que ça écoute bien sur `http://localhost:5171` (ou votre IP).

---

## 3. Configurer et lancer ClientApi

```bash
cd ../ClientApi
```

Ouvrez `appsettings.json` et remplacez `192.168.1.72` par **la même
valeur** que celle choisie pour `Network:Host` à l'étape 2.2 (`localhost`
si vous testez seul), dans les quatre champs :

```json
{
  "Sso": {
    "Authority": "http://localhost:5171",
    "ClientId": "mon-app-cliente",
    "ClientSecret": "secret-de-test-123",
    "RedirectUri": "http://localhost:5200/auth/callback"
  },
  "Frontend": {
    "Url": "http://localhost:5173"
  }
}
```

Puis :

```bash
dotnet run
```

Écoute sur `http://localhost:5200`.

---

## 4. Configurer et lancer sso-client

```bash
cd ../sso-client
npm install
cp .env.example .env
```

`.env` pointe déjà vers `http://localhost:5200` par défaut — à adapter
uniquement si vous avez changé le port de ClientApi.

```bash
npm run dev
```

Écoute sur `http://localhost:5173`.

---

## 5. Configurer et lancer sso-admin

```bash
cd ../sso-admin
npm install
npm run dev
```

Écoute sur `http://localhost:5174/admin/`, avec un proxy Vite qui
redirige vers `http://localhost:5171` (SsoServer). Aucune configuration
supplémentaire nécessaire si SsoServer tourne en local sur le port par
défaut.

Pour un accès depuis une autre machine du réseau, relancez avec
`npm run dev -- --host` et utilisez l'adresse réseau affichée dans la
console.

---

## 6. Comptes de test

Créés automatiquement au premier démarrage de SsoServer (voir
`Data/BootstrapSeeder.cs` et `Data/DevClientSeeder.cs`) :

| Email | Mot de passe | Rôle |
|---|---|---|
| `admin@entreprise.com` | celui défini via `Bootstrap:AdminPassword` (étape 2.1) | Admin |
| `employe@entreprise.com` | `MotDePasse123!` | Employe |
| `salim3@entreprise.com` | `MotDePasse123!` | Employe |

---

## 7. Tester le scénario complet

1. Ouvrez `http://localhost:5173` (sso-client), cliquez **Se connecter**
   → redirigé vers SsoServer → connectez-vous avec `employe@entreprise.com`
   → redirigé vers le tableau de bord de sso-client.
2. Ouvrez `http://localhost:5174/admin/` (sso-admin), connectez-vous avec
   `admin@entreprise.com` → gérez les comptes, applications et rôles.
3. Dans sso-admin, onglet **Comptes**, assignez un rôle applicatif à
   `employe@entreprise.com` pour l'application `mon-app-cliente`, puis
   reconnectez-vous sur sso-client pour vérifier qu'il apparaît.

---

## 8. Dépannage courant

- **`redirect_uri invalide` en se connectant** : `Network:Host`
  (SsoServer) et `Sso:RedirectUri`/`Sso:Authority` (ClientApi) doivent
  pointer vers la même adresse que celle tapée dans le navigateur.
- **Erreur de connexion SQL Server** : vérifiez que le service tourne
  (`docker ps` si vous utilisez Docker) et que le mot de passe dans
  `ConnectionStrings:DefaultConnection` correspond.
- **`dotnet ef` introuvable** : `dotnet tool install --global dotnet-ef`,
  puis relancez un nouveau terminal.
- **Page blanche sur `/admin/`** : assurez-vous d'accéder à l'URL avec le
  `/admin/` final, pas juste la racine.
