using System;
using System.IO;
using System.Text;

namespace DefinetlyNotAFishingBot {

  /// <summary>
  /// Appends timestamped lines to a log file in the user profile, so what the
  /// bot did while nobody was watching can be read afterwards. The file rotates
  /// to ".old" once it grows past one megabyte. Logging never throws.
  /// </summary>
  internal static class Logger {

    private const long MAX_BYTES = 1024 * 1024;
    private const string FILE_NAME = ".definetly_not_a_fishing_bot.log";

    private static readonly object sync = new object();

    /// <summary>Full path of the log file.</summary>
    internal static string FilePath {
      get {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), FILE_NAME);
      }
    }

    internal static void Write(string text) {
      try {
        if (!Config.WriteLogFile)
          return;
        lock (sync) {
          string file = FilePath;
          FileInfo info = new FileInfo(file);
          if (info.Exists && info.Length > MAX_BYTES) {
            string old = file + ".old";
            if (File.Exists(old))
              File.Delete(old);
            File.Move(file, old);
          }
          File.AppendAllText(file, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine, Encoding.UTF8);
        }
      } catch (Exception) {
        // A full disk or a locked file must never take the bot down.
      }
    }
  }
}
