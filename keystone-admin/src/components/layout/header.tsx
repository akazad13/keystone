import { Shield, LogOut, Bell } from "lucide-react";
import { Button } from "@/components/ui/button";
import { useAuth } from "@/hooks/useAuth";

export function AdminHeader() {
  const { user, logoutUrl } = useAuth();

  return (
    <header className="sticky top-0 z-30 flex h-16 items-center justify-between border-b border-admin-700 bg-admin-900/95 px-6 backdrop-blur supports-[backdrop-filter]:bg-admin-900/80">
      <div className="flex items-center gap-3">
        <Shield className="h-6 w-6 text-accent-400" />
        <h1 className="text-lg font-bold text-white">Admin Dashboard</h1>
      </div>

      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" className="relative">
          <Bell className="h-5 w-5" />
          <span className="absolute -top-1 -right-1 flex h-4 w-4 items-center justify-center rounded-full bg-danger text-[10px] font-bold text-white">
            3
          </span>
        </Button>

        {user && (
          <div className="flex items-center gap-3">
            <div className="text-right">
              <p className="text-sm font-medium text-white">{user.name ?? user.sub}</p>
              <p className="text-xs text-admin-400">{user.email}</p>
            </div>
            <div className="flex h-9 w-9 items-center justify-center rounded-full bg-accent-600 text-sm font-bold text-white">
              {(user.name ?? user.sub).charAt(0).toUpperCase()}
            </div>
          </div>
        )}

        <form method="post" action={logoutUrl}>
          <Button type="submit" variant="ghost" size="sm" className="text-admin-400 hover:text-white">
            <LogOut className="mr-1 h-4 w-4" />
            Logout
          </Button>
        </form>
      </div>
    </header>
  );
}
