import { useState } from "react";
import { NavLink } from "react-router-dom";
import { LogOut, Menu, PanelLeftClose } from "lucide-react";
import { hasPermission, isAdmin, logout, type User } from "../api";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import ThemeToggle from "./ThemeToggle";

interface LayoutProps {
  user: User;
  children: React.ReactNode;
}

const STORAGE_KEY = "atrium-sidebar-open";
const DESKTOP_QUERY = "(min-width: 768px)";

function getInitialOpen(): boolean {
  const isDesktop = window.matchMedia(DESKTOP_QUERY).matches;
  if (!isDesktop) return false; // toujours fermée par défaut sur mobile

  const saved = localStorage.getItem(STORAGE_KEY);
  return saved === null ? true : saved === "true";
}

export default function Layout({ user, children }: LayoutProps) {
  const [open, setOpen] = useState(getInitialOpen);

  function toggleSidebar() {
    setOpen((prev) => {
      const next = !prev;
      if (window.matchMedia(DESKTOP_QUERY).matches) {
        localStorage.setItem(STORAGE_KEY, String(next));
      }
      return next;
    });
  }

  const navLinkClass = ({ isActive }: { isActive: boolean }) =>
    cn(
      "rounded-md px-3 py-2 text-sm font-medium text-muted-foreground transition-colors hover:bg-muted hover:text-foreground",
      isActive && "bg-muted text-foreground"
    );

  return (
    <div className="min-h-screen md:flex">
      {/* Barre mobile */}
      <div className="flex items-center justify-between border-b p-4 md:hidden">
        <span className="text-lg font-semibold tracking-tight">Atrium</span>
        <Button variant="outline" size="icon" onClick={toggleSidebar}>
          <Menu />
        </Button>
      </div>

      {/* Fond assombri derrière le tiroir, mobile uniquement */}
      {open && (
        <div
          className="fixed inset-0 z-40 bg-black/50 md:hidden"
          onClick={toggleSidebar}
        />
      )}

      {open && (
        <aside
          className={cn(
            "z-50 flex w-64 shrink-0 flex-col border-r bg-background p-5",
            "fixed inset-y-0 left-0 md:static"
          )}
        >
          <div className="flex items-center justify-between pb-10">
            <span className="text-lg font-semibold tracking-tight">Atrium</span>
            <Button variant="ghost" size="icon-sm" onClick={toggleSidebar}>
              <PanelLeftClose />
            </Button>
          </div>

          <nav className="flex flex-1 flex-col gap-1">
            <NavLink to="/dashboard" end className={navLinkClass}>
              Tableau de bord
            </NavLink>
            <NavLink to="/announcements" className={navLinkClass}>
              Annonces
            </NavLink>
            {(isAdmin(user) || hasPermission(user, "roles.manage")) && (
              <NavLink to="/roles" className={navLinkClass}>
                Rôles &amp; permissions
              </NavLink>
            )}
          </nav>

          <div className="flex flex-col gap-3 border-t pt-4">
            <div className="flex flex-col gap-1.5">
              <span className="truncate text-xs text-muted-foreground">
                {user.email}
              </span>
              <Badge
                variant={isAdmin(user) ? "default" : "secondary"}
                className="w-fit"
              >
                {user.roles.join(", ") || "Aucun rôle"}
              </Badge>
            </div>
            <div className="flex items-center gap-2">
              <Button
                variant="outline"
                size="sm"
                className="flex-1"
                onClick={logout}
              >
                <LogOut />
                Se déconnecter
              </Button>
              <ThemeToggle />
            </div>
          </div>
        </aside>
      )}

      <main className="relative flex-1 px-4 py-8 sm:px-6 md:px-10 md:py-10">
        {!open && (
          <Button
            variant="outline"
            size="icon"
            className="mb-6 hidden md:inline-flex"
            onClick={toggleSidebar}
          >
            <Menu />
          </Button>
        )}

        <div className="mx-auto max-w-3xl">{children}</div>
      </main>
    </div>
  );
}
