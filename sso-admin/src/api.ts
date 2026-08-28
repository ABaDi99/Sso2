/* ============================================================
   Couche réseau
   Chemins relatifs : le proxy Vite les transmet à SsoServer en
   développement, et en production tout part de la même origine.
   ============================================================ */

export class NotAuthenticated extends Error {
  constructor() {
    super("Session absente ou expirée");
  }
}

export class ApiError extends Error {
  public readonly problems: string[];

  constructor(message: string, problems: string[] = []) {
    super(message);
    this.problems = problems;
  }
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const res = await fetch(path, {
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
      // Exigé par le filtre anti-CSRF du serveur sur les requêtes
      // qui modifient des données. Un formulaire hébergé sur un autre
      // site ne peut pas ajouter d'en-tête personnalisé.
      "X-Sso-Admin": "1",
    },
    ...init,
  });

  if (res.status === 401) throw new NotAuthenticated();

  if (res.status === 204) return undefined as T;

  let body: any = null;
  try {
    body = await res.json();
  } catch {
    /* réponse sans corps */
  }

  if (!res.ok) {
    // Le serveur renvoie soit { error }, soit { errors: [...] }.
    if (Array.isArray(body?.errors)) {
      throw new ApiError(body.errors[0] ?? "La requête a échoué.", body.errors);
    }
    throw new ApiError(body?.error ?? `La requête a échoué (${res.status}).`);
  }

  return body as T;
}

const get = <T>(path: string) => request<T>(path);
const post = <T>(path: string, data?: unknown) =>
  request<T>(path, {
    method: "POST",
    body: data ? JSON.stringify(data) : undefined,
  });
const put = <T>(path: string, data: unknown) =>
  request<T>(path, { method: "PUT", body: JSON.stringify(data) });
const del = (path: string) => request<void>(path, { method: "DELETE" });

/* ============================================================
     Types
     ============================================================ */

export interface Session {
  authenticated: boolean;
  email?: string;
  roles?: string[];
}

export interface Client {
  id: string;
  clientId: string;
  displayName: string | null;
  clientType: string;
  redirectUris: string[];
  postLogoutRedirectUris: string[];
  permissions: string[];
  hasSecret: boolean;
}

export interface ClientCreated {
  client: Client;
  clientSecret: string | null;
  notice: string;
}

export interface ClientRoleAssignment {
  id: number;
  userId: string;
  userEmail: string;
  roleName: string;
}

export interface CreateClientBody {
  clientId: string;
  displayName?: string;
  clientType: "confidential" | "public";
  redirectUris: string[];
  postLogoutRedirectUris?: string[];
  scopes?: string[];
}

export interface User {
  id: string;
  email: string | null;
  userName: string | null;
  phoneNumber: string | null;
  emailConfirmed: boolean;
  isActive: boolean;
  isSuspended: boolean;
  suspendedUntil: string | null;
  roles: string[];
}

export interface UserList {
  items: User[];
  total: number;
  page: number;
  pageSize: number;
}

export interface Role {
  id: string;
  name: string;
  userCount: number;
}

export interface UserApplicationRole {
  id: number;
  clientId: string;
  clientDisplayName: string;
  roleId: string;
  roleName: string;
}

export type SuspensionType = "Conge" | "Disciplinaire" | "Autre";

export interface UserSuspension {
  id: number;
  dateDebut: string;
  dateFin: string;
  motif: string;
  type: SuspensionType;
  createdBy: string;
  createdAt: string;
}

export interface SuspensionInput {
  dateDebut: string;
  dateFin: string;
  motif: string;
  type: SuspensionType;
}

/* ============================================================
     Appels
     ============================================================ */

export const api = {
  session: () => get<Session>("/api/account/session"),
  logout: () => post<{ success: boolean }>("/api/account/logout"),

  clients: {
    list: () => get<Client[]>("/admin/api/clients"),
    create: (body: CreateClientBody) =>
      post<ClientCreated>("/admin/api/clients", body),
    update: (
      id: string,
      body: { displayName?: string; redirectUris: string[]; scopes?: string[] }
    ) => put<Client>(`/admin/api/clients/${id}`, body),
    rotateSecret: (id: string) =>
      post<{ clientSecret: string; notice: string }>(
        `/admin/api/clients/${id}/rotate-secret`
      ),
    remove: (id: string) => del(`/admin/api/clients/${id}`),
    appRoles: (id: string) =>
      get<ClientRoleAssignment[]>(`/admin/api/clients/${id}/app-roles`),
  },

  users: {
    list: (search?: string, page = 1) =>
      get<UserList>(
        `/admin/api/users?page=${page}` +
          (search ? `&search=${encodeURIComponent(search)}` : "")
      ),
    create: (body: { email: string; password: string; roles?: string[] }) =>
      post<User>("/admin/api/users", body),
    setRoles: (id: string, roles: string[]) =>
      put<User>(`/admin/api/users/${id}/roles`, { roles }),
    setPassword: (id: string, newPassword: string) =>
      post<{ success: boolean }>(`/admin/api/users/${id}/reset-password`, {
        newPassword,
      }),
    disable: (id: string) => post<User>(`/admin/api/users/${id}/disable`),
    enable: (id: string) => post<User>(`/admin/api/users/${id}/enable`),
    remove: (id: string) => del(`/admin/api/users/${id}`),

    appRoles: {
      list: (userId: string) =>
        get<UserApplicationRole[]>(`/admin/api/users/${userId}/app-roles`),
      assign: (userId: string, clientId: string, roleName: string) =>
        post<UserApplicationRole>(`/admin/api/users/${userId}/app-roles`, {
          clientId,
          roleName,
        }),
      remove: (userId: string, appRoleId: number) =>
        del(`/admin/api/users/${userId}/app-roles/${appRoleId}`),
    },

    suspensions: {
      list: (userId: string) =>
        get<UserSuspension[]>(`/admin/api/users/${userId}/suspensions`),
      create: (userId: string, body: SuspensionInput) =>
        post<UserSuspension>(`/admin/api/users/${userId}/suspensions`, body),
      update: (userId: string, suspensionId: number, body: SuspensionInput) =>
        put<UserSuspension>(
          `/admin/api/users/${userId}/suspensions/${suspensionId}`,
          body
        ),
      remove: (userId: string, suspensionId: number) =>
        del(`/admin/api/users/${userId}/suspensions/${suspensionId}`),
    },
  },

  roles: {
    list: () => get<Role[]>("/admin/api/roles"),
    create: (name: string) =>
      post<{ name: string }>("/admin/api/roles", { name }),
    remove: (name: string) =>
      del(`/admin/api/roles/${encodeURIComponent(name)}`),
  },
};

/** Renvoie vers la page de connexion du serveur, avec retour ici. */
export function goToLogin() {
  const back = encodeURIComponent(
    window.location.pathname + window.location.search
  );
  window.location.href = `/Account/Login?ReturnUrl=${back}`;
}
