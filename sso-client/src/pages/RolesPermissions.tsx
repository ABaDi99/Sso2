import { useEffect, useState } from "react";
import { ShieldCheck, Plus, Trash2 } from "lucide-react";
import Layout from "../components/Layout";
import { useCurrentUser } from "../hooks/useCurrentUser";
import {
  createRoleDefinition,
  deleteRoleDefinition,
  getPermissionCatalog,
  getRoleDefinitions,
  setRolePermissions,
  ForbiddenError,
  type PermissionDefinition,
  type RoleDefinition,
} from "../api";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";

export default function RolesPermissions() {
  const { user, loading: userLoading } = useCurrentUser();
  const [roles, setRoles] = useState<RoleDefinition[]>([]);
  const [catalog, setCatalog] = useState<PermissionDefinition[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showCreate, setShowCreate] = useState(false);
  const [newName, setNewName] = useState("");
  const [newDescription, setNewDescription] = useState("");
  const [busyRole, setBusyRole] = useState<string | null>(null);

  async function load() {
    try {
      const [r, c] = await Promise.all([getRoleDefinitions(), getPermissionCatalog()]);
      setRoles(r);
      setCatalog(c);
      setError(null);
    } catch {
      setError("Impossible de charger les rôles.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    if (!user) return;
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [user]);

  if (userLoading || !user)
    return (
      <p className="p-10 text-center font-mono text-xs text-muted-foreground">
        vérification de la session
      </p>
    );

  async function togglePermission(role: RoleDefinition, code: string) {
    setBusyRole(role.name);
    const next = role.permissions.includes(code)
      ? role.permissions.filter((p) => p !== code)
      : [...role.permissions, code];
    try {
      const updated = await setRolePermissions(role.name, next);
      setRoles((prev) => prev.map((r) => (r.name === updated.name ? updated : r)));
      setError(null);
    } catch (err) {
      setError(err instanceof ForbiddenError ? err.message : "Modification impossible.");
    } finally {
      setBusyRole(null);
    }
  }

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    if (!newName.trim()) return;
    try {
      const created = await createRoleDefinition(newName.trim(), newDescription.trim());
      setRoles((prev) => [...prev, created].sort((a, b) => a.name.localeCompare(b.name)));
      setShowCreate(false);
      setNewName("");
      setNewDescription("");
      setError(null);
    } catch (err) {
      setError(err instanceof ForbiddenError ? err.message : "Création impossible.");
    }
  }

  async function handleDelete(role: RoleDefinition) {
    if (!window.confirm(`Retirer la définition du rôle « ${role.name} » ?`)) return;
    try {
      await deleteRoleDefinition(role.name);
      setRoles((prev) => prev.filter((r) => r.name !== role.name));
      setError(null);
    } catch (err) {
      setError(err instanceof ForbiddenError ? err.message : "Suppression impossible.");
    }
  }

  return (
    <Layout user={user}>
      <div className="mb-6 flex flex-col gap-4 border-b pb-4 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Rôles &amp; permissions
          </h1>
          <p className="text-sm text-muted-foreground">
            Ce que chaque rôle venu du SSO autorise à faire{" "}
            <em>dans cette application</em> — le serveur d'identité ne connaît
            que le nom du rôle, jamais ce qu'il permet ici.
          </p>
        </div>

        <Button className="w-full sm:w-auto" onClick={() => setShowCreate(true)}>
          <Plus />
          Nouveau rôle
        </Button>
      </div>

      {error && (
        <div className="mb-4 rounded-md border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
          {error}
        </div>
      )}

      {loading ? (
        <p className="py-10 text-center font-mono text-xs text-muted-foreground">
          chargement
        </p>
      ) : (
        <div className="space-y-4">
          {roles.map((role) => (
            <Card key={role.name}>
              <CardContent className="space-y-3">
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <div className="flex items-center gap-2">
                      <ShieldCheck className="h-4 w-4 text-muted-foreground" />
                      <span className="font-medium">{role.name}</span>
                    </div>
                    {role.description && (
                      <p className="mt-1 text-sm text-muted-foreground">
                        {role.description}
                      </p>
                    )}
                  </div>

                  {role.name !== "Admin" && (
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      onClick={() => handleDelete(role)}
                    >
                      <Trash2 />
                    </Button>
                  )}
                </div>

                <div className="flex flex-wrap gap-2">
                  {catalog.map((perm) => {
                    const active = role.permissions.includes(perm.code);
                    return (
                      <button
                        key={perm.code}
                        type="button"
                        disabled={role.name === "Admin" || busyRole === role.name}
                        onClick={() => togglePermission(role, perm.code)}
                        className="disabled:cursor-not-allowed disabled:opacity-70"
                      >
                        <Badge variant={active ? "default" : "secondary"}>
                          {perm.label}
                        </Badge>
                      </button>
                    );
                  })}
                </div>
                {role.name === "Admin" && (
                  <p className="text-xs text-muted-foreground">
                    Rôle protégé : toutes les permissions, non modifiable.
                  </p>
                )}
              </CardContent>
            </Card>
          ))}
        </div>
      )}

      <Dialog open={showCreate} onOpenChange={setShowCreate}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Nouveau rôle</DialogTitle>
          </DialogHeader>

          <form onSubmit={handleCreate} className="space-y-4">
            <div className="space-y-1.5">
              <Label htmlFor="role-name">
                Nom (doit correspondre à un rôle SSO existant ou à venir)
              </Label>
              <Input
                id="role-name"
                value={newName}
                onChange={(e) => setNewName(e.target.value)}
                placeholder="Manager"
                required
                autoFocus
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="role-description">Description</Label>
              <Input
                id="role-description"
                value={newDescription}
                onChange={(e) => setNewDescription(e.target.value)}
                placeholder="Facultatif"
              />
            </div>

            <DialogFooter>
              <Button type="button" variant="outline" onClick={() => setShowCreate(false)}>
                Annuler
              </Button>
              <Button type="submit">Créer</Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </Layout>
  );
}
