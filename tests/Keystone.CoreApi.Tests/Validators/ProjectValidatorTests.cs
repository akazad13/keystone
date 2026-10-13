using Keystone.CoreApi.Validators;

namespace Keystone.CoreApi.Tests.Validators;

public sealed class ProjectValidatorTests
{
    private readonly CreateProjectValidator _create = new();
    private readonly UpdateProjectValidator _update = new();

    [Fact]
    public void A_name_is_enough_to_create_a_project()
    {
        Assert.True(_create.Validate(new CreateProjectRequest("Website", null, null)).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_is_required(string name)
    {
        var result = _create.Validate(new CreateProjectRequest(name, null, null));

        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public void Name_is_at_most_200_characters()
    {
        Assert.True(_create.Validate(new CreateProjectRequest(new string('a', 200), null, null)).IsValid);
        Assert.False(_create.Validate(new CreateProjectRequest(new string('a', 201), null, null)).IsValid);
    }

    [Fact]
    public void Description_is_at_most_2000_characters()
    {
        Assert.True(_create.Validate(new CreateProjectRequest("x", new string('d', 2000), null)).IsValid);
        Assert.False(_create.Validate(new CreateProjectRequest("x", new string('d', 2001), null)).IsValid);
    }

    [Theory]
    [InlineData("Active")]
    [InlineData("Completed")]
    [InlineData("Archived")]
    public void Update_accepts_the_known_statuses(string status)
    {
        Assert.True(_update.Validate(new UpdateProjectRequest("x", null, status, null)).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Deleted")]
    [InlineData("active")]
    public void Update_rejects_other_statuses(string status)
    {
        var result = _update.Validate(new UpdateProjectRequest("x", null, status, null));

        Assert.Contains(result.Errors, e => e.PropertyName == "Status");
    }

    [Fact]
    public void Update_requires_a_name()
    {
        Assert.False(_update.Validate(new UpdateProjectRequest("", null, "Active", null)).IsValid);
    }
}
