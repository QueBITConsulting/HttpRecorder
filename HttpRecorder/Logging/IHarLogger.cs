using QueBIT.HttpRecorder.Repositories.HAR;

namespace QueBIT.HttpRecorder.Logging
{
    public interface IHarLogger
    {
        void LogHarArchive(string loggerName, HttpArchive archive);
    }
}