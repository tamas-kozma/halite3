namespace Halite3
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

        public bool IsMuted { get; set; }

        [Conditional("DEBUG")]
        public void LogDebug(string message)
        {
            if (IsMuted)
            {
                return;
            }

            string timestamp = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            string prefix = timestamp + ": ";
            File.AppendAllText(path, prefix + message + Environment.NewLine);
        }

        public void LogInfo(string message)
        {
            if (IsMuted)
            {
                return;
            }

            string timestamp = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            string prefix = timestamp + ": ";
            File.AppendAllText(path, prefix + message + Environment.NewLine);
        }

        public void LogError(string message)
        {
            if (IsMuted)
            {
                return;
            }

            string timestamp = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            string prefix = timestamp + ": !!! ";
            File.AppendAllText(path, prefix + message + Environment.NewLine);
        }
    }
}
