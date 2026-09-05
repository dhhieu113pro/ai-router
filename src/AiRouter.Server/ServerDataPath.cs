public static class ServerDataPath
{
    public static string Resolve(string? configured) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? "/data/ai-router.db" : configured);
}
