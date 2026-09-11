using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using XtreamForge.Infrastructure.Data;

namespace XtreamForge.Infrastructure.Tests;

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
            dbContext.Settings.Add(new Setting
            {
                Key = "Ui:Theme",
                Value = "Default",
                Description = "Development-only sample setting"
            });
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = new XtreamForgeDbContext(options))
        {
            var setting = await dbContext.Settings.SingleAsync();

            Assert.Equal("Ui:Theme", setting.Key);
            Assert.Equal("Default", setting.Value);
        }
    }
}
