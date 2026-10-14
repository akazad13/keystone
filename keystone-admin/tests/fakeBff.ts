import { vi } from "vitest";

/** One request the app sent, as the BFF would have received it. */
export interface RecordedRequest {
  method: string;
  path: string;
  headers: Headers;
  credentials: RequestCredentials | undefined;
  body: unknown;
}

type Reply = { status: number; body?: unknown };
type Responder = Reply | ((request: RecordedRequest) => Reply);

/**
 * Stands in for the BFF: replaces global fetch, answers with canned replies per "METHOD /path",
 * and records every request. A request nobody set up gets a 404, so unexpected calls show up in
 * assertions instead of hanging.
 */
export class FakeBff {
  readonly requests: RecordedRequest[] = [];
  private readonly replies = new Map<string, Responder>();

  constructor() {
    vi.stubGlobal("fetch", (input: RequestInfo | URL, init: RequestInit = {}) => this.handle(input, init));
  }

  /** Answers METHOD path (exact match, query string included) with a reply, or a function of the request. */
  on(method: string, path: string, reply: Responder): this {
    this.replies.set(`${method} ${path}`, reply);
    return this;
  }

  /** The browser has a session for this user. */
  signedIn(user: Partial<SessionUserReply> = {}): this {
    return this.on("GET", "/api/keystone-admin/me", {
      status: 200,
      body: { sub: "alice-sub", name: "Alice Example", email: "alice@example.com", sessionExpiresAt: null, claims: [], ...user },
    });
  }

  /** The browser has no session. */
  signedOut(): this {
    return this.on("GET", "/api/keystone-admin/me", { status: 401 });
  }

  requestsTo(method: string, path: string): RecordedRequest[] {
    return this.requests.filter((r) => r.method === method && r.path === path);
  }

  private async handle(input: RequestInfo | URL, init: RequestInit): Promise<Response> {
    const path = typeof input === "string" ? input : input instanceof URL ? input.pathname + input.search : input.url;
    const method = (init.method ?? "GET").toUpperCase();
    const request: RecordedRequest = {
      method,
      path,
      headers: new Headers(init.headers),
      credentials: init.credentials,
      body: typeof init.body === "string" ? JSON.parse(init.body) : init.body,
    };
    this.requests.push(request);

    const responder = this.replies.get(`${method} ${path}`) ?? { status: 404 };
    const reply = typeof responder === "function" ? responder(request) : responder;
    const hasBody = reply.body !== undefined && reply.status !== 204;
    const response = new Response(hasBody ? JSON.stringify(reply.body) : null, {
      status: reply.status,
      headers: hasBody ? { "Content-Type": "application/json" } : {},
    });
    Object.defineProperty(response, "url", { value: `http://localhost${path}` });
    return response;
  }
}

interface SessionUserReply {
  sub: string;
  name: string | null;
  email: string | null;
  sessionExpiresAt: string | null;
  claims: { type: string; value: string }[];
}
