using System;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DefinetlyNotAFishingBot {
  internal static class Program {

    /// <summary>
    /// Entry point. "--selftest" runs the built-in checks without opening any
    /// window and returns a process exit code (0 = all passed), which is what
    /// the CI build uses as its test gate.
    /// </summary>
    [STAThread]
    static int Main(string[] args) {
      try {
        Console.OutputEncoding = Encoding.UTF8;
      } catch (Exception) {
        // No usable console (e.g. detached) — output just goes nowhere.
      }

      if (args.Length > 0 && string.Equals(args[0], "--selftest", StringComparison.OrdinalIgnoreCase))
        return SelfTest.Run();

      // Must happen before the first window exists: without it Windows scales
      // the screen coordinates of a "DPI unaware" process, and the capture
      // region and the cursor positions drift apart on scaled displays.
      try {
        Win32.SetProcessDPIAware();
      } catch (Exception) {
        // Very old Windows without the API — scaling at 100% works anyway.
      }

      bool createdNew;
      using (Mutex singleInstance = new Mutex(true, "Local\\DefinetlyNotAFishingBot_SingleInstance", out createdNew)) {
        if (!createdNew) {
          MessageBox.Show(
            "The fishing bot is already running — two instances would fight over the same config and the same game.",
            "Already running", MessageBoxButtons.OK, MessageBoxIcon.Information);
          return 1;
        }

        // A stray exception in a UI handler is logged instead of killing a bot that runs unattended.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += delegate (object sender, ThreadExceptionEventArgs e) {
          Logger.Write("UI exception: " + e.Exception);
          Console.WriteLine("UI exception: " + e.Exception.Message);
        };
        AppDomain.CurrentDomain.UnhandledException += delegate (object sender, UnhandledExceptionEventArgs e) {
          Logger.Write("Fatal exception: " + e.ExceptionObject);
        };

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        frmMain mainWindow = new frmMain();
        new ConsoleInterface(mainWindow).Start();
        Application.Run(mainWindow);
      }
      return 0;
    }
  }
}
