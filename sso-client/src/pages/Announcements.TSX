import { useEffect, useState } from "react";
import { Pencil, Trash2 } from "lucide-react";
import Layout from "../components/Layout";
import { useCurrentUser } from "../hooks/useCurrentUser";
import {
  createAnnouncement,
  deleteAnnouncement,
  getAnnouncements,
  isAdmin,
  updateAnnouncement,
  ForbiddenError,
  type Announcement,
  type AnnouncementInput,
} from "../api";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";

const EMPTY_FORM: AnnouncementInput = { title: "", content: "" };

export default function Announcements() {
  const { user, loading: userLoading } = useCurrentUser();
  const [items, setItems] = useState<Announcement[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<Announcement | null>(null);
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState<AnnouncementInput>(EMPTY_FORM);

  useEffect(() => {
    if (!user) return;
    getAnnouncements()
      .then(setItems)
      .finally(() => setLoading(false));
  }, [user]);

  if (userLoading || !user)
    return (
      <p className="p-10 text-center font-mono text-xs text-muted-foreground">
        vérification de la session
      </p>
    );

  const admin = isAdmin(user);

  function openCreateForm() {
    setEditing(null);
    setForm(EMPTY_FORM);
    setShowForm(true);
    setError(null);
  }

  function openEditForm(item: Announcement) {
    setEditing(item);
    setForm({ title: item.title, content: item.content });
    setShowForm(true);
    setError(null);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    try {
      if (editing) {
        const updated = await updateAnnouncement(editing.id, form);
        setItems((prev) => prev.map((a) => (a.id === updated.id ? updated : a)));
      } else {
        const created = await createAnnouncement(form);
        setItems((prev) => [created, ...prev]);
      }
      setShowForm(false);
    } catch (err) {
      setError(err instanceof ForbiddenError ? err.message : "Une erreur est survenue.");
    }
  }

  async function handleDelete(id: number) {
    try {
      await deleteAnnouncement(id);
      setItems((prev) => prev.filter((a) => a.id !== id));
    } catch (err) {
      setError(err instanceof ForbiddenError ? err.message : "Une erreur est survenue.");
    }
  }

  return (
    <Layout user={user}>
      <div className="mb-6 flex flex-col gap-4 border-b pb-4 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Annonces</h1>
          <p className="text-sm text-muted-foreground">
            Communications internes de l'entreprise.
          </p>
        </div>

        {admin && (
          <Button className="w-full sm:w-auto" onClick={openCreateForm}>
            Nouvelle annonce
          </Button>
        )}
      </div>

      {error && (
        <div className="mb-4 rounded-md border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
          {error}
        </div>
      )}

      {loading ? (
        <p className="py-10 text-center font-mono text-xs text-muted-foreground">
          chargement des annonces
        </p>
      ) : items.length === 0 ? (
        <p className="py-10 text-center text-sm text-muted-foreground">
          Aucune annonce pour le moment.
        </p>
      ) : (
        <div className="space-y-3">
          {items.map((item) => (
            <Card key={item.id}>
              <CardContent className="flex items-start justify-between gap-4">
                <div className="min-w-0 space-y-1.5">
                  <h3 className="break-words font-medium leading-snug">{item.title}</h3>
                  <p className="break-words text-sm text-muted-foreground">
                    {item.content}
                  </p>
                  <p className="break-words font-mono text-xs text-muted-foreground/70">
                    {item.author} · {new Date(item.createdAt).toLocaleString("fr-FR")}
                  </p>
                </div>

                {admin && (
                  <div className="flex shrink-0 gap-1">
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      onClick={() => openEditForm(item)}
                    >
                      <Pencil />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      onClick={() => handleDelete(item.id)}
                    >
                      <Trash2 />
                    </Button>
                  </div>
                )}
              </CardContent>
            </Card>
          ))}
        </div>
      )}

      <Dialog open={showForm} onOpenChange={setShowForm}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>
              {editing ? "Modifier l'annonce" : "Nouvelle annonce"}
            </DialogTitle>
          </DialogHeader>

          <form onSubmit={handleSubmit} className="space-y-4">
            <div className="space-y-1.5">
              <Label htmlFor="title">Titre</Label>
              <Input
                id="title"
                value={form.title}
                onChange={(e) => setForm({ ...form, title: e.target.value })}
                required
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="content">Contenu</Label>
              <Textarea
                id="content"
                rows={4}
                value={form.content}
                onChange={(e) => setForm({ ...form, content: e.target.value })}
                required
              />
            </div>

            <DialogFooter>
              <Button type="button" variant="outline" onClick={() => setShowForm(false)}>
                Annuler
              </Button>
              <Button type="submit">{editing ? "Enregistrer" : "Publier"}</Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </Layout>
  );
}
