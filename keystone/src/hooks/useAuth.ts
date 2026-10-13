import { useQuery } from "@tanstack/react-query";
import { api, ApiError, API_BASE, loginUrl } from "@/lib/api";
import type { SessionUser } from "@/lib/schemas";

/**
 * Who is signed in, according to the BFF session. The browser never sees a token:
 * a 401 from /me simply means "no session".
 */
export function useAuth() {
  const {
    data: user,
    isLoading,
    isError,
    error,
  } = useQuery<SessionUser | null, ApiError>({
    queryKey: ["auth", "me"],
    // 401 means "no session": return null instead of failing. A failed query would keep showing the
    // last successful user, so a session ended elsewhere (back-channel logout, "sign out everywhere")
    // would go unnoticed.
    queryFn: async () => {
      try {
        return await api.get<SessionUser>("/me");
      } catch (error) {
        if (error instanceof ApiError && error.status === 401) return null;
        throw error;
      }
    },
    retry: false,
    // Re-check when the tab regains focus, so a session ended elsewhere
    // (e.g. by a back-channel logout) shows up without a reload.
    staleTime: 0,
    refetchOnWindowFocus: true,
  });

  return {
    user: user ?? null,
    isAuthenticated: !!user,
    isLoading,
    isError,
    error,
    loginUrl: loginUrl(),
    // POST only: a plain link would let any site log the user out.
    logoutUrl: `${API_BASE}/auth/logout`,
  };
}
