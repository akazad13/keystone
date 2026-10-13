import { z } from "zod";

export const userSchema = z.object({
  id: z.string(),
  email: z.string().email(),
  displayName: z.string(),
  role: z.enum(["admin", "manager", "user"]),
  status: z.enum(["active", "inactive", "suspended"]),
  createdAt: z.string(),
  lastLogin: z.string().nullable(),
});

export type User = z.infer<typeof userSchema>;

export const userListResponseSchema = z.object({
  users: z.array(userSchema),
  total: z.number(),
  page: z.number(),
  pageSize: z.number(),
});

export type UserListResponse = z.infer<typeof userListResponseSchema>;

export const createUserSchema = z.object({
  email: z.string().email("Invalid email address"),
  displayName: z.string().min(2, "Name must be at least 2 characters"),
  role: z.enum(["admin", "manager", "user"]),
});

export type CreateUserInput = z.infer<typeof createUserSchema>;

export const updateUserSchema = z.object({
  displayName: z.string().min(2, "Name must be at least 2 characters").optional(),
  role: z.enum(["admin", "manager", "user"]).optional(),
  status: z.enum(["active", "inactive", "suspended"]).optional(),
});

export type UpdateUserInput = z.infer<typeof updateUserSchema>;

/** The signed-in user as returned by the BFF's GET {prefix}/me (built from the OIDC session). */
export const sessionUserSchema = z.object({
  sub: z.string(),
  name: z.string().nullable(),
  email: z.string().nullable(),
  sessionExpiresAt: z.string().nullable(),
  claims: z.array(z.object({ type: z.string(), value: z.string() })),
});

export type SessionUser = z.infer<typeof sessionUserSchema>;

export const dashboardStatsSchema = z.object({
  totalUsers: z.number(),
  activeUsers: z.number(),
  newUsersToday: z.number(),
  totalSessions: z.number(),
});

export type DashboardStats = z.infer<typeof dashboardStatsSchema>;
