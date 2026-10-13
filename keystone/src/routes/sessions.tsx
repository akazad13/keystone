import { createFileRoute } from "@tanstack/react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Laptop, Loader2, LogOut, MonitorSmartphone, Smartphone } from "lucide-react";
import { SignInPrompt } from "@/components/auth/SignInPrompt";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { useAuth } from "@/hooks/useAuth";
import { api, ApiError } from "@/lib/api";
import type { BffSession } from "@/lib/schemas";

export const Route = createFileRoute("/sessions")({ component: SessionsPage });

/** A rough "Chrome on Windows" label; good enough to recognise your own devices. */
function describeDevice(userAgent: string | null): { label: string; mobile: boolean } {
  if (!userAgent) return { label: "Unknown device", mobile: false };
  const browser =
    /Edg\//.test(userAgent) ? "Edge" :
    /Firefox\//.test(userAgent) ? "Firefox" :
    /Chrome\//.test(userAgent) ? "Chrome" :
    /Safari\//.test(userAgent) ? "Safari" : "Browser";
  const os =
    /Windows/.test(userAgent) ? "Windows" :
    /iPhone|iPad/.test(userAgent) ? "iOS" :
    /Android/.test(userAgent) ? "Android" :
    /Mac OS X/.test(userAgent) ? "macOS" :
    /Linux/.test(userAgent) ? "Linux" : "unknown OS";
  return { label: `${browser} on ${os}`, mobile: /Mobile|iPhone|Android/.test(userAgent) };
}

function SessionsPage() {
  const { isAuthenticated, isLoading } = useAuth();
  const queryClient = useQueryClient();

  // Answered by the BFF itself, from the user's sessions in Redis; the Core API is not involved.
  const sessions = useQuery<BffSession[], ApiError>({
    queryKey: ["sessions"],
    queryFn: () => api.get<BffSession[]>("/sessions"),
    enabled: isAuthenticated,
  });

  // Ending this browser's session (or all of them) signs us out: refresh "who am I" so the page reacts.
  const afterRevoke = () => queryClient.invalidateQueries();

  const revoke = useMutation({
    mutationFn: (id: string) => api.post(`/sessions/${id}/revoke`),
    onSuccess: afterRevoke,
  });

  const revokeAll = useMutation({
    mutationFn: () => api.post<{ removed: number }>("/sessions/revoke-all"),
    onSuccess: afterRevoke,
  });

  if (isLoading) {
    return <Loader2 className="mx-auto mt-12 h-8 w-8 animate-spin text-slate-400" />;
  }

  if (!isAuthenticated) {
    return <SignInPrompt message="Sign in to see where you are signed in." />;
  }

  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-bold text-slate-900">
            <MonitorSmartphone className="h-6 w-6 text-primary-600" />
            Your sessions
          </h1>
          <p className="mt-1 max-w-xl text-sm text-slate-500">
            Every browser where you are signed in to Keystone. Ending a session signs that browser out of
            Keystone; its Zitadel sign-in stays, so signing in again there needs no password.
          </p>
        </div>
        <Button
          variant="outline"
          disabled={revokeAll.isPending}
          onClick={() => confirm("Sign out of Keystone everywhere, including this browser?") && revokeAll.mutate()}
        >
          <LogOut className="mr-1 h-4 w-4" />
          Sign out everywhere
        </Button>
      </div>

      {sessions.isLoading && <Loader2 className="mx-auto h-6 w-6 animate-spin text-slate-400" />}
      {sessions.isError && <p className="text-sm text-red-600">Could not load sessions ({sessions.error.status}).</p>}

      <div className="space-y-3">
        {sessions.data?.map((session) => {
          const device = describeDevice(session.userAgent);
          const DeviceIcon = device.mobile ? Smartphone : Laptop;
          return (
            <Card key={session.id}>
              <CardContent className="flex items-center justify-between gap-4 p-4">
                <div className="flex items-center gap-3">
                  <DeviceIcon className="h-8 w-8 text-slate-400" />
                  <div>
                    <p className="flex items-center gap-2 text-sm font-medium text-slate-900">
                      {device.label}
                      {session.current && (
                        <span className="rounded-full bg-emerald-100 px-2 py-0.5 text-xs font-medium text-emerald-700">
                          This browser
                        </span>
                      )}
                    </p>
                    <p className="text-xs text-slate-500">
                      Signed in {session.signedInAt ? new Date(session.signedInAt).toLocaleString() : "at an unknown time"}
                      {session.expiresAt && ` · expires ${new Date(session.expiresAt).toLocaleString()}`}
                    </p>
                    <p className="font-mono text-[11px] text-slate-400">session {session.id}</p>
                  </div>
                </div>
                <Button
                  size="sm"
                  variant="ghost"
                  className="text-red-600 hover:text-red-700"
                  disabled={revoke.isPending}
                  onClick={() => revoke.mutate(session.id)}
                >
                  {session.current ? "Sign out" : "End session"}
                </Button>
              </CardContent>
            </Card>
          );
        })}
      </div>
    </div>
  );
}
