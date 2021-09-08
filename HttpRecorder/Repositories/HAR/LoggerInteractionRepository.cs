using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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

        private readonly ILogger _logger;

        private int _logCtr;

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
            if (!_logger.IsEnabled(LogLevel.Trace))
            {
                return null;
            }

            if (interaction == null)
            {
                throw new ArgumentNullException(nameof(interaction));
            }

            if (interaction.Messages.Count == 0)
            {
                return null;
            }

            var logsFolder = GetLogDirectory();

            if (string.IsNullOrEmpty(logsFolder))
            {
                return null;
            }

            try
            {
                var archive = new HttpArchive(interaction);

                var logId = Interlocked.Increment(ref _logCtr);

                var message = interaction.Messages[0];
                var method = message.Response.RequestMessage.Method;
                var host = message.Response.RequestMessage.RequestUri.Host;
                var statusCode = message.Response.StatusCode;
                var threadId = Thread.CurrentThread.ManagedThreadId;

                var folderName = Path.Combine(logsFolder, "trace", MakeValidFilename(interaction.Name));

                if (!Directory.Exists(folderName))
                {
                    Directory.CreateDirectory(folderName);
                }

                var fileName = MakeValidFilename($"{threadId}_{logId} {statusCode} {method} {host}.har");

                File.WriteAllText(Path.Combine(folderName, fileName), JsonSerializer.Serialize(archive, JsonOptions));

                // Return null so that caller does NOT store and append to a list internally
                return null;
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
