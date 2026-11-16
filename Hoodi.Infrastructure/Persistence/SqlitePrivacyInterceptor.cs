using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Hoodi.Infrastructure.Persistence;

public sealed class SqlitePrivacyInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA secure_delete = ON;";
        command.ExecuteNonQuery();
    }
    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA secure_delete = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
