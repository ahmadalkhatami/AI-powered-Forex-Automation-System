using ForexAI.Domain.Interfaces;
using ForexAI.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ForexAI.Integration;

public class ForexApiFactory : WebApplicationFactory<Program>
{
    // Isolated temp files — each test class gets fresh state, nothing bleeds into real execution-log.json
    private readonly string _positionsFile = Path.GetTempFileName();
    private readonly string _signalsFile = Path.GetTempFileName();

    static ForexApiFactory()
    {
        // Services like StubMarketDataService resolve _bmad-output/ relative to CWD.
        // Set CWD to project root so relative paths work from test runner too.
        var projectRoot = FindProjectRoot();
        Directory.SetCurrentDirectory(projectRoot);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // WebApplicationFactory infers content root as <solutionRoot>/ForexAI.API,
        // but the actual API project lives in src/ForexAI.API. Override to correct path.
        builder.UseContentRoot(Path.Combine(FindProjectRoot(), "src", "ForexAI.API"));

        builder.ConfigureServices(services =>
        {
            // Replace real repositories with isolated temp-file versions
            services.AddScoped<ITradePositionRepository>(_ =>
                new JsonTradePositionRepository(_positionsFile));
            services.AddScoped<ISignalRepository>(_ =>
                new JsonSignalRepository(_signalsFile));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            if (File.Exists(_positionsFile)) File.Delete(_positionsFile);
            if (File.Exists(_signalsFile)) File.Delete(_signalsFile);
        }
    }

    private static string FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "_bmad-output")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Cannot find project root (_bmad-output not found in any ancestor directory)");
    }
}
