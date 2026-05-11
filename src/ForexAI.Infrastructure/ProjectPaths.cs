namespace ForexAI.Infrastructure;

internal static class ProjectPaths
{
    private static readonly Lazy<string> _root = new(FindRepoRoot);

    public static string RepoRoot => _root.Value;

    public static string ArtifactsDir =>
        Path.Combine(RepoRoot, "_bmad-output");

    public static string PlanningArtifactsDir =>
        Path.Combine(ArtifactsDir, "planning-artifacts");

    public static string ImplementationArtifactsDir =>
        Path.Combine(ArtifactsDir, "implementation-artifacts");

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
