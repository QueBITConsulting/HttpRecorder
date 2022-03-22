using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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

        private static object _lock = new object();
        private static int logCounter;

        private readonly ILogger _logger;
        private string _logDir;


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
                var threadId = Thread.CurrentThread.ManagedThreadId;
                var logsFolder = GetLogDirectory();

                var archive = new HttpArchive(interaction);

                MaskSensitiveData(archive);

                foreach (var entry in archive.Log.Entries)
                {
                    var logId = Interlocked.Increment(ref logCounter);

                    if (string.IsNullOrEmpty(logsFolder))
                    {
                        _logger.LogDebug(JsonSerializer.Serialize(entry, JsonOptions));
                    }
                    else
                    {
                        var traceFolderName = Path.Combine(logsFolder, "trace");
                        var folderName = Path.Combine(traceFolderName, MakeValidFilename(interaction.Name));

                        if (!Directory.Exists(folderName))
                        {
                            Directory.CreateDirectory(folderName);
                        }

                        var fileName = MakeValidFilename($"{logId:D4} T-{threadId} {entry.Request.Method} {entry.Response.Status} {entry.Request.Url.Host}.txt");

                        var sb = new StringBuilder();
                        sb.AppendLine("INFO");
                        sb.AppendLine($"Creator: {archive.Log.Creator}");
                        sb.AppendLine($"Version: {archive.Log.Version}");
                        sb.AppendLine($"Started: {entry.StartedDateTime:s}");
                        sb.AppendLine($"Elapsed: {entry.Timings.Wait} ms");
                        sb.AppendLine();

                        sb.AppendLine("REQUEST");
                        sb.AppendLine($"{entry.Request.Method} {entry.Request.Url}");
                        entry.Request.QueryString?.ForEach(x => sb.AppendLine($"{x.Name}={x.Value}"));
                        entry.Request.Headers?.ForEach(x => sb.AppendLine($"{x.Name}={x.Value}"));
                        sb.AppendLine();
                        entry.Request.PostData?.Params.ForEach(x => sb.AppendLine($"{x.Name}={x.Value}"));
                        sb.AppendLine(entry.Request.PostData?.Text);
                        sb.AppendLine();

                        sb.AppendLine("RESPONSE");
                        sb.AppendLine($"Status: {entry.Response.Status}  {entry.Response.StatusText}");
                        sb.AppendLine($"Content Size={entry.Response.Content.Size}");
                        entry.Response.Headers?.ForEach(x => sb.AppendLine($"{x.Name}={x.Value}"));
                        sb.AppendLine();
                        sb.AppendLine(entry.Response.Content.Text);

                        File.WriteAllText(Path.Combine(folderName, fileName), sb.ToString());

                        // Are we now sync because of the semaphore?
                        // Can we insert into the har file instead of deserialize and serialize?
                        var harFileName = MakeValidFilename($"HTTP Trace.har");
                        var harFile = Path.Combine(traceFolderName, harFileName);
                        lock (_lock)
                        {
                            if (!File.Exists(harFile))
                            {
                                File.WriteAllText(harFile, JsonSerializer.Serialize(archive, JsonOptions));
                            }
                            else
                            {
                                var newItem = "," + Environment.NewLine;
                                newItem += JsonSerializer.Serialize(entry, JsonOptions);
                                newItem += Environment.NewLine;
                                newItem += "]\r\n  }\r\n}"; // These 9 characters are very important.  Don't mess with this!

                                var offset = 9;

                                using (var fileStream = File.OpenWrite(harFile))
                                {
                                    fileStream.Seek(-offset, SeekOrigin.End);
                                    var bytes = Encoding.UTF8.GetBytes(newItem);

                                    fileStream.Write(bytes, 0, bytes.Length);
                                }
                            }
                        }
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

        private void MaskSensitiveData(HttpArchive archive)
        {

            foreach (var logEntry in archive.Log.Entries)
            {

                var pattern = @"(?<=password=).+?(?=(;|'|\""|$))";
                if (logEntry.Request.PostData != null)
                {
                    foreach (Match m in Regex.Matches(logEntry.Request.PostData.Text, pattern, RegexOptions.Multiline))
                    {
                        logEntry.Request.PostData.Text = logEntry.Request.PostData.Text?.Replace(m.Value, "\"" + new string('*', m.Value.Length - 1));
                    }
                }

                foreach (var header in logEntry.Request.Headers)
                {
                    if (header.Name.ToLower() == "authorization")
                    {
                        header.Value = "**** MASKED ****";
                    }
                }
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
            if (_logDir != null) return _logDir;

            if (_logger.GetType().Name == "FileLogger")
            {
                try
                {
                    var fileName = _logger.GetType().GetProperty("Filename").GetValue(_logger, null)?.ToString();
                    var logDir = Path.GetDirectoryName(fileName);
                    if (!string.IsNullOrEmpty(filename))
                        logDir = Path.Combine(logDir, filename);

                    _logDir = logDir;
                    return _logDir;
                }
                catch (Exception ex)
                {
                    _logger.LogError(new Exception("Logger looked like a FileLogger but calling for Filename failed", ex), "HAR GetLogDirectory failed.");
                }
            }

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
                    logDir = Path.Combine(logDir, filename);

                _logDir = logDir;
                return _logDir;
            }

            return null;
        }
    }
}
