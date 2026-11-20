using System;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using IO = System.IO;

namespace DefinetlyNotAFishingBot {
  internal class Config {

    #region Initialisation
    private const string FILE_NAME = ".definetly_not_a_fishing_bot_config.json";

    private static Config MySelf = null;

    private static string FileName() {
      string path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

      path = IO.Path.Combine(path, FILE_NAME);

      return path;
    }

    /// <summary>
    /// Loads the config file. A missing, empty or corrupt file never stops the
    /// application: a corrupt file is moved aside (".broken") and defaults are used.
    /// </summary>
    internal static void Reload() {
      string fileName = FileName();
      Config loaded = null;

      if (IO.File.Exists(fileName)) {
        try {
          string json = Encoding.UTF8.GetString(IO.File.ReadAllBytes(fileName));
          loaded = ParseAndValidate(json);
        } catch (Exception) {
          loaded = null;
        }
        if (loaded == null)
          MoveBrokenFileAside(fileName);
      }

      if (loaded == null) {
        MySelf = new Config();
        Save();
      } else {
        MySelf = loaded;
      }
    }

    private static void MoveBrokenFileAside(string fileName) {
      try {
        string broken = fileName + ".broken";
        if (IO.File.Exists(broken))
          IO.File.Delete(broken);
        IO.File.Move(fileName, broken);
      } catch (Exception) {
        // Nothing sensible left to do; the defaults overwrite the file on Save().
      }
    }

    /// <summary>
    /// Parses a config JSON and clamps every value into its valid range.
    /// Returns null when the text is not a usable config. Touches no file.
    /// </summary>
    internal static Config ParseAndValidate(string json) {
      Config parsed;
      try {
        parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<Config>(json);
      } catch (Exception) {
        return null;
      }
      if (parsed == null)
        return null;
      parsed.Validate();
      return parsed;
    }

    internal static void Save() {
      if (MySelf == null) {
        MySelf = new Config();
      }

      try {
        string jj = Newtonsoft.Json.JsonConvert.SerializeObject(MySelf, Newtonsoft.Json.Formatting.Indented);

        string fileName = FileName();
        string tmpName = fileName + ".tmp";

        // Write to a temp file first, so a crash mid-write cannot leave a half-written config behind.
        IO.File.WriteAllBytes(tmpName, Encoding.UTF8.GetBytes(jj));
        if (IO.File.Exists(fileName))
          IO.File.Delete(fileName);
        IO.File.Move(tmpName, fileName);
      } catch (Exception) {
        // A locked or read-only profile file must not crash the UI; settings just stay in memory.
      }
    }

    internal static void ResetToDefaults() {
      MySelf = new Config();
      Save();
    }

    static Config() {
      Reload();
    }

    private static int Clamp(int value, int min, int max) {
      return Math.Max(min, Math.Min(max, value));
    }

    private void Validate() {
      _RefishTime = Clamp(_RefishTime, 0, 60);
      _LootTime = Clamp(_LootTime, 0, 60);
      _ColorTollerance = Clamp(_ColorTollerance, 0, 255);
      _RefishTimeMax = Math.Max(0, Math.Min(120, _RefishTimeMax));
      _LootTimeMax = Math.Max(0, Math.Min(120, _LootTimeMax));
      _MaxFailsInRow = Clamp(_MaxFailsInRow, 0, 500);
      _MaxLoots = Clamp(_MaxLoots, 0, 1000000);
      _MaxMinutes = Clamp(_MaxMinutes, 0, 100000);
      _LureIntervalMinutes = Clamp(_LureIntervalMinutes, 1, 120);
      _BuffIntervalMinutes = Clamp(_BuffIntervalMinutes, 1, 120);
      if (_CaptureWidth < 20 || _CaptureHeight < 20) {
        _CaptureWidth = 800;
        _CaptureHeight = 450;
      }
    }

    /// <summary>True when every numeric setting is inside its valid range (used by the self test).</summary>
    internal bool IsValid() {
      return _RefishTime >= 0 && _RefishTime <= 60
        && _LootTime >= 0 && _LootTime <= 60
        && _ColorTollerance >= 0 && _ColorTollerance <= 255
        && _RefishTimeMax >= 0 && _RefishTimeMax <= 120
        && _LootTimeMax >= 0 && _LootTimeMax <= 120
        && _MaxFailsInRow >= 0 && _MaxLoots >= 0 && _MaxMinutes >= 0
        && _LureIntervalMinutes >= 1 && _BuffIntervalMinutes >= 1
        && _CaptureWidth >= 20 && _CaptureHeight >= 20;
    }
    #endregion


    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    [Newtonsoft.Json.JsonProperty("RefishTime")]
    private int _RefishTime = 3;
    /// <summary>
    /// Time in seconds the bot will wait after looting before refishing.
    /// </summary>
    public static int RefishTime {
      get {
        return MySelf._RefishTime;
      }
      set {
        MySelf._RefishTime = value;
      }
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    [Newtonsoft.Json.JsonProperty("LootTime")]
    private int _LootTime = 1;
    /// <summary>
    /// Time in seconds the bot will wait after a catch before looting.
    /// </summary>
    public static int LootTime {
      get {
        return MySelf._LootTime;
      }
      set {
        MySelf._LootTime = value;
      }
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    [Newtonsoft.Json.JsonProperty("FishingKey")]
    private Keys _FishingKey = Keys.D2;
    /// <summary>
    /// Key used to fish
    /// </summary>
    public static Keys FishingKey {
      get {
        return MySelf._FishingKey;
      }
      set {
        MySelf._FishingKey = value;
      }
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    [Newtonsoft.Json.JsonProperty("LureKey")]
    private Keys _LureKey = Keys.Oem6;
    /// <summary>
    /// The Key used to apply a Lure.
    /// </summary>
    public static Keys LureKey {
      get {
        return MySelf._LureKey;
      }
      set {
        MySelf._LureKey = value;
      }
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    [Newtonsoft.Json.JsonProperty("BuffKey")]
    private Keys _BuffKey = Keys.Oem4;
    /// <summary>
    /// The key used the apply a fishing buff
    /// </summary>
    public static Keys BuffKey {
      get {
        return MySelf._BuffKey;
      }
      set {
        MySelf._BuffKey = value;
      }
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    [Newtonsoft.Json.JsonProperty("OutfitKey")]
    private Keys _OutfitKey = Keys.D0;
    /// <summary>
    /// The key used to change into the right outfit.
    /// </summary>
    public static Keys OutfitKey {
      get {
        return MySelf._OutfitKey;
      }
      set {
        MySelf._OutfitKey = value;
      }
    }

    // The JSON name keeps its historic spelling so existing config files stay valid.
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    [Newtonsoft.Json.JsonProperty("ColorTollerance")]
    private int _ColorTollerance = 20;
    /// <summary>
    /// Per-channel tolerance (0-255) when matching pixels against the bobber color.
    /// </summary>
    public static int ColorTollerance {
      get {
        return MySelf._ColorTollerance;
      }
      set {
        MySelf._ColorTollerance = value;
      }
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    [Newtonsoft.Json.JsonProperty("BobberColorArgb")]
    private int _BobberColorArgb = unchecked((int)0xFF5C3227);
    /// <summary>
    /// The bobber color the developer picked from the capture preview, stored as
    /// an ARGB value. Zero means no color has been picked yet; defaults to the
    /// bobber feather color (#5C3227) picked on the dev machine.
    /// </summary>
    public static Color BobberColor {
      get {
        return Color.FromArgb(MySelf._BobberColorArgb);
      }
      set {
        MySelf._BobberColorArgb = value.ToArgb();
      }
    }

    /// <summary>True once the developer has picked a bobber color.</summary>
    public static bool HasBobberColor {
      get {
        return MySelf._BobberColorArgb != 0;
      }
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    [Newtonsoft.Json.JsonProperty("HideGameUi")]
    private bool _HideGameUi = true;
    /// <summary>
    /// Whether the bot hides the WoW interface with Alt+Y while it runs, so no
    /// UI element can cover the bobber or disturb the pixel detection.
    /// </summary>
    public static bool HideGameUi {
      get {
        return MySelf._HideGameUi;
      }
      set {
        MySelf._HideGameUi = value;
      }
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    [Newtonsoft.Json.JsonProperty("CaptureX")]
    private int _CaptureX = 1086;
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    [Newtonsoft.Json.JsonProperty("CaptureY")]
    private int _CaptureY = 159;
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    [Newtonsoft.Json.JsonProperty("CaptureWidth")]
    private int _CaptureWidth = 3140;
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    [Newtonsoft.Json.JsonProperty("CaptureHeight")]
    private int _CaptureHeight = 820;
    /// <summary>
    /// Position and size of the capture overlay window. Updated whenever the
    /// developer moves or resizes the overlay, so it comes back up in the same
    /// spot next time.
    /// </summary>
    public static Rectangle CaptureBounds {
      get {
        return new Rectangle(MySelf._CaptureX, MySelf._CaptureY, MySelf._CaptureWidth, MySelf._CaptureHeight);
      }
      set {
        MySelf._CaptureX = value.X;
        MySelf._CaptureY = value.Y;
        MySelf._CaptureWidth = value.Width;
        MySelf._CaptureHeight = value.Height;
      }
    }

    // ---- v2 settings (edited in the "Advanced…" dialog) ----

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    [Newtonsoft.Json.JsonProperty("RefishTimeMax")]
    private double _RefishTimeMax = 0;
    /// <summary>
    /// Upper bound in seconds (decimals allowed) for the wait before each cast.
    /// The actual wait is picked at random between RefishTime and this value
    /// every cast. 0, or anything not above RefishTime, means a fixed wait.
    /// </summary>
    public static double RefishTimeMax {
      get { return MySelf._RefishTimeMax; }
      set { MySelf._RefishTimeMax = value; }
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    [Newtonsoft.Json.JsonProperty("LootTimeMax")]
    private double _LootTimeMax = 0;
    /// <summary>
    /// Upper bound in seconds (decimals allowed) for the hover time before the
    /// loot click; picked at random between LootTime and this value every
    /// catch. 0, or anything not above LootTime, means a fixed time.
    /// </summary>
    public static double LootTimeMax {
      get { return MySelf._LootTimeMax; }
      set { MySelf._LootTimeMax = value; }
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    [Newtonsoft.Json.JsonProperty("MaxFailsInRow")]
    private int _MaxFailsInRow = 15;
    /// <summary>
    /// The bot stops itself (and says why) after this many failed casts in a
    /// row — bobber not found, no bite, or a click that looted nothing.
    /// 0 turns the safety stop off.
    /// </summary>
    public static int MaxFailsInRow {
      get { return MySelf._MaxFailsInRow; }
      set { MySelf._MaxFailsInRow = value; }
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    [Newtonsoft.Json.JsonProperty("MaxLoots")]
    private int _MaxLoots = 0;
    /// <summary>Stop after this many successful loots in one run; 0 = no limit.</summary>
    public static int MaxLoots {
      get { return MySelf._MaxLoots; }
      set { MySelf._MaxLoots = value; }
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    [Newtonsoft.Json.JsonProperty("MaxMinutes")]
    private int _MaxMinutes = 0;
    /// <summary>Stop after this many minutes of runtime; 0 = no limit.</summary>
    public static int MaxMinutes {
      get { return MySelf._MaxMinutes; }
      set { MySelf._MaxMinutes = value; }
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    [Newtonsoft.Json.JsonProperty("LureIntervalMinutes")]
    private int _LureIntervalMinutes = 9;
    /// <summary>Minutes between lure re-applications (lures last 10 minutes).</summary>
    public static int LureIntervalMinutes {
      get { return MySelf._LureIntervalMinutes; }
      set { MySelf._LureIntervalMinutes = value; }
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    [Newtonsoft.Json.JsonProperty("BuffIntervalMinutes")]
    private int _BuffIntervalMinutes = 9;
    /// <summary>Minutes between buff re-applications; set it a bit below the real buff duration.</summary>
    public static int BuffIntervalMinutes {
      get { return MySelf._BuffIntervalMinutes; }
      set { MySelf._BuffIntervalMinutes = value; }
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    [Newtonsoft.Json.JsonProperty("VerifyLoot")]
    private bool _VerifyLoot = true;
    /// <summary>
    /// After the loot click the bobber has to be gone; only then does the catch
    /// count. A bobber that is still there means the click missed (or the bags are full).
    /// </summary>
    public static bool VerifyLoot {
      get { return MySelf._VerifyLoot; }
      set { MySelf._VerifyLoot = value; }
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    [Newtonsoft.Json.JsonProperty("AlertOnAutoStop")]
    private bool _AlertOnAutoStop = true;
    /// <summary>Play a sound when the bot stops by itself (safety stop, limit reached, game gone).</summary>
    public static bool AlertOnAutoStop {
      get { return MySelf._AlertOnAutoStop; }
      set { MySelf._AlertOnAutoStop = value; }
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    [Newtonsoft.Json.JsonProperty("WriteLogFile")]
    private bool _WriteLogFile = true;
    /// <summary>Write everything the bot does to a log file in the user profile.</summary>
    public static bool WriteLogFile {
      get { return MySelf._WriteLogFile; }
      set { MySelf._WriteLogFile = value; }
    }
  }
}
