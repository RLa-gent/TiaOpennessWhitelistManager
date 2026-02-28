using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace TiaOpennessWhitelistManager;

/// <summary>
/// Hosted background service that exposes a named pipe (<c>TiaWhitelistPipe</c>) so external
/// processes (e.g. a Visual Studio post-build PowerShell script) can trigger whitelisting without
/// making an HTTP/MCP call.
/// </summary>
/// <remarks>
/// Protocol (newline-delimited UTF-8):
/// <list type="bullet">
///   <item>Client → Server: line 1 = absolute executable path</item>
///   <item>Client → Server: line 2 = TIA Portal version string (e.g. <c>18.0</c>)</item>
///   <item>Server → Client: line 1 = result message (starts with <c>Success:</c> or <c>Error:</c>)</item>
/// </list>
/// </remarks>
public sealed partial class TiaOpennessWhitelistPipeService : BackgroundService
{
    internal const string PipeName = "TiaOpennessWhitelistPipe";
    private readonly ILogger<TiaOpennessWhitelistPipeService> _logger;

    public TiaOpennessWhitelistPipeService(ILogger<TiaOpennessWhitelistPipeService> logger) => _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogPipeReady(_logger, PipeName);

        // Define security: Allow the Admin (current process) and all Authenticated Users 
        // to connect to the pipe. This solves the "Toegang tot het pad is geweigerd" error.
        var pipeSecurity = new PipeSecurity();
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));
        // Ensure the admin/owner has full control so the creating process can always manage the pipe.
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        const int maxConsecutiveFailures = 5;
        int consecutiveFailures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Use NamedPipeServerStreamAcl to apply the security settings on creation
                using var server = NamedPipeServerStreamAcl.Create(
                    PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    0, // default inBufferSize
                    0, // default outBufferSize
                    pipeSecurity);

                consecutiveFailures = 0; // pipe created successfully

                await server.WaitForConnectionAsync(stoppingToken);

                using var reader = new StreamReader(server);
                await using var writer = new StreamWriter(server) { AutoFlush = true };

                string? executablePath = await reader.ReadLineAsync(stoppingToken);
                string? tiaVersion = await reader.ReadLineAsync(stoppingToken);

                if (string.IsNullOrWhiteSpace(executablePath) || string.IsNullOrWhiteSpace(tiaVersion))
                {
                    LogIncompletePipeMessage(_logger);
                    await writer.WriteLineAsync("Error: Missing executable path or TIA version.".AsMemory(), stoppingToken);
                    continue;
                }

                LogWhitelisting(_logger, executablePath, tiaVersion);
                string result = new TiaOpennessWhitelistTools().WhitelistTiaOpennessApp(executablePath, tiaVersion);
                LogResult(_logger, result);
                await writer.WriteLineAsync(result.AsMemory(), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (UnauthorizedAccessException ex)
            {
                consecutiveFailures++;
                LogPipeAccessDenied(_logger, PipeName, ex);

                if (consecutiveFailures >= maxConsecutiveFailures)
                {
                    LogPipeGaveUp(_logger, PipeName, maxConsecutiveFailures);
                    return;
                }

                // Exponential backoff: 2s, 4s, 8s, 16s, ...
                int delayMs = 1000 * (1 << consecutiveFailures);
                await Task.Delay(delayMs, stoppingToken);
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                LogPipeError(_logger, ex);

                if (consecutiveFailures >= maxConsecutiveFailures)
                {
                    LogPipeGaveUp(_logger, PipeName, maxConsecutiveFailures);
                    return;
                }

                int delayMs = 1000 * (1 << consecutiveFailures);
                await Task.Delay(delayMs, stoppingToken);
            }
        }

        LogPipeStopped(_logger, PipeName);
    }

    // Fixed LoggerMessage patterns: static partial with explicit ILogger parameter 
    // to satisfy the source generator and improve performance.

    [LoggerMessage(Level = LogLevel.Information, Message = "Named pipe '{PipeName}' is ready.")]
    static partial void LogPipeReady(ILogger logger, string pipeName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Named pipe '{PipeName}' stopped.")]
    static partial void LogPipeStopped(ILogger logger, string pipeName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Received incomplete pipe message.")]
    static partial void LogIncompletePipeMessage(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Whitelisting '{ExecutablePath}' for TIA Portal v{TiaVersion}.")]
    static partial void LogWhitelisting(ILogger logger, string executablePath, string tiaVersion);

    [LoggerMessage(Level = LogLevel.Information, Message = "Result: {Result}")]
    static partial void LogResult(ILogger logger, string result);

    [LoggerMessage(Level = LogLevel.Error, Message = "Named pipe error.")]
    static partial void LogPipeError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Access denied creating pipe '{PipeName}'. Is another instance already running, or is this process not running as Administrator?")]
    static partial void LogPipeAccessDenied(ILogger logger, string pipeName, Exception exception);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Named pipe '{PipeName}' failed {FailureCount} times consecutively. Giving up — the pipe service is now disabled. Kill the other instance or restart as Administrator.")]
    static partial void LogPipeGaveUp(ILogger logger, string pipeName, int failureCount);
}