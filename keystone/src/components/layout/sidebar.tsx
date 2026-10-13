import { Link, useRouterState } from "@tanstack/react-router";
import {
  Fingerprint,
  FolderOpen,
  Home,
  LayoutDashboard,
  Rocket,
  Settings,
  Users,
} from "lucide-react";
import { cn } from "@/lib/utils";

const navItems = [
  { to: "/", label: "Home", icon: Home },
  { to: "/dashboard", label: "Dashboard", icon: LayoutDashboard },
  { to: "/projects", label: "Projects", icon: FolderOpen },
  { to: "/dashboard", label: "Deployments", icon: Rocket },
  { to: "/dashboard", label: "Team", icon: Users },
  { to: "/dashboard", label: "Settings", icon: Settings },
  { to: "/me", label: "Who am I", icon: Fingerprint },
] as const;

export function Sidebar() {
  const routerState = useRouterState();
  const currentPath = routerState.location.pathname;

  return (
    <aside className="hidden w-56 shrink-0 border-r border-border bg-surface md:block">
      <nav className="flex flex-col gap-1 p-3">
        <p className="mb-2 px-3 text-xs font-semibold uppercase tracking-wider text-slate-400">
          Navigation
        </p>
        {navItems.map((item) => {
          const isActive = currentPath === item.to;
          return (
            <Link
              key={item.label}
              to={item.to}
              className={cn(
                "flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition-colors",
                isActive
                  ? "bg-primary-50 text-primary-700"
                  : "text-slate-600 hover:bg-surface-hover hover:text-slate-900"
              )}
            >
              <item.icon className="h-4 w-4 shrink-0" />
              {item.label}
            </Link>
          );
        })}
      </nav>

      {/* Bottom section */}
      <div className="mt-auto border-t border-border p-4">
        <div className="rounded-lg bg-primary-50 p-3">
          <p className="text-xs font-semibold text-primary-800">Pro Plan</p>
          <p className="mt-0.5 text-xs text-primary-600">
            24 of 50 projects used
          </p>
          <div className="mt-2 h-1.5 rounded-full bg-primary-200">
            <div
              className="h-1.5 rounded-full bg-primary-600"
              style={{ width: "48%" }}
            />
          </div>
        </div>
      </div>
    </aside>
  );
}
