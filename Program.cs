/// <summary>
/// Entry point and application configuration for the TIA Openness Whitelist Manager MCP Server.
/// Configures CORS, the MCP HTTP transport, and starts the web host.
/// </summary>

using System.Globalization;
using System.Security.Principal;
using TiaOpennessWhitelistManager;


CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

// 1. Check for Administrative Privileges before building the app
if (!IsRunningAsAdmin())
{
    // Log to console as this happens before the builder/logger is initialized
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("ERROR: This service must be run as an Administrator to modify TIA Openness whitelists.");
    Console.ResetColor();

    // Optional: Keep window open for the user to see the error if not running as a background service
    if (Environment.UserInteractive)
    {
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }

    return; // Exit the application
}

try
{
    int port = args.Length > 0 && int.TryParse(args[0], out int p) ? p : 51234;
    await StartServerAsync(port);
}
catch (IOException ex) when (ex.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase))
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"WARNING: Port {GetPortFromException(ex)} is already in use.");
    Console.ResetColor();

    if (Environment.UserInteractive)
    {
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  [1] Enter a different port for the MCP HTTP transport");
        Console.WriteLine("  [2] Continue with named pipe only (no HTTP/MCP)");
        Console.WriteLine("  [Q] Quit");
        Console.Write("Choice: ");

        switch (Console.ReadLine()?.Trim().ToUpperInvariant())
        {
            case "1":
                Console.Write("Enter port number: ");
                if (int.TryParse(Console.ReadLine()?.Trim(), out int newPort) && newPort is > 0 and <= 65535)
                {
                    await StartServerAsync(newPort);
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Invalid port number.");
                    Console.ResetColor();
                }
                break;

            case "2":
                await StartPipeOnlyAsync();
                break;

            default:
                return;
        }
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"FATAL: {ex}");
    Console.ResetColor();

    if (Environment.UserInteractive)
    {
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
}

/// <summary>
/// Starts the full web host with MCP HTTP transport and the named pipe service.
/// </summary>
static async Task StartServerAsync(int port)
{
    var builder = WebApplication.CreateBuilder();

    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
    });

    builder.Services.AddHostedService<TiaOpennessWhitelistPipeService>();

    builder.Services.AddMcpServer()
        .WithHttpTransport()
        .WithTools<TiaOpennessWhitelistTools>();

    var app = builder.Build();

    app.UseCors();
    app.MapMcp();

    Console.WriteLine($"Starting MCP server on http://localhost:{port} + named pipe...");
    app.Run($"http://localhost:{port}");
    await Task.CompletedTask;
}

/// <summary>
/// Starts only the named pipe background service without the HTTP/MCP transport.
/// </summary>
static async Task StartPipeOnlyAsync()
{
    Console.WriteLine("Starting in named-pipe-only mode (no HTTP/MCP transport)...");

    var builder = Host.CreateApplicationBuilder();
    builder.Services.AddHostedService<TiaOpennessWhitelistPipeService>();

    using var host = builder.Build();
    await host.RunAsync();
}

/// <summary>
/// Extracts the port number from the IOException message, or returns "unknown".
/// </summary>
static string GetPortFromException(IOException ex)
{
    var match = System.Text.RegularExpressions.Regex.Match(ex.Message, @":(\d+)");
    return match.Success ? match.Groups[1].Value : "unknown";
}

/// <summary>
/// Helper method to verify the process token has the Administrator SID.
/// </summary>
static bool IsRunningAsAdmin()
{
    if (!OperatingSystem.IsWindows()) return false;

    using var identity = WindowsIdentity.GetCurrent();
    var principal = new WindowsPrincipal(identity);
    return principal.IsInRole(WindowsBuiltInRole.Administrator);
}