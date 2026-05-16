using ForexAI.Domain.Enums;

namespace ForexAI.Infrastructure;

internal static class ProjectPaths
{
    private static readonly Lazy<string> _root = new(FindRepoRoot);

    public static string RepoRoot => _root.Value;

    public static string ArtifactsDir =>
        Path.Combine(RepoRoot, "data");

    public static string GetImplementationArtifactsDir(TradeMode mode) =>
        Path.Combine(ArtifactsDir, mode.ToString().ToLowerInvariant());

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException(
                "Cannot locate repository root: no .git directory found in ancestor paths.");
    }
}
