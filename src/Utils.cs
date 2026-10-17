
namespace BatchExport
{
    public static class Utils
    {
        // Logging function that respects _enableLogging setting
        public static void LogInfo(string message, bool enableLogging)
        {
            if (enableLogging)
            {
                Console.WriteLine(message);
            }
        }
    }
}