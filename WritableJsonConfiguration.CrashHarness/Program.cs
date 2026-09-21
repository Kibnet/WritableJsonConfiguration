using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using WritableJsonConfiguration;

if (args[0] == "benchmark")
{
    var folder = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(folder);
    for (int run = 0; run < 5; run++)
    foreach (var atomic in run % 2 == 0 ? new[] { false, true } : new[] { true, false })
    {
        var path = Path.Combine(folder, $"{run}-" + (atomic ? "atomic.json" : "legacy.json"));
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(
            Enumerable.Range(0, 100).ToDictionary(i => "Setting" + i, i => new string('x', 200))));
        var startup = Stopwatch.StartNew();
        using var configuration = Build(path, atomic);
        startup.Stop();
        var root = (IConfigurationRoot)configuration;
        root["Counter"] = "warmup";
        var save = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++) root["Counter"] = i.ToString();
        save.Stop();
        var noOp = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++) root["Counter"] = "99";
        noOp.Stop();
        Console.WriteLine($"run={run} {(atomic ? "atomic" : "legacy")}: bytes={new FileInfo(path).Length}, startup_ms={startup.Elapsed.TotalMilliseconds:F3}, 100_save_ms={save.Elapsed.TotalMilliseconds:F3}, 100_noop_ms={noOp.Elapsed.TotalMilliseconds:F3}");
    }
    return;
}

using var disposable = Build(Path.GetFullPath(args[0]), atomic: args[1] != "legacy");
var rootConfiguration = (IConfigurationRoot)disposable;
if (args[1] == "legacy")
{
    var largeSyntheticValue = new string('x', 64 * 1024 * 1024);
    Console.WriteLine("checkpoint:legacy-ready");
    Console.Out.Flush();
    rootConfiguration["Theme"] = largeSyntheticValue;
    return;
}
var provider = (WritableJsonConfigurationProvider)rootConfiguration.Providers.Single();
var requestedStage = Enum.Parse<AtomicWriteStage>(args[1]);
provider.AtomicWriteCheckpoint = (stage, _) =>
{
    if (stage != requestedStage) return;
    Console.WriteLine("checkpoint:" + stage);
    Console.Out.Flush();
    Thread.Sleep(Timeout.Infinite);
};
rootConfiguration["Theme"] = "new";
throw new InvalidOperationException("The requested checkpoint was not reached.");

static IDisposable Build(string path, bool atomic) => (IDisposable)WritableJsonConfigurationFabric.Create(source =>
{
    source.Path = path;
    source.Optional = false;
    source.ReloadOnChange = false;
    source.UseAtomicWrites = atomic;
    source.ResolveFileProvider();
});
