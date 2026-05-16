using ForexAI.Domain.Enums;

namespace ForexAI.Infrastructure;

internal static class ProjectPaths
{
    private static readonly Lazy<string> _root = new(FindRepoRoot);

    public static string RepoRoot => _root.Value;

    public static string ArtifactsDir =>
        Path.Combine(RepoRoot, "_bmad-output");

    public static string PlanningArtifactsDir =>
        Path.Combine(ArtifactsDir, "planning-artifacts");

    /// <summary>
    /// Storage dir per mode — demo & real trade history disimpan terpisah supaya tidak mix.
    /// Pattern: implementation-artifacts-demo/, implementation-artifacts-real/
    /// </summary>
    public static string GetImplementationArtifactsDir(TradeMode mode) =>
        Path.Combine(ArtifactsDir, $"implementation-artifacts-{mode.ToString().ToLowerInvariant()}");

    /// <summary>
    /// One-time migration: kalau directory legacy "implementation-artifacts/" masih ada
    /// (sebelum mode-aware), rename ke "-demo" — semua existing data dianggap demo trading.
    /// </summary>
    public static void MigrateLegacyArtifactsDir()
    {
        var legacy = Path.Combine(ArtifactsDir, "implementation-artifacts");
        var target = GetImplementationArtifactsDir(TradeMode.Demo);
        if (!Directory.Exists(legacy)) return;
        if (Directory.Exists(target)) return; // sudah migrated
        Directory.Move(legacy, target);
    }

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
