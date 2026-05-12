using ForexAI.Domain.Interfaces;
using ForexAI.Infrastructure.Persistence.Repositories;
using ForexAI.Infrastructure.Services;
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
        var projectRoot = FindProjectRoot();

        // DeferredHostBuilder passes --contentRoot src/ForexAI.API (relative) to Program.Main.
        // HostApplicationBuilder resolves it against AppContext.BaseDirectory (test binary dir),
        // producing a non-existent path. PhysicalFileProvider throws if the directory is missing,
        // so we create it preemptively. ConfigureWebHost.UseContentRoot then corrects the path.
        var dummyContentRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "src", "ForexAI.API");
        Directory.CreateDirectory(dummyContentRoot);

        // Set CWD to project root so that ProjectPaths._bmad-output/ resolution works
        // for StubMarketDataService and BmadSignalAnalyzer when they use relative paths.
        Directory.SetCurrentDirectory(projectRoot);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // Correct the content root: DeferredHostBuilder passed a relative path to Program.Main;
        // this UseContentRoot call runs via deferred actions during IHostBuilder.Build() and
        // sets IWebHostEnvironment.ContentRootPath to the real project directory.
        builder.UseContentRoot(Path.Combine(FindProjectRoot(), "src", "ForexAI.API"));

        builder.ConfigureServices(services =>
        {
            // Replace real repositories with isolated temp-file versions
            services.AddScoped<ITradePositionRepository>(_ =>
                new JsonTradePositionRepository(_positionsFile));
            services.AddScoped<ISignalRepository>(_ =>
                new JsonSignalRepository(_signalsFile));

            // Replace Yahoo Finance with stub so tests don't hit the internet
            services.AddTransient<IMarketDataService, StubMarketDataService>();
            services.AddTransient<ICandleDataService>(_ => new NullCandleDataService());
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
