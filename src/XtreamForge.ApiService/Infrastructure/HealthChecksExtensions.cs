using XtreamForge.Database;

namespace XtreamForge.ApiService.Infrastructure;

public static class HealthChecksExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddCustomHealthChecks()
        {
            services.AddHealthChecks()
                .AddDbContextCheck<XtreamForgeDbContext>(name: "database");

            return services;
        }
    }
}
