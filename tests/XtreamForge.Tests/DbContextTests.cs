using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using XtreamForge.Data;

namespace XtreamForge.Tests;

public sealed class DbContextTests
{
    [Fact]
    public async Task Settings_CanBeSavedAndRead()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<XtreamForgeDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var dbContext = new XtreamForgeDbContext(options))
        {
            await dbContext.Database.EnsureCreatedAsync();
            dbContext.XtreamSources.Add(new Domain.XtreamSource
            {
                Id = 1,
                Protocol = "http",
                Host = "provider.net",
                Port = 8080
            });

            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = new XtreamForgeDbContext(options))
        {
            var source = await dbContext.XtreamSources.SingleAsync();

            Assert.Equal(1, source.Id);
            Assert.Equal("http", source.Protocol);
            Assert.Equal("provider.net", source.Host);
            Assert.Equal(8080, source.Port);
        }
    }
}
