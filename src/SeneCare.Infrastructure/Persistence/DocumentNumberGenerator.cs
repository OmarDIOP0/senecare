using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SeneCare.Application.Abstractions;

namespace SeneCare.Infrastructure.Persistence;

/// <summary>« SC-2026-000123 », « FAC-2026-000045 »: SQL Server sequences, safe under concurrency.</summary>
internal sealed class DocumentNumberGenerator(SeneCareDbContext db, TimeProvider clock) : IDocumentNumberGenerator
{
    public Task<string> NextServiceRequestNumberAsync(CancellationToken cancellationToken = default) =>
        NextAsync("SC", SeneCareDbContext.ServiceRequestNumberSequence, cancellationToken);

    public Task<string> NextInvoiceNumberAsync(CancellationToken cancellationToken = default) =>
        NextAsync("FAC", SeneCareDbContext.InvoiceNumberSequence, cancellationToken);

    private async Task<string> NextAsync(string prefix, string sequence, CancellationToken cancellationToken)
    {
        // A plain command, not an EF query: EF would wrap it in a subquery, where SQL Server refuses NEXT VALUE FOR.
        // The sequence name is a constant of this class, never user input.
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
#pragma warning disable CA2100
            command.CommandText = $"SELECT NEXT VALUE FOR [{sequence}]";
#pragma warning restore CA2100
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            var value = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            var year = clock.GetUtcNow().Year.ToString(CultureInfo.InvariantCulture);
            return $"{prefix}-{year}-{value.ToString("D6", CultureInfo.InvariantCulture)}";
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }
    }
}
