export const API_BASE = "/api/keystone";

/**
 * Starts the OIDC login through the BFF and comes back to the current page.
 * prompt "login" makes Zitadel ask for the password even when the user has an SSO session.
 */
export function loginUrl(returnUrl: string = window.location.pathname, prompt?: "login"): string {
  const url = `${API_BASE}/auth/login?returnUrl=${encodeURIComponent(returnUrl)}`;
  return prompt ? `${url}&prompt=${prompt}` : url;
}

/** True for the BFF's step-up challenge (RFC 9470): the action needs a recent sign-in. */
export function needsReauthentication(error: unknown): boolean {
  return error instanceof ApiError && error.status === 401 && error.message.includes("insufficient_user_authentication");
}

export class ApiError extends Error {
  constructor(
    public status: number,
    message: string
  ) {
    super(message);
    this.name = "ApiError";
  }
}

export async function fetchApi<T>(
  endpoint: string,
  options: RequestInit = {}
): Promise<T> {
  const url = `${API_BASE}${endpoint}`;

  const response = await fetch(url, {
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
      // The BFF rejects state-changing calls without this header (CSRF defence).
      "X-CSRF": "1",
      ...options.headers,
    },
    ...options,
  });

  if (!response.ok) {
    const errorBody = await response.text().catch(() => "Unknown error");
    throw new ApiError(response.status, errorBody);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return response.json() as Promise<T>;
}

export const api = {
  get: <T>(endpoint: string) => fetchApi<T>(endpoint, { method: "GET" }),

  post: <T>(endpoint: string, body?: unknown) =>
    fetchApi<T>(endpoint, {
      method: "POST",
      body: body ? JSON.stringify(body) : undefined,
    }),

  put: <T>(endpoint: string, body?: unknown) =>
    fetchApi<T>(endpoint, {
      method: "PUT",
      body: body ? JSON.stringify(body) : undefined,
    }),

  patch: <T>(endpoint: string, body?: unknown) =>
    fetchApi<T>(endpoint, {
      method: "PATCH",
      body: body ? JSON.stringify(body) : undefined,
    }),

  delete: <T>(endpoint: string) => fetchApi<T>(endpoint, { method: "DELETE" }),
};
