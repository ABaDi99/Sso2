import axios from "axios";

const BASE_URL = import.meta.env.VITE_API_URL;

const api = axios.create({
  baseURL: BASE_URL,
  withCredentials: true,
});

export interface User {
  id: string;
  email: string;
  name: string;
  roles: string[];
}

export function isAdmin(user: User | null): boolean {
  return user?.roles?.includes("Admin") ?? false;
}

export async function getCurrentUser(): Promise<User | null> {
  try {
    const response = await api.get<User>("/auth/me");
    return response.data;
  } catch {
    return null;
  }
}

export function login(): void {
  window.location.href = `${BASE_URL}/auth/login`;
}

export interface HealthStatus {
  status: string;
}

export async function getHealth(): Promise<HealthStatus | null> {
  try {
    const response = await api.get<HealthStatus>("/auth/health");
    return response.data;
  } catch {
    return null;
  }
}

export interface SecretData {
  message: string;
  pour: string;
  genere: string;
}

export async function getSecret(): Promise<SecretData | null> {
  try {
    const response = await api.get<SecretData>("/auth/secret");
    return response.data;
  } catch {
    return null;
  }
}

export function logout(): void {
  window.location.href = `${BASE_URL}/auth/logout`;
}

export interface Announcement {
  id: number;
  title: string;
  content: string;
  author: string;
  createdAt: string;
}

export interface AnnouncementInput {
  title: string;
  content: string;
}

/** Erreur levée quand le serveur refuse une action réservée aux admins. */
export class ForbiddenError extends Error {
  constructor() {
    super("Vous n'avez pas les droits nécessaires pour effectuer cette action.");
  }
}

function throwIfForbidden(error: unknown): never {
  if (axios.isAxiosError(error) && error.response?.status === 403) {
    throw new ForbiddenError();
  }
  throw error;
}

export async function getAnnouncements(): Promise<Announcement[]> {
  try {
    const response = await api.get<Announcement[]>("/announcements");
    return response.data;
  } catch {
    return [];
  }
}

export async function createAnnouncement(input: AnnouncementInput): Promise<Announcement> {
  try {
    const response = await api.post<Announcement>("/announcements", input);
    return response.data;
  } catch (error) {
    throwIfForbidden(error);
  }
}

export async function updateAnnouncement(
  id: number,
  input: AnnouncementInput
): Promise<Announcement> {
  try {
    const response = await api.put<Announcement>(`/announcements/${id}`, input);
    return response.data;
  } catch (error) {
    throwIfForbidden(error);
  }
}

export async function deleteAnnouncement(id: number): Promise<void> {
  try {
    await api.delete(`/announcements/${id}`);
  } catch (error) {
    throwIfForbidden(error);
  }
}
