import { describe, expect, it } from "vitest";
import { createProjectSchema, projectSchema } from "@/lib/schemas";

// createProjectSchema mirrors the Core API's CreateProjectValidator.
describe("createProjectSchema", () => {
  it("accepts a name on its own", () => {
    expect(createProjectSchema.safeParse({ name: "Website" }).success).toBe(true);
  });

  it.each(["", "   "])("requires a name (%j)", (name) => {
    const result = createProjectSchema.safeParse({ name });

    expect(result.success).toBe(false);
    expect(result.error?.issues[0].message).toBe("Project name is required");
  });

  it("trims the name", () => {
    expect(createProjectSchema.parse({ name: "  Website  " }).name).toBe("Website");
  });

  it("limits the name to 200 and the description to 2000 characters", () => {
    expect(createProjectSchema.safeParse({ name: "a".repeat(200), description: "d".repeat(2000) }).success).toBe(true);
    expect(createProjectSchema.safeParse({ name: "a".repeat(201) }).success).toBe(false);
    expect(createProjectSchema.safeParse({ name: "a", description: "d".repeat(2001) }).success).toBe(false);
  });
});

describe("projectSchema", () => {
  it("matches what the Core API returns", () => {
    const fromApi = {
      id: "7f1c",
      name: "Website",
      description: null,
      status: "Active",
      dueDate: "2026-12-01",
      createdAt: "2026-10-14T09:00:00Z",
      updatedAt: null,
    };

    expect(projectSchema.safeParse(fromApi).success).toBe(true);
  });

  it("rejects an unknown status", () => {
    expect(projectSchema.shape.status.safeParse("Deleted").success).toBe(false);
  });
});
