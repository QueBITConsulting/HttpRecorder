using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Targets;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace HttpRecorder.Repositories.HAR
{
    /// <summary>
    /// LoggerInteractionRepository.
    /// </summary>
    public class LoggerInteractionRepository : IInteractionRepository
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            IgnoreNullValues = true,
            WriteIndented = true,
        };

        private static int logCounter;

        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="LoggerInteractionRepository"/> class.
        /// LoggerInteractionRepository.
        /// </summary>
        /// <param name="logger">An Ilogger with a file logger available.</param>
        public LoggerInteractionRepository(ILogger logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public Task<bool> ExistsAsync(string interactionName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        /// <inheritdoc />
        public Task<Interaction> LoadAsync(string interactionName, CancellationToken cancellationToken = default)
        {
            throw new HttpRecorderException($"Error while loading file {interactionName}: This mode is not supported");
        }

        /// <inheritdoc />
        public Task<Interaction> StoreAsync(Interaction interaction, CancellationToken cancellationToken = default)
        {
            if (!_logger.IsEnabled(LogLevel.Debug))
            {
                return Task.FromResult<Interaction>(null);
            }

            if (interaction == null)
            {
                throw new ArgumentNullException(nameof(interaction));
            }

            if (interaction.Messages.Count == 0)
            {
                return Task.FromResult<Interaction>(null);
            }

            try
            {
                var archive = new HttpArchive(interaction);

                var logId = Interlocked.Increment(ref logCounter);

                var message = interaction.Messages[0];
                var method = message.Response.RequestMessage.Method;
                var host = message.Response.RequestMessage.RequestUri.Host;
                var statusCode = (int)message.Response.StatusCode;
                var threadId = Thread.CurrentThread.ManagedThreadId;

                var logsFolder = GetLogDirectory();
                if (string.IsNullOrEmpty(logsFolder))
                {
                    _logger.LogDebug(JsonSerializer.Serialize(message, JsonOptions));
                }
                else
                {
                    var traceFolderName = Path.Combine(logsFolder, "trace");
                    var folderName = Path.Combine(traceFolderName, MakeValidFilename(interaction.Name));

                    if (!Directory.Exists(folderName))
                    {
                        Directory.CreateDirectory(folderName);
                    }

                    var fileName = MakeValidFilename($"{logId:D4} T_{threadId} S_{statusCode} {method} {host}.txt");

                    var sb = new StringBuilder();
                    sb.AppendLine("INFO");
                    sb.AppendLine($"Creator: {archive.Log.Creator}");
                    sb.AppendLine($"Version: {archive.Log.Version}");
                    sb.AppendLine($"Started: {message.Timings.StartedDateTime:s}");
                    sb.AppendLine($"Elapsed: {message.Timings.Time:g}");
                    sb.AppendLine();
                    sb.AppendLine();

                    sb.AppendLine("REQUEST");
                    sb.AppendLine($"URI: {message.Response.RequestMessage.RequestUri}");
                    sb.AppendLine($"Method: {message.Response.RequestMessage.Method}");
                    foreach (var header in message.Response.RequestMessage.Headers)
                    {
                        sb.AppendLine($"HEADER: {header.Key}={string.Join(";", header.Value)}");
                    }

                    foreach (var property in message.Response.RequestMessage.Properties)
                    {
                        sb.AppendLine($"PROPERTY: {property.Key}={property.Value}");
                    }

                    sb.AppendLine("Request Body");
                    var requestBody = message.Response.RequestMessage.Content?.ReadAsStringAsync().Result;
                    sb.AppendLine(requestBody);
                    sb.AppendLine();
                    sb.AppendLine();

                    sb.AppendLine("RESPONSE");
                    sb.AppendLine($"Status: {message.Response.StatusCode}");
                    sb.AppendLine($"Reason: {message.Response.ReasonPhrase}");
                    foreach (var header in message.Response.Headers)
                    {
                        sb.AppendLine($"HEADER: {header.Key}={string.Join(";", header.Value)}");
                    }

                    sb.AppendLine("Response Body");
                    var responseBody = message.Response.Content.ReadAsStringAsync().Result;
                    sb.AppendLine(responseBody);
                    sb.AppendLine();
                    sb.AppendLine();

                    File.WriteAllText(Path.Combine(folderName, fileName), sb.ToString());

                    // Are we now sync because of the semaphore?
                    // Can we insert into the har file instead of deserialize and serialize?
                    var harFileName = MakeValidFilename($"HTTP Trace.har");
                    var harFile = Path.Combine(traceFolderName, harFileName);
                    if (!File.Exists(harFile))
                    {
                        File.WriteAllText(harFile, JsonSerializer.Serialize(archive, JsonOptions));
                    }
                    else
                    {
                        // TODO make this more better....
                        var json = File.ReadAllText(harFile);
                        var httpArchive = JsonSerializer.Deserialize<HttpArchive>(json, JsonOptions);
                        httpArchive.Log.Entries.Add(new Entry(message));
                        File.WriteAllText(harFile, JsonSerializer.Serialize(httpArchive, JsonOptions));
                    }
                }

                // Return null so that caller does NOT store and append to a list internally
                return Task.FromResult<Interaction>(null);
            }
            catch (Exception ex) when ((ex is IOException) || (ex is JsonException))
            {
                throw new HttpRecorderException($"Error while writing file {interaction.Name}: {ex.Message}", ex);
            }
        }

        private static string MakeValidFilename(string text)
        {
            var replacement = Path.GetInvalidFileNameChars().Aggregate(text, (current, c) => current.Replace(c, '_'));
            text = text.Replace(text, replacement);
            return text;
        }

        private string GetLogDirectory(string filename = null)
        {
            // LogManager.Configuration == null curing unit testing
            if (LogManager.Configuration == null)
            {
                return null;
            }

            if (LogManager.Configuration.AllTargets.FirstOrDefault(x => x is FileTarget) is FileTarget fileTarget)
            {
                var currentLogFile = fileTarget.FileName.Render(new LogEventInfo { TimeStamp = DateTime.Now });
                var logDir = Path.GetDirectoryName(currentLogFile);

                if (!string.IsNullOrEmpty(filename))
                {
                    logDir = Path.Combine(logDir, filename);
                }

                return logDir;
            }

            return null;
        }
    }
}
