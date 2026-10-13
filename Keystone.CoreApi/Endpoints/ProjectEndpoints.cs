using FluentValidation;
using Keystone.CoreApi.Services;
using Keystone.CoreApi.Validators;

namespace Keystone.CoreApi.Endpoints;

/// <summary>
/// The caller's own projects. The owner is always the authenticated user (resolved by
/// <see cref="ICurrentUserService"/>); another user's project id behaves exactly like an
/// unknown id (404), so ids cannot be probed.
/// </summary>
public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/projects")
            .WithTags("Projects")
            .RequireAuthorization("User");

        group.MapGet("/", async (ICurrentUserService currentUser, IProjectService service, CancellationToken ct) =>
        {
            var user = await currentUser.GetOrCreateAsync(ct);
            return Results.Ok(await service.GetForOwnerAsync(user.Id, ct));
        })
        .WithName("GetMyProjects")
        .Produces<IReadOnlyList<ProjectDto>>();

        group.MapGet("/{id:guid}", async (Guid id, ICurrentUserService currentUser, IProjectService service, CancellationToken ct) =>
        {
            var user = await currentUser.GetOrCreateAsync(ct);
            var project = await service.GetByIdAsync(id, user.Id, ct);
            return project is not null ? Results.Ok(project) : Results.NotFound();
        })
        .WithName("GetProjectById")
        .Produces<ProjectDto>()
        .Produces(404);

        group.MapPost("/", async (
            CreateProjectRequest request,
            IValidator<CreateProjectRequest> validator,
            ICurrentUserService currentUser,
            IProjectService service,
            CancellationToken ct) =>
        {
            var validationResult = await validator.ValidateAsync(request, ct);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            var user = await currentUser.GetOrCreateAsync(ct);
            var project = await service.CreateAsync(user.Id, request.Name, request.Description, request.DueDate, ct);
            return Results.Created($"/api/projects/{project.Id}", project);
        })
        .WithName("CreateProject")
        .Produces<ProjectDto>(201)
        .Produces(400);

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateProjectRequest request,
            IValidator<UpdateProjectRequest> validator,
            ICurrentUserService currentUser,
            IProjectService service,
            CancellationToken ct) =>
        {
            var validationResult = await validator.ValidateAsync(request, ct);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            var user = await currentUser.GetOrCreateAsync(ct);
            var project = await service.UpdateAsync(id, user.Id, request.Name, request.Description, request.Status, request.DueDate, ct);
            return project is not null ? Results.Ok(project) : Results.NotFound();
        })
        .WithName("UpdateProject")
        .Produces<ProjectDto>()
        .Produces(400)
        .Produces(404);

        group.MapDelete("/{id:guid}", async (Guid id, ICurrentUserService currentUser, IProjectService service, CancellationToken ct) =>
        {
            var user = await currentUser.GetOrCreateAsync(ct);
            var deleted = await service.DeleteAsync(id, user.Id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteProject")
        .Produces(204)
        .Produces(404);

        return routes;
    }
}
