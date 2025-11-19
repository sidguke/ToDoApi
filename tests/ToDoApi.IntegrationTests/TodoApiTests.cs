using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class TodoApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TodoApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Crud_flow_works_with_inmemory_sqlite()
    {
        // Use a unique in-memory SQLite database for this test instance
        var connectionString = "Data Source=file:integration?mode=memory&cache=shared";
        var keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace the DbContext registration with one that uses the in-memory SQLite
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<TodoDb>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<TodoDb>(options =>
                {
                    options.UseSqlite(keepAlive);
                });

                // Ensure DB schema is created
                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TodoDb>();
                db.Database.EnsureCreated();
            });
        }).CreateClient();

        // 1) POST create
        var postResp = await client.PostAsJsonAsync("/todoitems", new { name = "Test item", isComplete = false });
        postResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await postResp.Content.ReadFromJsonAsync<Todo>();
        created.Should().NotBeNull();
        created!.Id.Should().BeGreaterThan(0);

        // 2) GET list
    var list = await client.GetFromJsonAsync<Todo[]>("/todoitems");
        list.Should().NotBeNull();
        list!.Should().ContainSingle();

        // 3) GET by id
        var get = await client.GetFromJsonAsync<Todo>($"/todoitems/{created.Id}");
        get.Should().NotBeNull();
        get!.Name.Should().Be("Test item");

        // 4) PUT update
        var putResp = await client.PutAsJsonAsync($"/todoitems/{created.Id}", new { id = created.Id, name = "Updated", isComplete = true });
        putResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var updated = await client.GetFromJsonAsync<Todo>($"/todoitems/{created.Id}");
        updated!.Name.Should().Be("Updated");
        updated.IsComplete.Should().BeTrue();

        // 5) DELETE
        var delResp = await client.DeleteAsync($"/todoitems/{created.Id}");
        delResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

    var finalList = await client.GetFromJsonAsync<Todo[]>("/todoitems");
        finalList.Should().BeEmpty();

        keepAlive.Close();
    }
}

// Duplicate of the app's Todo model for deserialization in tests
// We reuse the `Todo` type from the main project; do not duplicate it here.
