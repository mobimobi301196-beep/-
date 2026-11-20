using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DefinetlyNotAFishingBot {
  /// <summary>
  /// Captures the screen area framed by the capture overlay window, i.e. only
  /// the see-through interior (the game behind it), never the overlay's own
  /// border. The margins below are just the fallback for the first moments,
  /// before the overlay reports its real interior.
  /// </summary>
  internal class ScreenManager {

    private const int TOP_MARGIN = 34;
    private const int LEFT_MARGIN = 11;
    private const int RIGHT_MARGIN = 11;
    private const int BOTTOM_MARGIN = 11;

    frmCapture myCaptureWindow;

    internal ScreenManager(frmCapture myCaptureWindow) {
      this.myCaptureWindow = myCaptureWindow;
    }

    /// <summary>
    /// The screen rectangle inside the overlay's frame. Also used to map bobber
    /// coordinates from a capture back to absolute screen coordinates. Taken from
    /// the overlay's real see-through panel (correct at any DPI scaling and for
    /// any frame metrics); the hardcoded margins are only the fallback for the
    /// moment before the overlay has been laid out.
    /// </summary>
    internal Rectangle GetCaptureRectangle() {
      Rectangle interior = myCaptureWindow.InteriorScreenBounds;
      if (interior.Width > 0 && interior.Height > 0)
        return interior;

      return new Rectangle(
        myCaptureWindow.Left + LEFT_MARGIN,
        myCaptureWindow.Top + TOP_MARGIN,
        Math.Max(1, myCaptureWindow.Width - LEFT_MARGIN - RIGHT_MARGIN),
        Math.Max(1, myCaptureWindow.Height - TOP_MARGIN - BOTTOM_MARGIN)
      );
    }

    /// <summary>Takes a fresh screenshot of the capture rectangle. The caller owns (and disposes) the bitmap.</summary>
    internal Bitmap GetScreenCapture() {
      Rectangle captureSize = GetCaptureRectangle();

      Bitmap b = new Bitmap(captureSize.Width, captureSize.Height);

      using (Graphics g = Graphics.FromImage(b)) {
        g.CopyFromScreen(captureSize.Location, Point.Empty, captureSize.Size);
      }

      return b;
    }

    /// <summary>
    /// Takes a screenshot of only a sub-region of the capture rectangle (region
    /// given in capture coordinates, clamped to it). Capturing just the small
    /// watch region instead of the whole overlay is what makes the fast bite
    /// sampling possible. The caller owns (and disposes) the bitmap.
    /// </summary>
    internal Bitmap GetScreenCapture(Rectangle region) {
      Rectangle captureRect = GetCaptureRectangle();
      Rectangle bounds = Rectangle.Intersect(new Rectangle(Point.Empty, captureRect.Size), region);
      if (bounds.Width <= 0 || bounds.Height <= 0)
        bounds = new Rectangle(0, 0, 1, 1);

      Bitmap b = new Bitmap(bounds.Width, bounds.Height);

      using (Graphics g = Graphics.FromImage(b)) {
        g.CopyFromScreen(new Point(captureRect.X + bounds.X, captureRect.Y + bounds.Y), Point.Empty, bounds.Size);
      }

      return b;
    }
  }
}
