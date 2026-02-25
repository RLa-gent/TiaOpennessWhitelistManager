using Microsoft.Win32;
using ModelContextProtocol.Server;
using System.Security.Cryptography;

namespace TiaOpennessWhitelistManager
{
    [McpServerToolType]
    public partial class TiaOpennessWhitelistTools
    {
        /// <summary>
        /// Adds an external executable to the Siemens TIA Portal Openness whitelist by writing
        /// the application path, last-modified date, and SHA-256 file hash to the Windows registry.
        /// </summary>
        /// <param name="executablePath">The absolute path to the .exe file to whitelist.</param>
        /// <param name="tiaVersion">
        /// The TIA Portal version number (e.g., <c>"18.0"</c>, <c>"21.0"</c>).
        /// Versions 21.0 and newer use a version-agnostic registry path under <c>AllowList</c>;
        /// older versions use a versioned path under <c>Whitelist</c>.
        /// </param>
        /// <returns>
        /// A string describing the result: a success message, or an error message if the file was
        /// not found, access was denied (requires Administrator), or another exception occurred.
        /// </returns>
        /// <remarks>
        /// This tool must be run with Administrator privileges to write to <c>HKEY_LOCAL_MACHINE</c>.
        /// </remarks>
        [McpServerTool]
        public partial string WhitelistTiaOpennessApp(string executablePath, string tiaVersion)
        {
            if (!File.Exists(executablePath)) return $"Error: File '{executablePath}' not found.";
            try
            {
                var fileInfo = new FileInfo(executablePath);
                string dateModified = fileInfo.LastWriteTimeUtc.ToString("yyyy/MM/dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
                using var stream = File.OpenRead(executablePath);
                string fileHash = Convert.ToBase64String(SHA256.HashData(stream));
                string appName = fileInfo.Name;
                bool isV21OrNewer = double.TryParse(tiaVersion, System.Globalization.CultureInfo.InvariantCulture, out double v) && v >= 21.0;
                string registryPath = isV21OrNewer
                    ? $@"SOFTWARE\Siemens\Automation\Openness\AllowList\{appName}\Entry"
                    : $@"SOFTWARE\Siemens\Automation\Openness\V{tiaVersion}\Whitelist\{appName}\Entry";
                using var key = Registry.LocalMachine.CreateSubKey(registryPath)
                    ?? throw new UnauthorizedAccessException("Failed to open registry key.");
                key.SetValue("Path", fileInfo.FullName, RegistryValueKind.String);
                key.SetValue("DateModified", dateModified, RegistryValueKind.String);
                key.SetValue("FileHash", fileHash, RegistryValueKind.String);
                return $"Success: '{appName}' added to the TIA Portal Openness whitelist (Version {tiaVersion}).";
            }
            catch (UnauthorizedAccessException)
            {
                return "Error: Access Denied. You must run this MCP Server as an Administrator.";
            }
            catch (Exception ex)
            {
                return $"Fatal Error: {ex.Message}";
            }
        }
    }
}