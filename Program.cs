/// <summary>
/// Entry point and application configuration for the TIA Openness Whitelist Manager MCP Server.
/// Configures CORS, the MCP HTTP transport, and starts the web host.
/// </summary>

using System.Security.Principal;
using TiaOpennessWhitelistManager;

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

var builder = WebApplication.CreateBuilder(args);

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

int port = builder.Configuration.GetValue<int>("port", 51234);
app.Run($"http://localhost:{port}");

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