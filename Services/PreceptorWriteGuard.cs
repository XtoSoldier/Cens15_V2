using System.Data;
using CENS15_V2.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CENS15_V2.Services;

internal static class PreceptorWriteGuard
{
    public static async Task<T> ExecuteAsync<T>(AppDbContext db, Func<Task<T>> write)
    {
        // The association and role editors must share serializable isolation to prevent write skew.
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;
        try
        {
            var result = await write();
            if (tx != null) await tx.CommitAsync();
            return result;
        }
        catch (Exception ex) when (ex is DbUpdateException || ex is PostgresException { SqlState: "40001" or "40P01" })
        {
            db.ChangeTracker.Clear();
            throw new PreceptorConflictException(ex);
        }
    }
}
