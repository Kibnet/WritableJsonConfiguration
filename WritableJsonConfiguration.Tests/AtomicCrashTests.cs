using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using TheoryAttribute = WritableJsonConfiguration.Tests.WindowsTheoryAttribute;
using FactAttribute = WritableJsonConfiguration.Tests.WindowsFactAttribute;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace WritableJsonConfiguration.Tests;

[SupportedOSPlatform("windows")]
public class AtomicCrashTests
{
    [Fact]
    public async Task RetainedLegacyWriterCanLeaveTruncatedJsonWhenKilledDuringWrite()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WritableJsonLegacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Settings.json");
        File.WriteAllText(path, "{\"Theme\":\"old\"}");
        var originalLength = new FileInfo(path).Length;
        using var process = CreateProcess(path, "legacy");
        try
        {
            Assert.True(process.Start());
            Assert.Equal("checkpoint:legacy-ready", await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)));
            var timer = Stopwatch.StartNew();
            bool sawTruncatedWrite = false;
            while (timer.Elapsed < TimeSpan.FromSeconds(20) && !process.HasExited)
            {
                var length = new FileInfo(path).Length;
                if (length != originalLength && length < 64 * 1024 * 1024)
                {
                    sawTruncatedWrite = true;
                    process.Kill(entireProcessTree: true);
                    break;
                }
                await Task.Delay(1);
            }
            Assert.True(sawTruncatedWrite, "The legacy write boundary was not observed; do not count this as reproduced corruption.");
            await process.WaitForExitAsync();
            Assert.ThrowsAny<Exception>(() => WritableJsonConfigurationFabric.Create(path, reloadOnChange: false, optional: false));
            Assert.False(File.Exists(path + ".bak"));
        }
        finally
        {
            if (!process.HasExited) { process.Kill(true); process.WaitForExit(); }
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("PermissionsApplied", "old", false)]
    [InlineData("BeforeFlush", "old", false)]
    [InlineData("BeforeCommit", "old", false)]
    [InlineData("AfterCommit", "new", true)]
    public async Task KillingWriterLeavesACompleteOldOrNewConfiguration(string checkpoint, string expected, bool hasBackup)
    {
        var directory = Path.Combine(Path.GetTempPath(), "WritableJsonCrash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Settings.json");
        var original = "{\"Theme\":\"old\",\"Unknown\":\"preserved\"}";
        File.WriteAllText(path, original);
        var permissions = new FileSecurity();
        permissions.SetAccessRuleProtection(true, false);
        permissions.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(permissions);
        var expectedAcl = new FileInfo(path).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        using var process = CreateProcess(path, checkpoint);
        try
        {
            Assert.True(process.Start());
            var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal("checkpoint:" + checkpoint, line);
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            using var reloaded = (IDisposable)WritableJsonConfigurationFabric.Create(path, reloadOnChange: false, optional: false);
            var root = (IConfigurationRoot)reloaded;
            Assert.Equal(expected, root["Theme"]);
            Assert.Equal("preserved", root["Unknown"]);
            Assert.Equal(hasBackup, File.Exists(path + ".bak"));
            if (hasBackup) Assert.Equal(original, File.ReadAllText(path + ".bak"));
            else Assert.Equal(original, File.ReadAllText(path));
            foreach (var file in Directory.EnumerateFiles(directory))
                Assert.Equal(expectedAcl, new FileInfo(file).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access));
        }
        finally
        {
            if (process.Id != 0 && !process.HasExited) { process.Kill(true); process.WaitForExit(); }
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Process CreateProcess(string path, string checkpoint)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            }
        };
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repository.FullName, "WritableJsonConfiguration.sln")))
            repository = repository.Parent ?? throw new InvalidOperationException("Repository not found.");
        process.StartInfo.ArgumentList.Add(Path.Combine(repository.FullName, "WritableJsonConfiguration.CrashHarness", "bin", configuration, "net8.0", "WritableJsonConfiguration.CrashHarness.dll"));
        process.StartInfo.ArgumentList.Add(path);
        process.StartInfo.ArgumentList.Add(checkpoint);
        return process;
    }
}
