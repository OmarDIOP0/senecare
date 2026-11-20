using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SeneCare.Infrastructure.Persistence;

/// <summary>
/// Counts the SQL commands of each request (shown in the request log line, to spot N+1 pages) and logs a warning
/// for any command slower than <see cref="SlowThreshold"/>. No extra package: plain EF Core interceptor + Serilog.
/// </summary>
public sealed partial class QueryDiagnostics(ILogger<QueryDiagnostics> logger) : DbCommandInterceptor
{
    public static readonly TimeSpan SlowThreshold = TimeSpan.FromMilliseconds(300);

    private static readonly ConditionalWeakTable<DbContext, StrongBox<int>> Counts = [];

    /// <summary>SQL commands run so far by this context (one context per request).</summary>
    public static int CountFor(DbContext? context) =>
        context is not null && Counts.TryGetValue(context, out var count) ? count.Value : 0;

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Observe(command, eventData);
        return base.ReaderExecuted(command, eventData, result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        Observe(command, eventData);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Observe(command, eventData);
        return base.ScalarExecuted(command, eventData, result);
    }

    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    {
        Observe(command, eventData);
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Observe(command, eventData);
        return base.NonQueryExecuted(command, eventData, result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Observe(command, eventData);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    private void Observe(DbCommand command, CommandExecutedEventData eventData)
    {
        if (eventData.Context is { } context)
        {
            Interlocked.Increment(ref Counts.GetOrCreateValue(context).Value);
        }

        if (eventData.Duration > SlowThreshold)
        {
            // The SQL text only (parameters are placeholders, values are never logged).
            var text = command.CommandText.Length > 800 ? command.CommandText[..800] + "…" : command.CommandText;
            LogSlow(logger, eventData.Duration.TotalMilliseconds, text);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Slow SQL ({Milliseconds:0} ms): {Sql}")]
    private static partial void LogSlow(ILogger logger, double milliseconds, string sql);
}
