using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ForexAI.Infrastructure.Services;

public record EaDeployResult(
    bool   Success,
    string Message,
    bool   Compiled,
    string DeployedPath = "",
    string CompileLog   = ""
);

/// <summary>
/// Copy ForexAI_Bridge.mq5 terbaru ke folder MT5 Experts, lalu buka MetaEditor
/// otomatis via AppleScript (F4 di MT5). User cukup tekan F7 sekali di MetaEditor.
/// </summary>
public class EaDeployService
{
    private static readonly string WinePrefix =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "net.metaquotes.wine.metatrader5");

    private static readonly string ExpertsMacPath =
        Path.Combine(WinePrefix, "drive_c", "Program Files", "MetaTrader 5", "MQL5", "Experts");

    private readonly ILogger<EaDeployService> _logger;

    public EaDeployService(ILogger<EaDeployService> logger) => _logger = logger;

    public async Task<EaDeployResult> DeployAsync(CancellationToken ct = default)
    {
        // 1. Cari source .mq5 dari project root
        var sourcePath = FindSourceMq5();
        if (sourcePath == null)
            return Fail("ForexAI_Bridge.mq5 tidak ditemukan di folder project.");

        // 2. Pastikan Experts folder ada
        if (!Directory.Exists(ExpertsMacPath))
            return Fail($"MT5 Experts folder tidak ditemukan.\nPastikan MetaTrader 5 sudah pernah dibuka: {ExpertsMacPath}");

        // 3. Copy .mq5 ke MT5 Experts folder
        var destPath = Path.Combine(ExpertsMacPath, "ForexAI_Bridge.mq5");
        try
        {
            File.Copy(sourcePath, destPath, overwrite: true);
            _logger.LogInformation("EA copied: {Src} → {Dest}", sourcePath, destPath);
        }
        catch (Exception ex)
        {
            return Fail($"Gagal copy file: {ex.Message}");
        }

        // 4. Buka MetaEditor + auto-compile via AppleScript (F4 → F7)
        string compileLog  = "";
        bool   compiled    = false;
        if (OperatingSystem.IsMacOS())
        {
            try
            {
                (compiled, compileLog) = await CompileViaAppleScriptAsync(destPath, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Auto-compile via AppleScript gagal");
                compileLog = ex.Message;
            }
        }

        var message = compiled
            ? "EA v1.18 ✅ Compile berhasil! EA aktif — drag ke chart jika belum ada."
            : "EA v1.18 dicopy ✅ MetaEditor terbuka — tekan F7 untuk compile, lalu drag EA ke chart.";

        return new EaDeployResult(true, message, compiled, destPath, compileLog);
    }

    // ── Buka MetaEditor, tekan F7, tunggu .ex5 muncul ──────────────────────────
    private async Task<(bool compiled, string log)> CompileViaAppleScriptAsync(
        string destMq5Path, CancellationToken ct)
    {
        // Script: activate MT5 → F4 (MetaEditor) → tunggu → F7 (compile)
        var script = """
            tell application "MetaTrader 5"
                activate
            end tell
            delay 2.0
            tell application "System Events"
                tell process "MetaTrader 5"
                    key code 118
                end tell
            end tell
            delay 5.0
            tell application "MetaTrader 5"
                activate
            end tell
            delay 0.5
            tell application "System Events"
                tell process "MetaTrader 5"
                    key code 98
                end tell
            end tell
            """;

        var scriptPath = Path.Combine(Path.GetTempPath(), "forexai-compile.scpt");
        await File.WriteAllTextAsync(scriptPath, script, ct);

        var psi = new ProcessStartInfo
        {
            FileName               = "osascript",
            Arguments              = $"\"{scriptPath}\"",
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
        };

        using var proc = new Process { StartInfo = psi };
        proc.Start();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        await proc.WaitForExitAsync(cts.Token);

        var scriptOk = proc.ExitCode == 0;
        _logger.LogInformation("AppleScript compile: exitCode={Exit}", proc.ExitCode);

        if (!scriptOk)
            return (false, await proc.StandardError.ReadToEndAsync(ct));

        // Tunggu file .ex5 muncul (hasil compile) — max 20 detik
        var ex5Path = Path.ChangeExtension(destMq5Path, ".ex5");
        var copyTime = DateTime.UtcNow;

        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(1000, ct);
            if (File.Exists(ex5Path))
            {
                var written = File.GetLastWriteTimeUtc(ex5Path);
                if (written >= copyTime.AddSeconds(-2))
                {
                    _logger.LogInformation("EA compiled: {Ex5}", ex5Path);
                    return (true, $"Compiled at {written:HH:mm:ss}");
                }
            }
        }

        // Script ran OK tapi .ex5 belum muncul — MetaEditor mungkin butuh dibuka manual dulu
        return (false, "F7 terkirim tapi .ex5 belum muncul — tekan F7 sekali lagi di MetaEditor.");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string? FindSourceMq5()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8; i++)
        {
            if (dir == null) break;
            var candidate = Path.Combine(dir.FullName, "mql5", "ForexAI_Bridge.mq5");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private static EaDeployResult Fail(string msg)
        => new(false, msg, Compiled: false);
}
