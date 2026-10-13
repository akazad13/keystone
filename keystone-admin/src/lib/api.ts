export const API_BASE = "/api/keystone-admin";

/** Starts the OIDC login through the BFF and comes back to the current page. */
export function loginUrl(returnUrl: string = window.location.pathname): string {
  return `${API_BASE}/auth/login?returnUrl=${encodeURIComponent(returnUrl)}`;
}

// Every API call carries this header; the BFF rejects state-changing calls without it (CSRF defence).
const CSRF_HEADER = { "X-CSRF": "1" };

export class ApiError extends Error {
  constructor(
    public status: number,
    message: string
  ) {
    super(message);
    this.name = "ApiError";
  }
}

async function handleResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    // No session (expired, logged out, or ended by a back-channel logout): sign in again.
    // /me is exempt so the app can show its own "sign in" screen.
    if (response.status === 401 && !response.url.endsWith(`${API_BASE}/me`)) {
      window.location.href = loginUrl();
      throw new ApiError(401, "Unauthorized");
    }
    const body = await response.text();
    throw new ApiError(response.status, body || response.statusText);
  }
  return response.json();
}

export const api = {
  get<T>(path: string): Promise<T> {
    return fetch(`${API_BASE}${path}`, {
      credentials: "include",
      headers: { Accept: "application/json", ...CSRF_HEADER },
    }).then(handleResponse<T>);
  },

  post<T>(path: string, body?: unknown): Promise<T> {
    return fetch(`${API_BASE}${path}`, {
      method: "POST",
      credentials: "include",
      headers: {
        "Content-Type": "application/json",
        Accept: "application/json",
        ...CSRF_HEADER,
      },
      body: body ? JSON.stringify(body) : undefined,
    }).then(handleResponse<T>);
  },

  put<T>(path: string, body?: unknown): Promise<T> {
    return fetch(`${API_BASE}${path}`, {
      method: "PUT",
      credentials: "include",
      headers: {
        "Content-Type": "application/json",
        Accept: "application/json",
        ...CSRF_HEADER,
      },
      body: body ? JSON.stringify(body) : undefined,
    }).then(handleResponse<T>);
  },

  delete<T>(path: string): Promise<T> {
    return fetch(`${API_BASE}${path}`, {
      method: "DELETE",
      credentials: "include",
      headers: { Accept: "application/json", ...CSRF_HEADER },
    }).then(handleResponse<T>);
  },
};
