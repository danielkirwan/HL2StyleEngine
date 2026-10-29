using System.Diagnostics;
using System.Text.Json;

internal static class GameplayHitchChecks
{
    internal static void Run(string root)
    {
        string manifest = LoadingPackageChecks.Build(root);
        string folder = Path.Combine(AppContext.BaseDirectory, "qa", "hitch-suite-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);
        var runs = new List<object>();
        int failures = 0;
        // Build/cook in this parent only. Every measured child starts with a fresh managed heap.
        string[] modes = ["source", "package", "package", "source", "source", "package", "package-off"];
        for (int i = 0; i < modes.Length; i++)
        {
            string mode = modes[i], report = Path.Combine(folder, $"{i:00}-{mode}.json");
            var start = new ProcessStartInfo(Path.Combine(root, ".dotnet/dotnet.exe"))
            {
                WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (string arg in new[] { typeof(GameplayHitchChecks).Assembly.Location, "profile-gameplay-child", manifest, mode, report })
                start.ArgumentList.Add(arg);
            using var child = Process.Start(start) ?? throw new InvalidOperationException("Gameplay child did not start.");
            var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
            bool timedOut = !child.WaitForExit(120000);
            if (timedOut) { child.Kill(true); child.WaitForExit(); }
            string log = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            File.WriteAllText(Path.ChangeExtension(report, ".log"), log);
            double? mean = null, p95 = null;
            int? over33 = null;
            string? reportError = null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(report, ".gameplay.json")));
                var times = doc.RootElement.GetProperty("Intervals").EnumerateArray().Select(x => x.GetDouble()).Order().ToArray();
                using var trace = JsonDocument.Parse(File.ReadAllText(report));
                if (times.Length != 300 || trace.RootElement.GetProperty("Frames").GetArrayLength() != 420)
                    throw new InvalidDataException("Incomplete frame report.");
                mean = times.Average(); p95 = times[284]; over33 = times.Count(x => x > 33.3);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException)
            {
                reportError = ex.Message;
            }
            var result = new { Mode = mode, child.ExitCode, TimedOut = timedOut, Mean = mean, P95 = p95, Over33ms = over33, Report = report, ReportError = reportError };
            runs.Add(result); Console.WriteLine(JsonSerializer.Serialize(result));
            if (child.ExitCode != 0 || timedOut || reportError != null) failures++;
        }
        File.WriteAllText(Path.Combine(folder, "summary.json"), JsonSerializer.Serialize(new { Runs = runs, Failures = failures,
            CacheCondition = "Fresh child per run; OS files warmed by parent preparation; not cold-disk timing" }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("[HitchSuite] " + folder);
        if (failures != 0) throw new InvalidDataException($"{failures} gameplay runs failed existing gates; all reports retained.");
    }
}
