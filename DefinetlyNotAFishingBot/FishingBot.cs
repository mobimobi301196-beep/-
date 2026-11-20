using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace DefinetlyNotAFishingBot {

  /// <summary>Coarse bot state, used by the UI to color the window background.</summary>
  internal enum BotState {
    Stopped,
    Running,
    Paused
  }

  /// <summary>What the bot is currently doing, used to color the capture overlay's frame.</summary>
  internal enum BotPhase {
    /// <summary>Bot is not running — the overlay frame is red.</summary>
    Off,
    /// <summary>Casting/watching for the bite — the overlay frame is blue.</summary>
    Fishing,
    /// <summary>A fish bit and is being looted — the overlay frame is green.</summary>
    Looting
  }

  /// <summary>How one bite watch ended.</summary>
  internal enum WatchResult {
    /// <summary>The bobber dipped / vanished into the splash: a fish.</summary>
    Bite,
    /// <summary>The watch time ran out without a bite (or the bobber was too faint to watch).</summary>
    Timeout,
    /// <summary>The picked color matches half the lake — a configuration problem, not bad luck.</summary>
    TooMuchWater,
    /// <summary>The game lost the focus or a stop was requested; says nothing about the fishing itself.</summary>
    Interrupted
  }

  /// <summary>
  /// The actual bot: runs the cast → watch → loot loop on a background thread.
  /// All game interaction is purely pixel and input based (screen capture plus
  /// SendInput), no memory reading and no injection, so the bot needs no
  /// administrator rights as long as WoW itself is not started elevated.
  ///
  /// Start sequence: hide the game UI, equip the fishing outfit (which includes
  /// the pole), apply the lure onto the equipped pole, apply the buff — each
  /// followed by a wait so the apply/cast time and the server round trip
  /// complete before going on.
  ///
  /// Loop: wait the refish time, cast, watch for the bite; on a bite move the
  /// mouse onto the bobber right away, wait the loot time, right-click to loot,
  /// wait for the server, check that the bobber really is gone, then re-apply
  /// lure/buff when their timers say they are about to run out (the real buffs
  /// cannot be read without touching game memory, so timers are the best
  /// available signal).
  ///
  /// v2 safety net: the bot stops itself — and says why — after too many failed
  /// casts in a row, when a loot/time limit is reached, or when the game window
  /// stays gone. A restarted game client is found again automatically.
  ///
  /// The loop pauses automatically whenever the WoW window loses focus, so the
  /// developer can take over at any time by alt-tabbing; giving WoW the focus
  /// again resumes the bot.
  /// </summary>
  internal class FishingBot {

    /// <summary>Applying a lure channels for 5 seconds; wait a little longer to be safe.</summary>
    private const int LURE_APPLY_WAIT_MS = 6500;
    /// <summary>Wait after the buff key so its cast/GCD and the server round trip complete.</summary>
    private const int BUFF_APPLY_WAIT_MS = 3000;
    /// <summary>Wait after the outfit key so the gear swap really is on the server before the lure.</summary>
    private const int OUTFIT_APPLY_WAIT_MS = 4000;
    /// <summary>Time after pressing the fishing key until the bobber has landed and the water has calmed down.</summary>
    private const int CAST_SETTLE_MS = 2500;
    /// <summary>In 3.3.5 a fish bites within ~17 s of the bobber landing; give up and recast after this long.</summary>
    private const int WATCH_TIMEOUT_MS = 25000;
    /// <summary>No fish ever bites this early after the bobber landed — ignore bite signals before then.</summary>
    private const int MIN_WATCH_BEFORE_BITE_MS = 2000;
    /// <summary>
    /// Pause between bobber scans while waiting for the bite. The dip only
    /// lasts a few hundred milliseconds, so the watcher runs at ~25-30 scans
    /// per second — feasible because it only captures the small watch region.
    /// </summary>
    private const int SAMPLE_INTERVAL_MS = 30;
    /// <summary>Number of initial scans averaged into the "calm bobber" baseline.</summary>
    private const int BASELINE_SAMPLES = 5;
    /// <summary>Minimum matching pixels for the bobber to count as "found".</summary>
    private const int MIN_BOBBER_PIXELS = 12;
    /// <summary>The splash swallows the bobber: it is a bite when the match count drops below this fraction of the baseline.</summary>
    private const double BITE_COUNT_FACTOR = 0.5;
    /// <summary>Or the bobber visibly dips: it is a bite when its center moves down this many pixels.</summary>
    private const int BITE_DIP_PIXELS = 8;
    /// <summary>Half size of the square region around the found bobber that is watched for the bite.</summary>
    private const int WATCH_RADIUS = 40;
    /// <summary>Wait after the loot click so the loot reaches the bags before anything else happens.</summary>
    private const int POST_LOOT_WAIT_MS = 1500;
    /// <summary>The game window may stay gone this long (restart, relog) before the bot gives up.</summary>
    private const int WOW_GONE_TIMEOUT_S = 300;
    /// <summary>After a restarted game client is focused, wait this long so the developer can get in-world before any key is sent.</summary>
    private const int REACQUIRE_GRACE_MS = 10000;
    /// <summary>Safety margin (px) the click point keeps from the capture edge, so it never hits the overlay frame.</summary>
    private const int CLICK_EDGE_MARGIN = 2;

    private readonly ScreenManager screenManager;
    private readonly WowWindow wowWindow = new WowWindow();
    private readonly Random random = new Random();

    private Thread botThread = null;
    private volatile bool stopRequested = false;
    private volatile bool windowReacquired = false;
    private string stopReason = null;
    private DateTime lastLureTime = DateTime.MinValue;
    private DateTime lastBuffTime = DateTime.MinValue;
    private DateTime startTime = DateTime.MinValue;
    private DateTime endTime = DateTime.MinValue;
    private int fishCaught = 0;
    private int casts = 0;
    private int bites = 0;
    private int failedLoots = 0;
    private int failStreak = 0;
    private bool uiHidden = false;

    private readonly object markerLock = new object();
    private Rectangle bobberMarker = Rectangle.Empty;

    /// <summary>
    /// The region (in capture coordinates) the bot currently believes the bobber
    /// is in; Rectangle.Empty while no bobber is being watched. The preview in
    /// frmMain draws this so the developer can see what the bot is looking at.
    /// </summary>
    internal Rectangle BobberMarker {
      get { lock (markerLock) { return bobberMarker; } }
    }

    private void SetBobberMarker(Rectangle region) {
      lock (markerLock) { bobberMarker = region; }
    }

    /// <summary>Raised from the bot thread whenever there is a new status message to show.</summary>
    internal event Action<string> StatusChanged;
    /// <summary>Raised from the bot thread whenever the coarse state (running/paused/stopped) changes.</summary>
    internal event Action<BotState> StateChanged;
    /// <summary>Raised from the bot thread whenever the activity phase (off/fishing/looting) changes.</summary>
    internal event Action<BotPhase> PhaseChanged;
    /// <summary>Raised from the bot thread whenever a fish was looted, with the total of this run.</summary>
    internal event Action<int> CaughtChanged;
    /// <summary>Raised from the bot thread when the loop has terminated for any reason.</summary>
    internal event Action Stopped;

    /// <summary>True while the bot thread is alive.</summary>
    internal bool IsRunning {
      get { return botThread != null && botThread.IsAlive; }
    }

    /// <summary>Why the bot stopped on its own (null for a stop the developer asked for).</summary>
    internal string StopReason {
      get { return stopReason; }
    }

    #region Statistics
    /// <summary>Verified loots of this run.</summary>
    internal int Loots { get { return fishCaught; } }
    /// <summary>Loot clicks after which the bobber was still there.</summary>
    internal int FailedLoots { get { return failedLoots; } }
    /// <summary>Number of casts of this run.</summary>
    internal int Casts { get { return casts; } }

    /// <summary>Runtime of this run; stops counting when the bot has stopped.</summary>
    internal TimeSpan Elapsed {
      get {
        if (startTime == DateTime.MinValue)
          return TimeSpan.Zero;
        DateTime end = endTime == DateTime.MinValue ? DateTime.UtcNow : endTime;
        return end - startTime;
      }
    }

    /// <summary>Two-line summary for the UI and the console: loots, misses, loots per hour, runtime.</summary>
    internal string StatsText() {
      TimeSpan elapsed = Elapsed;
      double perHour = elapsed.TotalSeconds >= 60 ? fishCaught / elapsed.TotalHours : 0;
      string misses = failedLoots > 0 ? " (missed " + failedLoots + ")" : "";
      return "Loots: " + fishCaught + misses + "\n"
        + perHour.ToString("0.0") + "/h   "
        + ((int)elapsed.TotalHours).ToString("00") + ":" + elapsed.Minutes.ToString("00") + ":" + elapsed.Seconds.ToString("00");
    }
    #endregion

    /// <param name="screenManager">Provides the capture region defined by the overlay window.</param>
    internal FishingBot(ScreenManager screenManager) {
      this.screenManager = screenManager;
    }

    /// <summary>Starts the bot loop on a background thread. Does nothing if it is already running.</summary>
    internal void Start() {
      if (IsRunning)
        return;
      stopRequested = false;
      stopReason = null;
      fishCaught = 0;
      casts = 0;
      bites = 0;
      failedLoots = 0;
      failStreak = 0;
      startTime = DateTime.UtcNow;
      endTime = DateTime.MinValue;
      botThread = new Thread(RunLoop) { IsBackground = true, Name = "FishingBot" };
      botThread.Start();
    }

    /// <summary>Signals the bot loop to stop and waits briefly for the thread to end.</summary>
    internal void Stop() {
      stopRequested = true;
      if (botThread != null && botThread.IsAlive)
        botThread.Join(3000);
    }

    /// <summary>
    /// Stops the loop from inside the bot thread and remembers why, so the UI
    /// can show the reason instead of a bare "Stopped.". Optionally keeps a
    /// screenshot of what the bot saw — the fastest way to tune color and region.
    /// </summary>
    private void AutoStop(string reason, bool keepScreenshot) {
      stopReason = reason;
      Status("Stopping: " + reason);
      if (keepScreenshot)
        SaveDebugShot();
      if (Config.AlertOnAutoStop) {
        try { System.Media.SystemSounds.Exclamation.Play(); } catch (Exception) { }
      }
      stopRequested = true;
    }

    private void SaveDebugShot() {
      try {
        string file = Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
          ".definetly_not_a_fishing_bot_debug_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
        using (Bitmap shot = screenManager.GetScreenCapture()) {
          shot.Save(file, ImageFormat.Png);
        }
        Status("Screenshot of what the bot saw: " + file);
      } catch (Exception) {
        // The screenshot is a courtesy; failing to write it changes nothing.
      }
    }

    /// <summary>True (and the bot is stopping) when the loot or time limit of this run is reached.</summary>
    private bool LimitReached() {
      if (Config.MaxLoots > 0 && fishCaught >= Config.MaxLoots) {
        AutoStop("loot limit reached (" + fishCaught + ")", false);
        return true;
      }
      if (Config.MaxMinutes > 0 && Elapsed.TotalMinutes >= Config.MaxMinutes) {
        AutoStop("time limit reached (" + Config.MaxMinutes + " min)", false);
        return true;
      }
      return false;
    }

    /// <summary>
    /// Counts one failed cast; stops the bot when too many failed in a row
    /// (a bot that keeps casting into nothing helps nobody).
    /// </summary>
    private void RegisterFail(string what) {
      failStreak++;
      int max = Config.MaxFailsInRow;
      Status(CaughtPrefix() + what + (max > 0 ? " [fail " + failStreak + "/" + max + "]" : ""));
      if (max > 0 && failStreak >= max)
        AutoStop(failStreak + " failed casts in a row — check overlay position, bobber color, bags and fishing spot", true);
    }

    /// <summary>The complete bot life cycle: find WoW, prepare once, then fish until stopped.</summary>
    private void RunLoop() {
      try {
        Status("Fishing bot v2 started.");
        if (!wowWindow.Find()) {
          Status("WoW window not found — is the game running?");
          return;
        }

        ChangeState(BotState.Running);
        ChangePhase(BotPhase.Fishing);
        Status("Bringing WoW to the foreground…");
        if (!wowWindow.EnsureForeground()) {
          Status("Could not focus WoW — click into the game window…");
          if (!WaitForWowFocus())
            return;
        }
        SleepChecked(1000);

        PrepareSequence();

        while (!stopRequested) {
          if (LimitReached())
            break;

          SetBobberMarker(Rectangle.Empty);

          int refishMs = PickDelayMs(Config.RefishTime, Config.RefishTimeMax, random);
          Status(CaughtPrefix() + "Waiting to refish (" + (refishMs / 1000.0).ToString("0.0") + " s)…");
          SleepChecked(refishMs);
          if (stopRequested)
            break;
          if (!WaitForWowFocus())
            break;
          if (windowReacquired) {
            // The game client was restarted meanwhile: its UI, outfit and buffs
            // are back to normal, so the start sequence has to run again.
            windowReacquired = false;
            uiHidden = false;
            Status("WoW is back — getting ready again…");
            SleepChecked(REACQUIRE_GRACE_MS);
            if (stopRequested || !WaitForWowFocus())
              break;
            PrepareSequence();
            continue;
          }

          // Park the mouse outside the capture area first: if the bobber lands
          // under the cursor, the mouse-over glow brightens it and the color
          // detection fails.
          MoveMouseAside();

          Status(CaughtPrefix() + "Casting… [key: " + Config.FishingKey + "]");
          if (!wowWindow.IsForeground && !WaitForWowFocus())
            break;
          casts++;
          InputSender.PressKey(Config.FishingKey);
          SleepChecked(CAST_SETTLE_MS);
          if (stopRequested)
            break;

          BobberSample bobber = ScanForBobber();
          if (bobber.MatchCount < MIN_BOBBER_PIXELS) {
            RegisterFail("Bobber not found (" + bobber.MatchCount + " px matched) — recasting…");
            continue; // The loop restarts with the refish wait.
          }

          Point clickPoint;
          WatchResult watch = WatchForBite(bobber, out clickPoint);
          if (watch == WatchResult.Bite) {
            bites++;
            // Move onto the bobber right away, then give the loot timer time to
            // pass (the client registers the hover meanwhile), then loot.
            ChangePhase(BotPhase.Looting);
            Status(CaughtPrefix() + "Bite! Moving onto the bobber…");
            MoveToBobber(clickPoint);
            SleepChecked(PickDelayMs(Config.LootTime, Config.LootTimeMax, random));
            if (stopRequested)
              break;
            if (!wowWindow.IsForeground) {
              ChangePhase(BotPhase.Fishing);
              continue; // Focus was taken over mid-loot — skip the click.
            }
            InputSender.RightClick();
            Status(CaughtPrefix() + "Looted — waiting for the server…");
            SleepChecked(POST_LOOT_WAIT_MS);

            if (Config.VerifyLoot && BobberStillVisible(clickPoint)) {
              failedLoots++;
              RegisterFail("Click did not loot — the bobber is still there (bags full? Auto Loot off?)");
            } else {
              fishCaught++;
              failStreak = 0;
              CaughtChanged?.Invoke(fishCaught);
              Status(CaughtPrefix() + "Fish #" + fishCaught + " in the bag.");
            }
            ChangePhase(BotPhase.Fishing);
          } else if (watch == WatchResult.TooMuchWater) {
            RegisterFail("Color matches too much water — lower the tolerance or re-pick the bobber color");
          } else if (watch == WatchResult.Timeout) {
            RegisterFail("No bite — recasting…");
          }
          // WatchResult.Interrupted: focus lost or stop requested — not a failed cast.

          // Keep lure and buff alive before the next cast.
          if (!stopRequested && (DateTime.UtcNow - lastLureTime).TotalMinutes >= Config.LureIntervalMinutes) {
            PressAndWait(Config.LureKey, CaughtPrefix() + "Re-applying lure", LURE_APPLY_WAIT_MS);
            lastLureTime = DateTime.UtcNow;
          }
          if (!stopRequested && (DateTime.UtcNow - lastBuffTime).TotalMinutes >= Config.BuffIntervalMinutes) {
            PressAndWait(Config.BuffKey, CaughtPrefix() + "Re-applying buff", BUFF_APPLY_WAIT_MS);
            lastBuffTime = DateTime.UtcNow;
          }
        }

        Status(stopReason != null ? "Stopped: " + stopReason : "Stopped.");
      } catch (Exception ex) {
        Logger.Write("Bot loop crashed: " + ex);
        Status("Error: " + ex.Message);
      } finally {
        endTime = DateTime.UtcNow;
        // Bring the game UI back; skipped (with a hint) when WoW lost the
        // focus meanwhile, so the combo cannot land in another window.
        if (uiHidden) {
          if (wowWindow.IsForeground)
            ToggleGameUi("Restoring the game UI");
          else
            Status("Game UI is still hidden — press Alt+Y in WoW to bring it back.");
        }
        Status("Run summary: " + StatsText().Replace("\n", " | ") + " | casts " + casts + ", bites " + bites);
        SetBobberMarker(Rectangle.Empty);
        ChangePhase(BotPhase.Off);
        ChangeState(BotState.Stopped);
        Stopped?.Invoke();
      }
    }

    /// <summary>
    /// A delay in milliseconds: fixed at minSeconds, or — when maxSeconds is
    /// above it — a random value between the two (both ends included).
    /// </summary>
    internal static int PickDelayMs(int minSeconds, double maxSeconds, Random random) {
      int minMs = minSeconds * 1000;
      int maxMs = (int)Math.Round(maxSeconds * 1000);
      if (maxMs <= minMs)
        return minMs;
      return random.Next(minMs, maxMs + 1);
    }

    /// <summary>
    /// Hide the game UI (Alt+Y) so no interface element can cover the bobber or
    /// disturb the pixel detection (restored when the bot stops), then outfit →
    /// lure → buff. The order matters: the outfit equips the fishing pole, only
    /// then can the lure be applied onto it. Keys set to None are skipped.
    /// </summary>
    private void PrepareSequence() {
      if (Config.HideGameUi && !uiHidden)
        ToggleGameUi("Hiding the game UI");

      PressAndWait(Config.OutfitKey, "Equipping fishing outfit", OUTFIT_APPLY_WAIT_MS);
      PressAndWait(Config.LureKey, "Applying lure", LURE_APPLY_WAIT_MS);
      lastLureTime = DateTime.UtcNow;
      PressAndWait(Config.BuffKey, "Applying buff", BUFF_APPLY_WAIT_MS);
      lastBuffTime = DateTime.UtcNow;
    }

    /// <summary>Captures the overlay region once and searches the whole image for the bobber.</summary>
    private BobberSample ScanForBobber() {
      using (Bitmap shot = screenManager.GetScreenCapture()) {
        return BobberDetector.FindBobber(shot, Config.BobberColor, Config.ColorTollerance);
      }
    }

    /// <summary>
    /// After the loot click: is the bobber still visible around the spot we
    /// clicked? A looted bobber disappears, so "still there" means the click
    /// did not loot anything.
    /// </summary>
    private bool BobberStillVisible(Point captureCoordinate) {
      Rectangle region = new Rectangle(
        captureCoordinate.X - WATCH_RADIUS,
        captureCoordinate.Y - WATCH_RADIUS,
        WATCH_RADIUS * 2,
        WATCH_RADIUS * 2
      );
      using (Bitmap shot = screenManager.GetScreenCapture(region)) {
        BobberSample sample = BobberDetector.Scan(shot, new Rectangle(Point.Empty, shot.Size), Config.BobberColor, Config.ColorTollerance);
        return sample.MatchCount >= MIN_BOBBER_PIXELS;
      }
    }

    /// <summary>
    /// Watches a small region around the found bobber until the bite is detected
    /// (match count collapses into the splash, or the bobber center dips down),
    /// the timeout expires, the WoW window loses focus, or a stop is requested.
    /// clickPoint is the last position the bobber was clearly seen at (it drifts
    /// slightly while waiting), in capture coordinates.
    /// </summary>
    private WatchResult WatchForBite(BobberSample initial, out Point clickPoint) {
      clickPoint = initial.Centroid;
      Rectangle captureArea = new Rectangle(Point.Empty, screenManager.GetCaptureRectangle().Size);
      Rectangle watchRegion = Rectangle.Intersect(
        new Rectangle(
          initial.Centroid.X - WATCH_RADIUS,
          initial.Centroid.Y - WATCH_RADIUS,
          WATCH_RADIUS * 2,
          WATCH_RADIUS * 2
        ),
        captureArea
      );
      if (watchRegion.Width <= 0 || watchRegion.Height <= 0)
        return WatchResult.Timeout;

      SetBobberMarker(watchRegion);

      int samples = 0;
      int countSum = 0;
      int baselineCount = 0;
      int baselineY = initial.Centroid.Y;
      int biteStreak = 0;
      DateTime watchStart = DateTime.UtcNow;

      Status(CaughtPrefix() + "Bobber at (" + initial.Centroid.X + ", " + initial.Centroid.Y + "), "
        + initial.MatchCount + " px — watching…");

      while (!stopRequested && (DateTime.UtcNow - watchStart).TotalMilliseconds < WATCH_TIMEOUT_MS) {
        if (!wowWindow.IsForeground)
          return WatchResult.Interrupted;

        // Capture only the small watch region — a full-overlay capture per
        // sample would throttle the loop to a few frames per second.
        BobberSample sample;
        using (Bitmap shot = screenManager.GetScreenCapture(watchRegion)) {
          sample = BobberDetector.Scan(shot, new Rectangle(Point.Empty, shot.Size), Config.BobberColor, Config.ColorTollerance);
        }
        if (sample.MatchCount > 0)
          sample.Centroid = new Point(sample.Centroid.X + watchRegion.X, sample.Centroid.Y + watchRegion.Y);

        samples++;
        if (sample.MatchCount >= MIN_BOBBER_PIXELS)
          clickPoint = sample.Centroid;
        if (samples <= BASELINE_SAMPLES) {
          // Build the "calm bobber" baseline from the first few scans.
          countSum += sample.MatchCount;
          baselineCount = countSum / samples;
          if (sample.MatchCount > 0)
            baselineY = sample.Centroid.Y;
          if (samples == BASELINE_SAMPLES) {
            // Sanity check: when a large part of the watch region matches, the
            // picked color matches the environment and every ripple would look
            // like a bite — better to tell the developer than to click wildly.
            int regionArea = watchRegion.Width * watchRegion.Height;
            if (baselineCount > regionArea / 2)
              return WatchResult.TooMuchWater;
            Status(CaughtPrefix() + "Watching… (baseline " + baselineCount + " px)");
          }
        } else {
          if (baselineCount < MIN_BOBBER_PIXELS)
            return WatchResult.Timeout; // The bobber was too faint to watch reliably.
          bool biteSignal = sample.MatchCount < baselineCount * BITE_COUNT_FACTOR
            || (sample.MatchCount > 0 && sample.Centroid.Y - baselineY >= BITE_DIP_PIXELS);
          // Two consecutive samples must agree, so single-frame noise (ripples,
          // light flicker) cannot fake a bite — and no signal counts during the
          // first seconds, because no fish bites right after the cast.
          biteStreak = biteSignal ? biteStreak + 1 : 0;
          if (biteStreak >= 2 && (DateTime.UtcNow - watchStart).TotalMilliseconds >= MIN_WATCH_BEFORE_BITE_MS)
            return WatchResult.Bite;
        }

        SleepChecked(SAMPLE_INTERVAL_MS);
      }

      return stopRequested ? WatchResult.Interrupted : WatchResult.Timeout;
    }

    /// <summary>
    /// Parks the cursor just right of the capture area (or left, when the right
    /// side would leave the screen), so no mouse-over glow can touch the bobber.
    /// </summary>
    private void MoveMouseAside() {
      Rectangle captureRect = screenManager.GetCaptureRectangle();
      Rectangle screen = SystemInformation.VirtualScreen;
      int x = captureRect.Right + 40;
      if (x > screen.Right - 2)
        x = Math.Max(screen.Left + 2, captureRect.Left - 40);
      int y = captureRect.Y + captureRect.Height / 2;
      InputSender.MoveCursor(new Point(x, y));
      SleepChecked(150);
    }

    /// <summary>
    /// Moves the cursor onto the bobber (capture coordinates mapped to the
    /// screen). The picked color usually sits off-center on the bobber, so the
    /// point is nudged 5 px up-left to reliably hit it — clamped into the
    /// capture area, because outside of it the click would land on the overlay
    /// window's frame instead of passing through into the game.
    /// </summary>
    private void MoveToBobber(Point captureCoordinate) {
      Rectangle captureRect = screenManager.GetCaptureRectangle();
      int x = captureRect.X + captureCoordinate.X - 5;
      int y = captureRect.Y + captureCoordinate.Y - 5;
      x = Math.Max(captureRect.Left + CLICK_EDGE_MARGIN, Math.Min(captureRect.Right - 1 - CLICK_EDGE_MARGIN, x));
      y = Math.Max(captureRect.Top + CLICK_EDGE_MARGIN, Math.Min(captureRect.Bottom - 1 - CLICK_EDGE_MARGIN, y));
      InputSender.MoveCursor(new Point(x, y));
    }

    /// <summary>
    /// Presses one of the utility keys and waits; keys set to None are skipped
    /// entirely. Blocks until WoW really has the focus — re-checked immediately
    /// before the press — so the key press cannot land in another window.
    /// </summary>
    private void PressAndWait(Keys key, string statusText, int waitMs) {
      if (key == Keys.None || stopRequested)
        return;
      if (!WaitForWowFocus())
        return;
      Status(statusText + "… [key: " + key + ", then waiting " + (waitMs / 1000.0).ToString("0.#") + "s]");
      if (!wowWindow.IsForeground && !WaitForWowFocus())
        return;
      InputSender.PressKey(key);
      SleepChecked(waitMs);
    }

    /// <summary>
    /// Toggles the WoW interface with Alt+Y so nothing covers the bobber; only
    /// sent while WoW has the focus. Tracks the toggle so the UI is restored
    /// exactly once when the bot stops.
    /// </summary>
    private void ToggleGameUi(string statusText) {
      if (!wowWindow.IsForeground)
        return;
      Status(statusText + "… [Alt+Y]");
      InputSender.PressAltCombo(Keys.Y);
      Thread.Sleep(400);
      uiHidden = !uiHidden;
    }

    /// <summary>
    /// Blocks while the WoW window does not have the focus, so the developer can
    /// take over at any time. When the window is gone altogether (crash,
    /// restart, disconnect to desktop) it keeps looking for a new one and gives
    /// up after a few minutes. Returns false when the bot is stopping.
    /// </summary>
    private bool WaitForWowFocus() {
      if (wowWindow.IsForeground)
        return true;
      ChangeState(BotState.Paused);
      Status(CaughtPrefix() + "Paused — click into the WoW window to continue…");
      DateTime goneSince = DateTime.MinValue;
      while (!stopRequested && !wowWindow.IsForeground) {
        if (!wowWindow.IsAlive) {
          if (goneSince == DateTime.MinValue) {
            goneSince = DateTime.UtcNow;
            Status(CaughtPrefix() + "WoW window is gone — waiting for the game to come back…");
          }
          if (wowWindow.Find()) {
            goneSince = DateTime.MinValue;
            windowReacquired = true;
            Status(CaughtPrefix() + "WoW window found again — click into it to continue…");
          } else if ((DateTime.UtcNow - goneSince).TotalSeconds >= WOW_GONE_TIMEOUT_S) {
            AutoStop("WoW window gone for " + (WOW_GONE_TIMEOUT_S / 60) + " minutes (crash or disconnect?)", false);
            return false;
          }
        }
        Thread.Sleep(250);
      }
      if (stopRequested)
        return false;
      ChangeState(BotState.Running);
      Status(CaughtPrefix() + "WoW focused again — resuming…");
      SleepChecked(750);
      return true;
    }

    /// <summary>Sleeps in small slices so a stop request is honored quickly.</summary>
    private void SleepChecked(int milliseconds) {
      const int SLICE_MS = 50;
      int remaining = milliseconds;
      while (remaining > 0 && !stopRequested) {
        int slice = Math.Min(SLICE_MS, remaining);
        Thread.Sleep(slice);
        remaining -= slice;
      }
    }

    /// <summary>Status prefix showing the catch counter once the first fish is in the bag.</summary>
    private string CaughtPrefix() {
      return fishCaught > 0 ? "Caught " + fishCaught + " | " : "";
    }

    /// <summary>Logs and raises the status event; the UI marshals it onto its own thread.</summary>
    private void Status(string text) {
      Logger.Write(text);
      StatusChanged?.Invoke(text);
    }

    /// <summary>Raises the state event; the UI marshals it onto its own thread.</summary>
    private void ChangeState(BotState state) {
      StateChanged?.Invoke(state);
    }

    /// <summary>Raises the phase event; the UI marshals it onto its own thread.</summary>
    private void ChangePhase(BotPhase phase) {
      PhaseChanged?.Invoke(phase);
    }
  }
}
