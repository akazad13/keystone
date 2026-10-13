import { createRootRouteWithContext, Outlet } from "@tanstack/react-router";
import { TanStackRouterDevtools } from "@tanstack/router-devtools";
import type { QueryClient } from "@tanstack/react-query";
import { AdminHeader } from "@/components/layout/header";
import { AdminSidebar } from "@/components/layout/sidebar";
import { useAuth } from "@/hooks/useAuth";
import { Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";

interface RouterContext {
  queryClient: QueryClient;
}

export const Route = createRootRouteWithContext<RouterContext>()({
  component: RootLayout,
});

function RootLayout() {
  const { isLoading, isAuthenticated, loginUrl } = useAuth();

  if (isLoading) {
    return (
      <div className="flex h-screen items-center justify-center bg-admin-900">
        <div className="flex flex-col items-center gap-4">
          <Loader2 className="h-10 w-10 animate-spin text-accent-400" />
          <p className="text-sm text-admin-400">Loading admin panel...</p>
        </div>
      </div>
    );
  }

  if (!isAuthenticated) {
    return (
      <div className="flex h-screen items-center justify-center bg-admin-900">
        <div className="flex flex-col items-center gap-6 rounded-xl border border-admin-700 bg-admin-800 p-10 shadow-2xl">
          <div className="flex h-16 w-16 items-center justify-center rounded-full bg-accent-600/20">
            <svg
              className="h-8 w-8 text-accent-400"
              fill="none"
              viewBox="0 0 24 24"
              stroke="currentColor"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z"
              />
            </svg>
          </div>
          <div className="text-center">
            <h2 className="text-xl font-bold text-white">Admin Access Required</h2>
            <p className="mt-2 text-sm text-admin-400">
              Sign in with your administrator credentials to continue.
            </p>
          </div>
          <a href={loginUrl}>
            <Button size="lg">Sign In</Button>
          </a>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-admin-900">
      <AdminHeader />
      <AdminSidebar />
      <main className="pl-64 pt-16">
        <div className="p-6">
          <Outlet />
        </div>
      </main>
      {import.meta.env.DEV && <TanStackRouterDevtools position="bottom-right" />}
    </div>
  );
}
