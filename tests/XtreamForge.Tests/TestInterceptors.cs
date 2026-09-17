using System.Data.Common;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace XtreamForge.Tests;

internal sealed class DbCommandCounterInterceptor : DbCommandInterceptor
{
    private int _commandCount;
    private int _writeCommandCount;

    public int CommandCount => Volatile.Read(ref _commandCount);

    public int WriteCommandCount => Volatile.Read(ref _writeCommandCount);

    public void Reset()
    {
        Interlocked.Exchange(ref _commandCount, 0);
        Interlocked.Exchange(ref _writeCommandCount, 0);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Count(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Count(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        Count(command);
        return base.ScalarExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Count(command);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        Count(command);
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Count(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void Count(DbCommand command)
    {
        Interlocked.Increment(ref _commandCount);

        var sql = command.CommandText.TrimStart();
        if (sql.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)
            || sql.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
            || sql.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref _writeCommandCount);
        }
    }
}

internal sealed class SaveChangesCounterInterceptor : SaveChangesInterceptor
{
    private int _saveChangesCount;

    public int SaveChangesCount => Volatile.Read(ref _saveChangesCount);

    public void Reset() => Interlocked.Exchange(ref _saveChangesCount, 0);

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Interlocked.Increment(ref _saveChangesCount);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _saveChangesCount);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
