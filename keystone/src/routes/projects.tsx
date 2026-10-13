import { createFileRoute } from "@tanstack/react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { CalendarDays, CheckCircle2, FolderOpen, Loader2, Plus, RotateCcw, Trash2 } from "lucide-react";
import { SignInPrompt } from "@/components/auth/SignInPrompt";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { useAuth } from "@/hooks/useAuth";
import { api, ApiError } from "@/lib/api";
import { createProjectSchema, type CreateProjectInput, type Project } from "@/lib/schemas";
import { cn } from "@/lib/utils";

export const Route = createFileRoute("/projects")({ component: ProjectsPage });

const PROJECTS_KEY = ["projects"] as const;

const STATUS_STYLES: Record<Project["status"], string> = {
  Active: "bg-blue-100 text-blue-700",
  Completed: "bg-emerald-100 text-emerald-700",
  Archived: "bg-slate-100 text-slate-600",
};

/** Turns an ASP.NET Core ValidationProblem (or any error body) into one readable line. */
function describeError(error: unknown): string {
  if (!(error instanceof ApiError)) return "Something went wrong.";
  try {
    const problem = JSON.parse(error.message) as { title?: string; errors?: Record<string, string[]> };
    if (problem.errors) return Object.values(problem.errors).flat().join(" ");
    if (problem.title) return problem.title;
  } catch {
    // Not JSON: fall through to the status code.
  }
  return `Request failed (${error.status}).`;
}

function ProjectsPage() {
  const { isAuthenticated, isLoading } = useAuth();
  const queryClient = useQueryClient();

  // GET /api/keystone/projects -> BFF -> GET /api/projects on the Core API: only the caller's projects.
  const projects = useQuery<Project[], ApiError>({
    queryKey: PROJECTS_KEY,
    queryFn: () => api.get<Project[]>("/projects"),
    enabled: isAuthenticated,
  });

  // After any change, refetch the list instead of patching it by hand.
  const refresh = () => queryClient.invalidateQueries({ queryKey: PROJECTS_KEY });

  const update = useMutation({
    mutationFn: (project: Project & { status: Project["status"] }) =>
      api.put<Project>(`/projects/${project.id}`, {
        name: project.name,
        description: project.description,
        status: project.status,
        dueDate: project.dueDate,
      }),
    onSuccess: refresh,
  });

  const remove = useMutation({
    mutationFn: (id: string) => api.delete(`/projects/${id}`),
    onSuccess: refresh,
  });

  if (isLoading) {
    return <Loader2 className="mx-auto mt-12 h-8 w-8 animate-spin text-slate-400" />;
  }

  if (!isAuthenticated) {
    return <SignInPrompt message="Sign in to see your projects." />;
  }

  return (
    <div className="mx-auto max-w-5xl space-y-6">
      <div>
        <h1 className="flex items-center gap-2 text-2xl font-bold text-slate-900">
          <FolderOpen className="h-6 w-6 text-primary-600" />
          My projects
        </h1>
        <p className="mt-1 text-sm text-slate-500">
          Only you can see these. The owner comes from your access token, never from the request.
        </p>
      </div>

      <CreateProjectForm onCreated={refresh} />

      {projects.isLoading && <Loader2 className="mx-auto h-6 w-6 animate-spin text-slate-400" />}

      {projects.isError && <p className="text-sm text-red-600">{describeError(projects.error)}</p>}

      {projects.data?.length === 0 && (
        <p className="rounded-lg border border-dashed border-border p-8 text-center text-sm text-slate-500">
          No projects yet. Create your first one above.
        </p>
      )}

      {(update.isError || remove.isError) && (
        <p className="text-sm text-red-600">{describeError(update.error ?? remove.error)}</p>
      )}

      <div className="grid gap-4 sm:grid-cols-2">
        {projects.data?.map((project) => (
          <Card key={project.id} className={cn(project.status === "Archived" && "opacity-60")}>
            <CardHeader>
              <div className="flex items-start justify-between gap-3">
                <CardTitle className="text-base">{project.name}</CardTitle>
                <span className={cn("rounded-full px-2 py-0.5 text-xs font-medium", STATUS_STYLES[project.status])}>
                  {project.status}
                </span>
              </div>
            </CardHeader>
            <CardContent className="space-y-3">
              {project.description && <p className="text-sm text-slate-600">{project.description}</p>}
              <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-slate-500">
                {project.dueDate && (
                  <span className="flex items-center gap-1">
                    <CalendarDays className="h-3.5 w-3.5" />
                    Due {new Date(`${project.dueDate}T00:00:00`).toLocaleDateString()}
                  </span>
                )}
                <span>Created {new Date(project.createdAt).toLocaleDateString()}</span>
              </div>
              <div className="flex gap-2">
                {project.status === "Active" ? (
                  <Button size="sm" variant="ghost" onClick={() => update.mutate({ ...project, status: "Completed" })}>
                    <CheckCircle2 className="mr-1 h-4 w-4" />
                    Complete
                  </Button>
                ) : (
                  <Button size="sm" variant="ghost" onClick={() => update.mutate({ ...project, status: "Active" })}>
                    <RotateCcw className="mr-1 h-4 w-4" />
                    Reopen
                  </Button>
                )}
                <Button
                  size="sm"
                  variant="ghost"
                  className="text-red-600 hover:text-red-700"
                  onClick={() => confirm(`Delete "${project.name}"?`) && remove.mutate(project.id)}
                >
                  <Trash2 className="mr-1 h-4 w-4" />
                  Delete
                </Button>
              </div>
            </CardContent>
          </Card>
        ))}
      </div>
    </div>
  );
}

function CreateProjectForm({ onCreated }: { onCreated: () => void }) {
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<CreateProjectInput>({ resolver: zodResolver(createProjectSchema) });

  const create = useMutation({
    // api.post sends X-CSRF: 1; without it the BFF would reject the request with 400.
    mutationFn: (input: CreateProjectInput) =>
      api.post<Project>("/projects", {
        name: input.name,
        description: input.description || null,
        dueDate: input.dueDate || null,
      }),
    onSuccess: () => {
      reset();
      onCreated();
    },
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">New project</CardTitle>
      </CardHeader>
      <CardContent>
        <form onSubmit={handleSubmit((input) => create.mutate(input))} className="grid gap-4 sm:grid-cols-[2fr_1fr]">
          <Input label="Name" placeholder="Website relaunch" error={errors.name?.message} {...register("name")} />
          <Input label="Due date" type="date" error={errors.dueDate?.message} {...register("dueDate")} />
          <div className="space-y-1.5 sm:col-span-2">
            <label htmlFor="description" className="block text-sm font-medium text-slate-700">
              Description
            </label>
            <textarea
              id="description"
              rows={2}
              placeholder="What is this project about?"
              className="w-full rounded-lg border border-border bg-surface px-3 py-2 text-sm shadow-sm placeholder:text-slate-400 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary-500"
              {...register("description")}
            />
            {errors.description && <p className="text-xs text-red-600">{errors.description.message}</p>}
          </div>
          <div className="flex items-center gap-3 sm:col-span-2">
            <Button type="submit" disabled={create.isPending}>
              {create.isPending ? <Loader2 className="mr-1 h-4 w-4 animate-spin" /> : <Plus className="mr-1 h-4 w-4" />}
              Create project
            </Button>
            {create.isError && <p className="text-sm text-red-600">{describeError(create.error)}</p>}
          </div>
        </form>
      </CardContent>
    </Card>
  );
}
