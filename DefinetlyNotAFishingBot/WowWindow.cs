using System;
using System.Text;
using System.Threading;

namespace DefinetlyNotAFishingBot {
  /// <summary>
  /// Locates the World of Warcraft client window and tracks whether it currently
  /// has the input focus. The 3.3.5a client registers its window under the class
  /// name "GxWindowClassD3d" (older clients use "GxWindowClass"); the window
  /// title "World of Warcraft" is used as a last resort. When the game is
  /// restarted the old handle dies; Find() can simply be called again.
  /// </summary>
  internal class WowWindow {

    private IntPtr handle = IntPtr.Zero;

    private static bool IsWowClass(string className) {
      return className == "GxWindowClassD3d" || className == "GxWindowClass";
    }

    /// <summary>
    /// Tries to locate the WoW client window. With several clients running, the
    /// one that currently has the focus wins, so the developer picks the
    /// client by clicking into it. Returns true when found.
    /// </summary>
    internal bool Find() {
      IntPtr foreground = Win32.GetForegroundWindow();
      if (foreground != IntPtr.Zero) {
        StringBuilder name = new StringBuilder(64);
        if (Win32.GetClassName(foreground, name, name.Capacity) > 0 && IsWowClass(name.ToString())) {
          handle = foreground;
          return true;
        }
      }

      handle = Win32.FindWindow("GxWindowClassD3d", null);
      if (handle == IntPtr.Zero)
        handle = Win32.FindWindow("GxWindowClass", null);
      if (handle == IntPtr.Zero)
        handle = Win32.FindWindow(null, "World of Warcraft");
      return handle != IntPtr.Zero;
    }

    /// <summary>True while the remembered WoW window still exists (false after a crash, restart or disconnect-to-desktop).</summary>
    internal bool IsAlive {
      get { return handle != IntPtr.Zero && Win32.IsWindow(handle); }
    }

    /// <summary>True while the WoW window still exists and is the foreground window.</summary>
    internal bool IsForeground {
      get { return IsAlive && Win32.GetForegroundWindow() == handle; }
    }

    /// <summary>
    /// Actively brings the WoW window to the foreground and verifies it worked,
    /// retrying a few times. Restores the window first if it is minimized, and
    /// taps Alt before each attempt so Windows honors SetForegroundWindow even
    /// when this process is not the foreground process. Returns true when WoW
    /// really has the focus afterwards.
    /// </summary>
    internal bool EnsureForeground() {
      const int ATTEMPTS = 4;
      const int SETTLE_MS = 400;
      for (int attempt = 0; attempt < ATTEMPTS; attempt++) {
        if (IsForeground)
          return true;
        if (handle == IntPtr.Zero)
          return false;
        if (Win32.IsIconic(handle))
          Win32.ShowWindow(handle, Win32.SW_RESTORE);
        InputSender.TapAlt();
        Win32.SetForegroundWindow(handle);
        Thread.Sleep(SETTLE_MS);
      }
      return IsForeground;
    }
  }
}
