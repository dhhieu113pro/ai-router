namespace AiRouter.Persistence.Sqlite;

public sealed record SqliteStoreOptions
{
    public SqliteStoreOptions(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A SQLite connection string is required.", nameof(connectionString));

        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }
}
