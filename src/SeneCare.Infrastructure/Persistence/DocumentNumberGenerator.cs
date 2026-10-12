using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;

namespace SeneCare.Infrastructure.Persistence;

/// <summary>« SC-2026-000123 », « FAC-2026-000045 »: PostgreSQL sequences, safe under concurrency.</summary>
internal sealed class DocumentNumberGenerator(SeneCareDbContext db, TimeProvider clock) : IDocumentNumberGenerator
{
    public Task<string> NextServiceRequestNumberAsync(CancellationToken cancellationToken = default) =>
        NextAsync("SC", SeneCareDbContext.ServiceRequestNumberSequence, cancellationToken);

    public Task<string> NextInvoiceNumberAsync(CancellationToken cancellationToken = default) =>
        NextAsync("FAC", SeneCareDbContext.InvoiceNumberSequence, cancellationToken);

    private async Task<string> NextAsync(string prefix, string sequence, CancellationToken cancellationToken)
    {
        // The sequence name is a constant of this class, never user input.
#pragma warning disable EF1002
        var value = await db.Database
            .SqlQueryRaw<long>($"SELECT nextval('{sequence}') AS \"Value\"")
            .SingleAsync(cancellationToken);
#pragma warning restore EF1002
        var year = clock.GetUtcNow().Year.ToString(CultureInfo.InvariantCulture);
        return $"{prefix}-{year}-{value.ToString("D6", CultureInfo.InvariantCulture)}";
    }
}
