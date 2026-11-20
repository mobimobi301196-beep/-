using System;
using System.Drawing;

namespace DefinetlyNotAFishingBot {

  /// <summary>
  /// Built-in checks for the pure logic (bobber detection, config validation),
  /// run with "DefinetlyNotAFishingBot.exe --selftest". There is no test
  /// framework in this classic project; this is the cheap safety net that
  /// needs neither a game nor a window and exits with 0 on success, 1 on failure.
  /// </summary>
  internal static class SelfTest {

    private static int failures = 0;

    internal static int Run() {
      failures = 0;
      Console.WriteLine("Self test…");

      TestDetectorFindsBobber();
      TestDetectorRejectsMurkyWater();
      TestDetectorTracksADip();
      TestConfigRejectsGarbage();
      TestConfigClampsValues();
      TestDelayRange();

      Console.WriteLine(failures == 0 ? "Self test passed." : "Self test FAILED (" + failures + ").");
      return failures == 0 ? 0 : 1;
    }

    private static void Check(string name, bool ok) {
      Console.WriteLine((ok ? "  ok    " : "  FAIL  ") + name);
      if (!ok)
        failures++;
    }

    private static Bitmap Filled(int width, int height, Color color) {
      Bitmap b = new Bitmap(width, height);
      for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
          b.SetPixel(x, y, color);
      return b;
    }

    private static void Block(Bitmap b, int x0, int y0, int size, Color color) {
      for (int y = y0; y < y0 + size; y++)
        for (int x = x0; x < x0 + size; x++)
          b.SetPixel(x, y, color);
    }

    private static readonly Color Feather = Color.FromArgb(255, 0x5C, 0x32, 0x27);

    private static void TestDetectorFindsBobber() {
      using (Bitmap img = Filled(200, 120, Color.FromArgb(255, 30, 70, 110))) {
        Block(img, 50, 40, 10, Feather);
        BobberSample s = BobberDetector.FindBobber(img, Feather, 20);
        Check("detector counts exactly the bobber pixels", s.MatchCount == 100);
        Check("detector centroid is on the bobber", Math.Abs(s.Centroid.X - 54) <= 1 && Math.Abs(s.Centroid.Y - 44) <= 1);
      }
    }

    private static void TestDetectorRejectsMurkyWater() {
      // Brownish water is close to the feather color per channel but lacks its red dominance.
      using (Bitmap img = Filled(100, 100, Color.FromArgb(255, 80, 70, 40))) {
        BobberSample s = BobberDetector.FindBobber(img, Feather, 20);
        Check("detector ignores murky brown water", s.MatchCount == 0);
      }
    }

    private static void TestDetectorTracksADip() {
      Color water = Color.FromArgb(255, 30, 70, 110);
      int upperY;
      int lowerY;
      using (Bitmap a = Filled(100, 100, water)) {
        Block(a, 40, 30, 8, Feather);
        upperY = BobberDetector.FindBobber(a, Feather, 20).Centroid.Y;
      }
      using (Bitmap b = Filled(100, 100, water)) {
        Block(b, 40, 42, 8, Feather);
        lowerY = BobberDetector.FindBobber(b, Feather, 20).Centroid.Y;
      }
      Check("detector centroid follows the bobber downwards", lowerY - upperY == 12);
    }

    private static void TestDelayRange() {
      Random rng = new Random(1);
      Check("delay: no upper bound gives the fixed time", FishingBot.PickDelayMs(2, 0, rng) == 2000);
      Check("delay: upper bound not above the minimum stays fixed", FishingBot.PickDelayMs(3, 2.5, rng) == 3000);
      bool inRange = true;
      bool varies = false;
      int first = FishingBot.PickDelayMs(1, 3.5, rng);
      for (int i = 0; i < 300; i++) {
        int d = FishingBot.PickDelayMs(1, 3.5, rng);
        if (d < 1000 || d > 3500)
          inRange = false;
        if (d != first)
          varies = true;
      }
      Check("delay: random values stay inside the range", inRange);
      Check("delay: random values actually vary", varies);
    }

    private static void TestConfigRejectsGarbage() {
      Check("config: garbage text is rejected", Config.ParseAndValidate("this is not json") == null);
      Check("config: empty text is rejected", Config.ParseAndValidate("") == null);
    }

    private static void TestConfigClampsValues() {
      Config c = Config.ParseAndValidate(
        "{\"RefishTime\":-5,\"RefishTimeMax\":9999,\"LootTimeMax\":-3,\"LootTime\":999,\"ColorTollerance\":9999,\"MaxFailsInRow\":-1,"
        + "\"LureIntervalMinutes\":0,\"CaptureWidth\":0,\"CaptureHeight\":-4}");
      Check("config: out-of-range values are clamped", c != null && c.IsValid());
      Config d = Config.ParseAndValidate("{}");
      Check("config: empty object gives valid defaults", d != null && d.IsValid());
    }
  }
}
