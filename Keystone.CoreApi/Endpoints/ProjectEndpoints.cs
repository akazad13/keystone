using FluentValidation;
using Keystone.CoreApi.Services;
using Keystone.CoreApi.Validators;

namespace Keystone.CoreApi.Endpoints;

public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/projects")
            .WithTags("Projects")
            .RequireAuthorization("User");

        group.MapGet("/", async (IProjectService service, CancellationToken ct) =>
        {
            var projects = await service.GetAllAsync(ct);
            return Results.Ok(projects);
        })
        .WithName("GetAllProjects")
        .Produces(200);

        group.MapGet("/{id:guid}", async (Guid id, IProjectService service, CancellationToken ct) =>
        {
            var project = await service.GetByIdAsync(id, ct);
            return project is not null ? Results.Ok(project) : Results.NotFound();
        })
        .WithName("GetProjectById")
        .Produces(200)
        .Produces(404);

        group.MapPost("/", async (
            CreateProjectRequest request,
            IValidator<CreateProjectRequest> validator,
            IProjectService service,
            CancellationToken ct) =>
        {
            var validationResult = await validator.ValidateAsync(request, ct);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            var project = await service.CreateAsync(request.Name, request.Description, request.OwnerId, ct);
            return Results.Created($"/api/projects/{project.Id}", project);
        })
        .WithName("CreateProject")
        .Produces(201)
        .Produces(400);

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateProjectRequest request,
            IValidator<UpdateProjectRequest> validator,
            IProjectService service,
            CancellationToken ct) =>
        {
            var validationResult = await validator.ValidateAsync(request, ct);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            var project = await service.UpdateAsync(id, request.Name, request.Description, request.Status, ct);
            return project is not null ? Results.Ok(project) : Results.NotFound();
        })
        .WithName("UpdateProject")
        .Produces(200)
        .Produces(400)
        .Produces(404);

        group.MapDelete("/{id:guid}", async (Guid id, IProjectService service, CancellationToken ct) =>
        {
            var deleted = await service.DeleteAsync(id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteProject")
        .RequireAuthorization("Admin")
        .Produces(204)
        .Produces(404);

        return routes;
    }
}
