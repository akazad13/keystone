import { z } from "zod";

/** A user as the Core API knows them (GET /api/admin/users). Accounts themselves live in Zitadel. */
export const adminUserSchema = z.object({
  id: z.string(),
  email: z.string(),
  displayName: z.string(),
  role: z.string(),
  projectCount: z.number(),
  createdAt: z.string(),
  lastSeenAt: z.string(),
});

export type AdminUser = z.infer<typeof adminUserSchema>;

/** Platform numbers for the dashboard (GET /api/admin/stats). */
export const adminStatsSchema = z.object({
  totalUsers: z.number(),
  activeUsers: z.number(),
  newUsersToday: z.number(),
  admins: z.number(),
  totalProjects: z.number(),
  activeProjects: z.number(),
});

export type AdminStats = z.infer<typeof adminStatsSchema>;

/** The signed-in user as returned by the BFF's GET {prefix}/me (built from the OIDC session). */
export const sessionUserSchema = z.object({
  sub: z.string(),
  name: z.string().nullable(),
  email: z.string().nullable(),
  sessionExpiresAt: z.string().nullable(),
  claims: z.array(z.object({ type: z.string(), value: z.string() })),
});

export type SessionUser = z.infer<typeof sessionUserSchema>;
