import { Link } from "@tanstack/react-router";
import { Bell, LogOut, Search, User } from "lucide-react";
import { useAuth } from "@/hooks/useAuth";
import { Button } from "@/components/ui/button";

export function Header() {
  const { user, isAuthenticated, loginUrl, logoutUrl } = useAuth();
  const displayName = user?.name ?? user?.email ?? "";

  return (
    <header className="sticky top-0 z-30 flex h-14 items-center border-b border-border bg-surface px-4 shadow-sm">
      {/* Logo / Brand */}
      <Link to="/" className="flex items-center gap-2 font-semibold">
        <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-primary-600 text-sm font-bold text-white">
          CP
        </div>
        <span className="hidden text-slate-900 sm:inline-block">
          Customer Portal
        </span>
      </Link>

      {/* Search */}
      <div className="ml-6 hidden flex-1 md:block">
        <div className="relative max-w-md">
          <Search className="absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input
            type="text"
            placeholder="Search projects, deployments..."
            className="h-8 w-full rounded-md border border-border bg-surface-muted pl-9 pr-3 text-sm text-slate-700 placeholder:text-slate-400 focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-1"
          />
        </div>
      </div>

      {/* Right Actions */}
      <div className="ml-auto flex items-center gap-1">
        <Button variant="ghost" size="icon" aria-label="Notifications">
          <Bell className="h-4 w-4" />
        </Button>

        {isAuthenticated && user ? (
          <div className="flex items-center gap-2 ml-2">
            <div className="flex h-8 w-8 items-center justify-center rounded-full bg-primary-100 text-xs font-semibold text-primary-700">
              {displayName
                .split(" ")
                .map((n) => n[0])
                .join("")
                .toUpperCase()
                .slice(0, 2)}
            </div>
            <span className="hidden text-sm font-medium text-slate-700 lg:inline-block">
              {displayName}
            </span>
            <form method="post" action={logoutUrl}>
              <Button type="submit" variant="ghost" size="icon" aria-label="Sign out">
                <LogOut className="h-4 w-4" />
              </Button>
            </form>
          </div>
        ) : (
          <div className="flex items-center gap-2 ml-2">
            <a href={loginUrl}>
              <Button variant="ghost" size="sm" aria-label="Sign in">
                <User className="mr-1 h-4 w-4" />
                Sign in
              </Button>
            </a>
          </div>
        )}
      </div>
    </header>
  );
}
