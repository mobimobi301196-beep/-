using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DefinetlyNotAFishingBot {
  public partial class frmCapture : Form {

    public const string DEFAULT_TITLE = "Where is the Fish?!";

    /// <summary>
    /// True once the window is fully up; programmatic placement during startup
    /// must never write the config, only the developer's own moves/resizes do.
    /// </summary>
    private bool doneLoading = false;

    private readonly object interiorLock = new object();
    private Rectangle interiorScreenBounds = Rectangle.Empty;

    /// <summary>
    /// The see-through interior in screen coordinates — exactly what the bot may
    /// capture. Cached on the UI thread (whenever the window is laid out or
    /// moved) so the bot thread can read it without touching any control.
    /// Empty until the first layout.
    /// </summary>
    internal Rectangle InteriorScreenBounds {
      get { lock (interiorLock) { return interiorScreenBounds; } }
    }

    private void UpdateInterior() {
      if (!IsHandleCreated || panel1 == null)
        return;
      Rectangle r = panel1.RectangleToScreen(panel1.ClientRectangle);
      lock (interiorLock) { interiorScreenBounds = r; }
    }

    protected override void OnLayout(LayoutEventArgs levent) {
      base.OnLayout(levent);
      UpdateInterior();
    }

    protected override void OnMove(EventArgs e) {
      base.OnMove(e);
      UpdateInterior();
    }

    public frmCapture() {
      InitializeComponent();
      // The saved position is applied in OnHandleCreated: setting the size
      // before the window handle exists makes WinForms convert it through
      // assumed frame metrics, which drifts the window by a few pixels on
      // every save/load cycle.
      StartPosition = FormStartPosition.Manual;
    }

    protected override void OnHandleCreated(EventArgs e) {
      base.OnHandleCreated(e);
      if (doneLoading)
        return; // A recreated handle must not reset the developer's position.

      // Come back up where the developer last left the window; the position is
      // persisted in the config. Skip it when that spot is no longer on any
      // screen (e.g. after a monitor change), keeping the designer defaults.
      Rectangle saved = Config.CaptureBounds;
      if (SystemInformation.VirtualScreen.IntersectsWith(saved))
        Bounds = saved;
      UpdateInterior();
    }

    protected override void OnShown(EventArgs e) {
      base.OnShown(e);
      doneLoading = true;
      UpdateInterior();
    }

    /// <summary>
    /// Colors the window frame by bot phase, so the developer sees at a glance
    /// what the bot is doing: red = bot off, blue = fishing, green = looting.
    /// </summary>
    internal void ShowPhase(BotPhase phase) {
      switch (phase) {
        case BotPhase.Fishing:
          BackColor = Color.DodgerBlue;
          break;
        case BotPhase.Looting:
          BackColor = Color.LimeGreen;
          break;
        default:
          BackColor = Color.Red;
          break;
      }
    }

    /// <summary>Persists position and size once the developer finished moving or resizing the window.</summary>
    protected override void OnResizeEnd(EventArgs e) {
      base.OnResizeEnd(e);
      SaveBounds();
    }

    protected override void OnFormClosing(FormClosingEventArgs e) {
      // Also save on close so the final position is never lost.
      SaveBounds();
      base.OnFormClosing(e);
    }

    private void SaveBounds() {
      if (!doneLoading)
        return;
      Config.CaptureBounds = Bounds;
      Config.Save();
    }
  }
}
