using Microsoft.EntityFrameworkCore;
using XtreamForge.Database;

namespace XtreamForge.ApiService.Infrastructure;

public static class DatabaseExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddDatabase()
        {
            services.AddDbContextFactory<XtreamForgeDbContext>((serviceProvider, options) =>
            {
                var connectionString = serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString("database");
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException("Connection string 'database' is required.");
                }

                options.UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(XtreamForgeDbContext).Assembly.FullName);
                    npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                });
            });

            services.AddScoped(static serviceProvider =>
            {
                return serviceProvider
                    .GetRequiredService<IDbContextFactory<XtreamForgeDbContext>>()
                    .CreateDbContext();
            });

            return services;
        }
    }

    extension(IApplicationBuilder appBuilder)
    {
        public IApplicationBuilder UseDatabase()
        {
            using var scope = appBuilder.ApplicationServices.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
            dbContext.Database.Migrate();

            return appBuilder;
        }
    }
}
