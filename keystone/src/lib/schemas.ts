import { z } from "zod";

export const userSchema = z.object({
  id: z.string(),
  email: z.string().email(),
  displayName: z.string(),
  avatarUrl: z.string().url().nullable().optional(),
  role: z.enum(["admin", "member", "viewer"]),
  createdAt: z.string().datetime(),
});

export type User = z.infer<typeof userSchema>;

/** A project as returned by the Core API (ProjectDto). The owner is implicit: always the caller. */
export const projectSchema = z.object({
  id: z.string(),
  name: z.string().min(1),
  description: z.string().nullable(),
  status: z.enum(["Active", "Archived", "Completed"]),
  dueDate: z.string().nullable(),
  createdAt: z.string(),
  updatedAt: z.string().nullable(),
});

export type Project = z.infer<typeof projectSchema>;

export const projectListSchema = z.object({
  items: z.array(projectSchema),
  total: z.number().int().nonnegative(),
  page: z.number().int().positive(),
  pageSize: z.number().int().positive(),
});

export type ProjectList = z.infer<typeof projectListSchema>;

export const activitySchema = z.object({
  id: z.string(),
  type: z.enum(["created", "updated", "deleted", "commented", "deployed"]),
  description: z.string(),
  userId: z.string(),
  userName: z.string(),
  projectId: z.string().optional(),
  projectName: z.string().optional(),
  timestamp: z.string().datetime(),
});

export type Activity = z.infer<typeof activitySchema>;

export const metricsSchema = z.object({
  totalProjects: z.number(),
  activeProjects: z.number(),
  totalDeployments: z.number(),
  uptimePercentage: z.number(),
  monthlyUsage: z.array(
    z.object({
      month: z.string(),
      requests: z.number(),
      deployments: z.number(),
    })
  ),
});

export type Metrics = z.infer<typeof metricsSchema>;

// Mirrors the Core API's CreateProjectValidator, so most mistakes are caught before a round trip.
export const createProjectSchema = z.object({
  name: z.string().trim().min(1, "Project name is required").max(200),
  description: z.string().max(2000).optional(),
  dueDate: z.string().optional(),
});

export type CreateProjectInput = z.infer<typeof createProjectSchema>;

/** The signed-in user as returned by the BFF's GET {prefix}/me (built from the OIDC session). */
export const sessionUserSchema = z.object({
  sub: z.string(),
  name: z.string().nullable(),
  email: z.string().nullable(),
  sessionExpiresAt: z.string().nullable(),
  claims: z.array(z.object({ type: z.string(), value: z.string() })),
});

export type SessionUser = z.infer<typeof sessionUserSchema>;
