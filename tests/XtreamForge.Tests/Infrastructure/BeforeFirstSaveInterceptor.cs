using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace XtreamForge.Tests.Infrastructure;

/// <summary>
/// Runs an action once, just before the first asynchronous save of the context, to simulate a concurrent request
/// writing between the reads and the save of the code under test.
/// </summary>
public sealed class BeforeFirstSaveInterceptor(Func<DbContext, CancellationToken, Task> action) : SaveChangesInterceptor
{
    private bool _hasRun;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (!_hasRun && eventData.Context is { } dbContext)
        {
            _hasRun = true;
            await action(dbContext, cancellationToken);
        }

        return result;
    }
}
