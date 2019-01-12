namespace Halite3.hlt
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;

    public sealed class Logger
    {
        private readonly string path;

        public Logger(string path)
        {
            this.path = path;
        }

        [Conditional("DEBUG")]
        public void LogDebug(string message)
        {
            string timestamp = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            string prefix = timestamp + ": ";
            File.AppendAllText(path, prefix + message + Environment.NewLine);
        }
    }
}
