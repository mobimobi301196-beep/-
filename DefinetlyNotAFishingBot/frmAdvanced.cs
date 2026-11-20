using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DefinetlyNotAFishingBot {

  /// <summary>
  /// The v2 settings that do not fit on the main window: random delay ranges
  /// (the sliders on the main window are the lower bound, the "up to" values
  /// here the upper one), safety stops, run
  /// limits, lure/buff timers and logging. Built in code (no designer file) and
  /// written to the config only when OK is pressed; the running bot reads the
  /// values live, so changes apply to a run in progress.
  /// </summary>
  internal class frmAdvanced : Form {

    private readonly NumericUpDown refishMaxNum = MakeDec();
    private readonly NumericUpDown lootMaxNum = MakeDec();
    private readonly NumericUpDown maxFailsNum = MakeNum(0, 500);
    private readonly NumericUpDown maxLootsNum = MakeNum(0, 1000000);
    private readonly NumericUpDown maxMinutesNum = MakeNum(0, 100000);
    private readonly NumericUpDown lureMinutesNum = MakeNum(1, 120);
    private readonly NumericUpDown buffMinutesNum = MakeNum(1, 120);
    private readonly CheckBox verifyLootChk = new CheckBox();
    private readonly CheckBox alertChk = new CheckBox();
    private readonly CheckBox logChk = new CheckBox();

    private static NumericUpDown MakeNum(int min, int max) {
      return new NumericUpDown { Minimum = min, Maximum = max, Width = 80 };
    }

    private static NumericUpDown MakeDec() {
      return new NumericUpDown { Minimum = 0, Maximum = 120, DecimalPlaces = 1, Increment = 0.1M, Width = 80 };
    }

    internal frmAdvanced() {
      Text = "Advanced settings";
      AutoScaleMode = AutoScaleMode.Font;
      AutoScaleDimensions = new SizeF(6F, 13F);
      FormBorderStyle = FormBorderStyle.FixedDialog;
      MaximizeBox = false;
      MinimizeBox = false;
      ShowInTaskbar = false;
      StartPosition = FormStartPosition.CenterParent;
      ClientSize = new Size(440, 444);

      int y = 14;
      y = AddNumRow("Refish wait: random up to … sec (0 = fixed at the slider value):", refishMaxNum, y);
      y = AddNumRow("Loot hover: random up to … sec (0 = fixed at the slider value):", lootMaxNum, y);
      y += 6;
      y = AddNumRow("Safety stop after this many failed casts in a row (0 = never):", maxFailsNum, y);
      y = AddNumRow("Stop after this many loots (0 = no limit):", maxLootsNum, y);
      y = AddNumRow("Stop after this many minutes (0 = no limit):", maxMinutesNum, y);
      y = AddNumRow("Re-apply lure every … minutes (lures last 10):", lureMinutesNum, y);
      y = AddNumRow("Re-apply buff every … minutes:", buffMinutesNum, y);

      y += 6;
      y = AddCheckRow("Verify loot: a catch only counts when the bobber is gone after the click", verifyLootChk, y);
      y = AddCheckRow("Play a sound when the bot stops by itself", alertChk, y);
      y = AddCheckRow("Write a log file (" + Path.GetFileName(Logger.FilePath) + ")", logChk, y);

      Button openLogBtn = new Button { Text = "Show log file", Location = new Point(14, y + 6), Size = new Size(110, 25) };
      openLogBtn.Click += OpenLogBtn_Click;
      Controls.Add(openLogBtn);
      y += 40;

      Label hint = new Label {
        Text = "When the bot stops by itself it saves a screenshot of what it saw "
          + "(next to the log file) — the quickest way to fix color and overlay position.",
        ForeColor = Color.Gray,
        Location = new Point(14, y),
        Size = new Size(412, 42)
      };
      Controls.Add(hint);

      Button okBtn = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(262, 406), Size = new Size(80, 26) };
      Button cancelBtn = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(348, 406), Size = new Size(80, 26) };
      okBtn.Click += OkBtn_Click;
      Controls.Add(okBtn);
      Controls.Add(cancelBtn);
      AcceptButton = okBtn;
      CancelButton = cancelBtn;

      LoadFromConfig();
    }

    private int AddNumRow(string caption, NumericUpDown num, int y) {
      Label lab = new Label { Text = caption, Location = new Point(14, y + 3), Size = new Size(330, 15) };
      num.Location = new Point(346, y);
      Controls.Add(lab);
      Controls.Add(num);
      return y + 30;
    }

    private int AddCheckRow(string caption, CheckBox chk, int y) {
      chk.Text = caption;
      chk.Location = new Point(14, y);
      chk.Size = new Size(412, 20);
      Controls.Add(chk);
      return y + 26;
    }

    private void LoadFromConfig() {
      refishMaxNum.Value = (decimal)Config.RefishTimeMax;
      lootMaxNum.Value = (decimal)Config.LootTimeMax;
      maxFailsNum.Value = Config.MaxFailsInRow;
      maxLootsNum.Value = Config.MaxLoots;
      maxMinutesNum.Value = Config.MaxMinutes;
      lureMinutesNum.Value = Config.LureIntervalMinutes;
      buffMinutesNum.Value = Config.BuffIntervalMinutes;
      verifyLootChk.Checked = Config.VerifyLoot;
      alertChk.Checked = Config.AlertOnAutoStop;
      logChk.Checked = Config.WriteLogFile;
    }

    private void OkBtn_Click(object sender, EventArgs e) {
      Config.RefishTimeMax = (double)refishMaxNum.Value;
      Config.LootTimeMax = (double)lootMaxNum.Value;
      Config.MaxFailsInRow = (int)maxFailsNum.Value;
      Config.MaxLoots = (int)maxLootsNum.Value;
      Config.MaxMinutes = (int)maxMinutesNum.Value;
      Config.LureIntervalMinutes = (int)lureMinutesNum.Value;
      Config.BuffIntervalMinutes = (int)buffMinutesNum.Value;
      Config.VerifyLoot = verifyLootChk.Checked;
      Config.AlertOnAutoStop = alertChk.Checked;
      Config.WriteLogFile = logChk.Checked;
      Config.Save();
    }

    private void OpenLogBtn_Click(object sender, EventArgs e) {
      try {
        string file = Logger.FilePath;
        if (File.Exists(file))
          Process.Start("explorer.exe", "/select,\"" + file + "\"");
        else
          MessageBox.Show("No log file yet — it is created the first time the bot runs.", "Log file");
      } catch (Exception ex) {
        MessageBox.Show(ex.Message, "Could not open the log");
      }
    }
  }
}
