import { Link, useRouterState } from "@tanstack/react-router";
import {
  LayoutDashboard,
  Users,
  Settings,
  BarChart3,
  ShieldCheck,
  Database,
} from "lucide-react";
import { cn } from "@/lib/utils";

const navItems = [
  { to: "/", label: "Dashboard", icon: LayoutDashboard },
  { to: "/users", label: "Users", icon: Users },
  { to: "/", label: "Analytics", icon: BarChart3 },
  { to: "/", label: "Permissions", icon: ShieldCheck },
  { to: "/", label: "Database", icon: Database },
  { to: "/", label: "Settings", icon: Settings },
];

export function AdminSidebar() {
  const routerState = useRouterState();
  const currentPath = routerState.location.pathname;

  return (
    <aside className="fixed inset-y-0 left-0 z-40 flex w-64 flex-col border-r border-admin-700 bg-admin-900 pt-16">
      <nav className="flex-1 space-y-1 px-3 py-4">
        {navItems.map((item) => {
          const Icon = item.icon;
          const isActive = currentPath === item.to;

          return (
            <Link
              key={item.label}
              to={item.to}
              className={cn(
                "flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-medium transition-colors",
                isActive
                  ? "bg-accent-600/20 text-accent-400"
                  : "text-admin-400 hover:bg-admin-800 hover:text-white"
              )}
            >
              <Icon className="h-5 w-5 shrink-0" />
              {item.label}
            </Link>
          );
        })}
      </nav>

      <div className="border-t border-admin-700 p-4">
        <div className="rounded-lg bg-admin-800 p-3">
          <p className="text-xs font-medium text-admin-400">System Status</p>
          <div className="mt-2 flex items-center gap-2">
            <span className="h-2 w-2 rounded-full bg-success" />
            <span className="text-xs text-admin-300">All systems operational</span>
          </div>
        </div>
      </div>
    </aside>
  );
}
