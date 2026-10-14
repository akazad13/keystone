import type { ReactNode } from "react";
import { render, renderHook } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  createMemoryHistory,
  createRootRoute,
  createRouter,
  RouterProvider,
  type RouteComponent,
} from "@tanstack/react-router";

/** A fresh query client per test, without retries so errors show up immediately. */
export function newQueryClient() {
  return new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
}

/**
 * Renders a page or component inside a router (for Link) and a query client, as main.tsx does.
 * Use findBy* queries: the router renders asynchronously.
 */
export function renderWithApp(Component: RouteComponent, { path = "/", queryClient = newQueryClient() } = {}) {
  const router = createRouter({
    routeTree: createRootRoute({ component: Component }),
    history: createMemoryHistory({ initialEntries: [path] }),
  });

  return {
    queryClient,
    ...render(
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    ),
  };
}

export function renderHookWithQueries<T>(hook: () => T, queryClient = newQueryClient()) {
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
  return { queryClient, ...renderHook(hook, { wrapper }) };
}
