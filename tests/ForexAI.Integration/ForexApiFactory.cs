using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ForexAI.Integration;

public class ForexApiFactory : WebApplicationFactory<Program>
{
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
        var apiProjectRoot = Path.Combine(FindProjectRoot(), "src", "ForexAI.API");
        builder.UseContentRoot(apiProjectRoot);
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
