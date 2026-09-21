using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;
using FactAttribute = WritableJsonConfiguration.Tests.WindowsFactAttribute;
using TheoryAttribute = WritableJsonConfiguration.Tests.WindowsTheoryAttribute;

namespace WritableJsonConfiguration.Tests;

[SupportedOSPlatform("windows")]
public class AtomicWriteTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "WritableJsonTests-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(directory, "Settings.json");

    public AtomicWriteTests() => Directory.CreateDirectory(directory);

    private IConfigurationRoot Create(bool atomic = true)
    {
        return WritableJsonConfigurationFabric.Create(source =>
        {
            source.Path = SettingsPath;
            source.Optional = true;
            source.ReloadOnChange = false;
            source.ResolveFileProvider();
            source.UseAtomicWrites = atomic;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedSaveDoesNotPublishMemory(bool objectOverload)
    {
        File.WriteAllText(SettingsPath, "{\"Theme\":\"old\"}");
        using var root = (IDisposable)Create();
        var configuration = (IConfigurationRoot)root;
        var provider = (WritableJsonConfigurationProvider)configuration.Providers.Single();
        using var locked = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.ThrowsAny<IOException>(() =>
        {
            if (objectOverload) provider.Set("Theme", (object)"new");
            else ((IConfigurationProvider)provider).Set("Theme", "new");
        });
        Assert.Equal("old", configuration["Theme"]);
        Assert.Equal("old", JObject.Parse(File.ReadAllText(SettingsPath))["Theme"]!.Value<string>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingApiPreservesUnknownValuesAndLegacyMerge(bool atomic)
    {
        File.WriteAllText(SettingsPath, "{\"Unknown\":17,\"Enabled\":true,\"Null\":null,\"Items\":[\"a\",\"b\"]}");
        using var root = (IDisposable)Create(atomic);
        var configuration = (IConfigurationRoot)root;
        configuration.Set("Items", new[] { "changed" });
        configuration.Set("Options", new { Flag = true, Count = 42, Empty = (string?)null });
        Assert.Equal(new[] { "changed", "b" }, configuration.Get<string[]>("Items"));
        Assert.True(configuration.Get<bool>("Options:Flag"));
        Assert.Equal(42, configuration.Get<int>("Options:Count"));
        Assert.Equal("", configuration["Options:Empty"]);
        var disk = JObject.Parse(File.ReadAllText(SettingsPath));
        Assert.Equal(17, disk["Unknown"]!.Value<int>());
        Assert.Equal(JTokenType.Boolean, disk["Enabled"]!.Type);
        Assert.Equal(JTokenType.Null, disk["Null"]!.Type);
        Assert.Equal(JTokenType.String, disk["Options"]!["Count"]!.Type);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AtomicDeepUpdatesPreserveUnknownSiblingsOnDiskAndAfterReload(bool objectOverload)
    {
        File.WriteAllText(SettingsPath, "{\"A\":{\"B\":{\"One\":\"1\",\"Two\":\"2\"},\"Unknown\":true},\"Outside\":17}");
        using var root = (IDisposable)Create();
        var configuration = (IConfigurationRoot)root;
        if (objectOverload) configuration.Set("A:B", new { One = "x" });
        else configuration["A:B:One"] = "x";
        Assert.Equal("x", configuration["A:B:One"]);
        Assert.Equal("2", configuration["A:B:Two"]);
        using var reloaded = (IDisposable)Create();
        Assert.Equal("2", ((IConfigurationRoot)reloaded)["A:B:Two"]);
        var disk = JObject.Parse(File.ReadAllText(SettingsPath));
        Assert.True(disk["A"]!["Unknown"]!.Value<bool>());
        Assert.Equal(17, disk["Outside"]!.Value<int>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AtomicNestedArrayUpdatesPreserveOtherFieldsAndTail(bool objectOverload)
    {
        File.WriteAllText(SettingsPath, "{\"A\":{\"B\":{\"Items\":[{\"One\":\"1\",\"Two\":\"2\"},{\"Tail\":\"kept\"}],\"Unknown\":7}}}");
        using var root = (IDisposable)Create();
        var configuration = (IConfigurationRoot)root;
        if (objectOverload) configuration.Set("A:B:Items", new[] { new { One = "x" } });
        else configuration["A:B:Items:0:One"] = "x";
        Assert.Equal("x", configuration["A:B:Items:0:One"]);
        Assert.Equal("2", configuration["A:B:Items:0:Two"]);
        Assert.Equal("kept", configuration["A:B:Items:1:Tail"]);
        Assert.Equal("7", configuration["A:B:Unknown"]);
        using var reloaded = (IDisposable)Create();
        Assert.Equal("2", ((IConfigurationRoot)reloaded)["A:B:Items:0:Two"]);
        Assert.Equal("kept", ((IConfigurationRoot)reloaded)["A:B:Items:1:Tail"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AtomicNumericObjectKeysRemainObjectProperties(bool objectOverload)
    {
        File.WriteAllText(SettingsPath, "{\"Years\":{\"2024\":\"old\",\"Label\":\"kept\"}}");
        using var root = (IDisposable)Create();
        var configuration = (IConfigurationRoot)root;
        if (objectOverload) configuration.Set("Years:2024", (object)"new");
        else configuration["Years:2024"] = "new";

        Assert.Equal("new", configuration["Years:2024"]);
        Assert.Equal("kept", configuration["Years:Label"]);
        var disk = JObject.Parse(File.ReadAllText(SettingsPath));
        Assert.Equal(JTokenType.Object, disk["Years"]!.Type);
        Assert.Equal("new", disk["Years"]!["2024"]!.Value<string>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AtomicSparseArrayIndexesAreRejectedWithoutWritingAnotherIndex(bool objectOverload)
    {
        const string original = "{\"Items\":[],\"Outside\":\"kept\"}";
        File.WriteAllText(SettingsPath, original);
        using var root = (IDisposable)Create();
        var configuration = (IConfigurationRoot)root;

        Assert.Throws<ArgumentException>(() =>
        {
            if (objectOverload) configuration.Set("Items:2", new { Name = "new" });
            else configuration["Items:2:Name"] = "new";
        });

        Assert.Null(configuration["Items:0:Name"]);
        Assert.Null(configuration["Items:2:Name"]);
        Assert.Equal("kept", configuration["Outside"]);
        Assert.True(JToken.DeepEquals(JObject.Parse(original), JObject.Parse(File.ReadAllText(SettingsPath))));
        Assert.False(File.Exists(SettingsPath + ".bak"));
    }

    [Fact]
    public void AtomicPathsCannotContinueThroughExistingScalarValues()
    {
        const string original = "{\"Theme\":\"old\"}";
        File.WriteAllText(SettingsPath, original);
        using var root = (IDisposable)Create();
        var configuration = (IConfigurationRoot)root;

        Assert.Throws<ArgumentException>(() => configuration["Theme:Nested"] = "new");

        Assert.Equal("old", configuration["Theme"]);
        Assert.Null(configuration["Theme:Nested"]);
        Assert.Equal(original, File.ReadAllText(SettingsPath));
        Assert.False(File.Exists(SettingsPath + ".bak"));
    }

    [Fact]
    public void DefaultRemainsLegacyAndDoesNotCreateBackup()
    {
        Assert.False(new WritableJsonConfigurationSource().UseAtomicWrites);
        using var root = (IDisposable)Create(atomic: false);
        var configuration = (IConfigurationRoot)root;
        configuration["Theme"] = "first";
        configuration["Theme"] = "second";
        Assert.False(File.Exists(SettingsPath + ".bak"));
    }

    [Fact]
    public void FirstSaveCreatesMainThenSecondCreatesExactPreviousBackupAndNoOpLeavesBothUntouched()
    {
        using var root = (IDisposable)Create();
        var configuration = (IConfigurationRoot)root;
        configuration["Theme"] = "first";
        Assert.False(File.Exists(SettingsPath + ".bak"));
        var first = File.ReadAllBytes(SettingsPath);
        configuration["Theme"] = "second";
        Assert.Equal(first, File.ReadAllBytes(SettingsPath + ".bak"));
        var mainTime = File.GetLastWriteTimeUtc(SettingsPath);
        var backupTime = File.GetLastWriteTimeUtc(SettingsPath + ".bak");
        configuration["Theme"] = "second";
        Assert.Equal(mainTime, File.GetLastWriteTimeUtc(SettingsPath));
        Assert.Equal(backupTime, File.GetLastWriteTimeUtc(SettingsPath + ".bak"));
        Assert.Equal(first, File.ReadAllBytes(SettingsPath + ".bak"));
    }

    [Fact]
    public async Task DifferentProvidersSerializeEntireReadModifyWrite()
    {
        File.WriteAllText(SettingsPath, "{\"Unknown\":\"kept\"}");
        using var left = (IDisposable)Create();
        using var right = (IDisposable)Create();
        await Task.WhenAll(Enumerable.Range(0, 40).Select(index => Task.Run(() =>
        {
            var root = (IConfigurationRoot)(index % 2 == 0 ? left : right);
            if (index % 2 == 0) root["Key" + index] = index.ToString();
            else root.Set("Key" + index, index);
        })));
        using var reloaded = (IDisposable)Create();
        for (int index = 0; index < 40; index++)
            Assert.Equal(index.ToString(), ((IConfigurationRoot)reloaded)["Key" + index]);
        Assert.Equal("kept", ((IConfigurationRoot)reloaded)["Unknown"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void InjectedPrecommitFailureLeavesMainBackupAndMemoryUnchanged(int stageValue)
    {
        File.WriteAllText(SettingsPath, "{\"Theme\":\"old\"}");
        File.Copy(SettingsPath, SettingsPath + ".bak");
        using var root = (IDisposable)Create();
        var configuration = (IConfigurationRoot)root;
        var provider = (WritableJsonConfigurationProvider)configuration.Providers.Single();
        var before = File.ReadAllBytes(SettingsPath);
        provider.AtomicWriteCheckpoint = (stage, temporary) =>
        {
            if (stage != (AtomicWriteStage)stageValue) return;
            if (stage == AtomicWriteStage.PermissionsApplied) Assert.Equal(0, new FileInfo(temporary).Length);
            throw new IOException("injected failure");
        };
        Assert.Throws<IOException>(() => configuration["Theme"] = "new");
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
        Assert.Equal(before, File.ReadAllBytes(SettingsPath + ".bak"));
        Assert.Equal("old", configuration["Theme"]);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp-*"));
    }

    [Fact]
    public void AmbiguousPostcommitErrorReconcilesMemoryWithActualDisk()
    {
        File.WriteAllText(SettingsPath, "{\"Theme\":\"old\"}");
        using var root = (IDisposable)Create();
        var configuration = (IConfigurationRoot)root;
        var provider = (WritableJsonConfigurationProvider)configuration.Providers.Single();
        provider.AtomicWriteCheckpoint = (stage, _) =>
        {
            if (stage == AtomicWriteStage.AfterCommit) throw new IOException("ambiguous commit");
        };
        Assert.Throws<IOException>(() => configuration["Theme"] = "new");
        Assert.Equal("new", configuration["Theme"]);
        Assert.Equal("new", JObject.Parse(File.ReadAllText(SettingsPath))["Theme"]!.Value<string>());
        Assert.Equal("old", JObject.Parse(File.ReadAllText(SettingsPath + ".bak"))["Theme"]!.Value<string>());
        provider.AtomicWriteCheckpoint = null;
        configuration["Theme"] = "next";
        Assert.Equal("next", configuration["Theme"]);
    }

    [Fact]
    public void UnreconciledFailureBlocksFurtherWritesEvenAfterExternalRepair()
    {
        File.WriteAllText(SettingsPath, "{\"Theme\":\"old\"}");
        using var root = (IDisposable)Create();
        var configuration = (IConfigurationRoot)root;
        var provider = (WritableJsonConfigurationProvider)configuration.Providers.Single();
        provider.AtomicWriteCheckpoint = (stage, _) =>
        {
            if (stage != AtomicWriteStage.BeforeCommit) return;
            File.WriteAllText(SettingsPath, "");
            throw new IOException("ambiguous external failure");
        };
        Assert.Throws<IOException>(() => configuration["Theme"] = "new");
        File.WriteAllText(SettingsPath, "{\"Theme\":\"repaired\"}");
        provider.AtomicWriteCheckpoint = null;
        Assert.Throws<IOException>(() => configuration["Theme"] = "retry");
        Assert.Equal("repaired", JObject.Parse(File.ReadAllText(SettingsPath))["Theme"]!.Value<string>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"Theme\":" )]
    [InlineData("[]")]
    [InlineData("{\"duplicate\":1,\"DUPLICATE\":2}")]
    public void CorruptionDuringRuntimeIsNotOverwritten(string invalidJson)
    {
        File.WriteAllText(SettingsPath, "{\"Theme\":\"old\"}");
        using var root = (IDisposable)Create();
        File.WriteAllText(SettingsPath, invalidJson);
        Assert.ThrowsAny<Exception>(() => ((IConfigurationRoot)root)["Theme"] = "new");
        Assert.Equal(invalidJson, File.ReadAllText(SettingsPath));
        Assert.False(File.Exists(SettingsPath + ".bak"));
    }

    [Fact]
    public void CandidateMustBeAcceptedByStartupParser()
    {
        File.WriteAllText(SettingsPath, "{\"theme\":\"old\"}");
        using var root = (IDisposable)Create();
        var before = File.ReadAllBytes(SettingsPath);
        Assert.ThrowsAny<Exception>(() => ((IConfigurationRoot)root)["THEME"] = "new");
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
        Assert.Equal("old", ((IConfigurationRoot)root)["theme"]);
    }

    [Fact]
    public void ParserFailureDoesNotExposeSettingsThroughExceptionChain()
    {
        File.WriteAllText(SettingsPath, "{\"Theme\":\"old\"}");
        using var root = (IDisposable)Create();
        File.WriteAllText(SettingsPath, "{\"synthetic-secret-marker\":1,\"SYNTHETIC-SECRET-MARKER\":2}");
        var error = Assert.Throws<InvalidDataException>(() => ((IConfigurationRoot)root)["Theme"] = "new");
        Assert.DoesNotContain("secret-marker", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void CopiesReceiveRestrictiveMainAclBeforeAnyBytes()
    {
        File.WriteAllText(SettingsPath, "{\"Secret\":\"synthetic-only\"}");
        Restrict(SettingsPath);
        var expected = Permissions(SettingsPath);
        Assert.NotEqual(expected, Permissions(directory, isDirectory: true));
        using var root = (IDisposable)Create();
        var configuration = (IConfigurationRoot)root;
        var provider = (WritableJsonConfigurationProvider)configuration.Providers.Single();
        var inspected = false;
        provider.AtomicWriteCheckpoint = (stage, temporary) =>
        {
            if (stage != AtomicWriteStage.PermissionsApplied) return;
            inspected = true;
            Assert.Equal(0, new FileInfo(temporary).Length);
            Assert.Equal(expected, Permissions(temporary));
        };
        configuration["Theme"] = "new";
        Assert.True(inspected);
        Assert.Equal(expected, Permissions(SettingsPath));
        Assert.Equal(expected, Permissions(SettingsPath + ".bak"));
    }

    [Fact]
    public void PersistedProtectedBootstrapBackupAllowsSuccessiveSavesOfInheritedMain()
    {
        File.WriteAllText(SettingsPath, "{\"Theme\":\"old\"}");
        Assert.False(new FileInfo(SettingsPath).GetAccessControl().AreAccessRulesProtected);
        var bootstrapPermissions = new FileInfo(SettingsPath).GetAccessControl(AccessControlSections.Access);
        bootstrapPermissions.SetAccessRuleProtection(isProtected: true, preserveInheritance: true);
        var temporary = SettingsPath + ".bootstrap";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            new FileInfo(temporary).SetAccessControl(bootstrapPermissions);
            stream.Write(File.ReadAllBytes(SettingsPath));
            stream.Flush(true);
        }
        File.Move(temporary, SettingsPath + ".bak");
        Assert.True(AtomicSettingsFile.HaveEquivalentAccess(
            new FileInfo(SettingsPath).GetAccessControl(AccessControlSections.Access),
            new FileInfo(SettingsPath + ".bak").GetAccessControl(AccessControlSections.Access)));
        using var root = (IDisposable)Create();
        var configuration = (IConfigurationRoot)root;
        configuration["Theme"] = "first";
        configuration["Theme"] = "second";
        configuration["Theme"] = "third";
        Assert.Equal("third", configuration["Theme"]);
        Assert.Equal("second", JObject.Parse(File.ReadAllText(SettingsPath + ".bak"))["Theme"]!.Value<string>());
    }

    [Fact]
    public void AccessComparisonIgnoresOnlyProvenanceAndOrderWithinSameKind()
    {
        var inherited = Security("D:AI(A;ID;FR;;;SY)(A;ID;FA;;;BA)");
        var explicitReversed = Security("D:P(A;;FA;;;BA)(A;;FR;;;SY)");
        Assert.True(AtomicSettingsFile.HaveEquivalentAccess(inherited, explicitReversed));
        Assert.True(AtomicSettingsFile.HaveEquivalentAccess(
            Security("D:P(A;OICINP;FR;;;SY)"), Security("D:P(A;;FR;;;SY)")));
        Assert.False(AtomicSettingsFile.HaveEquivalentAccess(inherited, Security("D:P(A;;FA;;;BA)(A;;FA;;;SY)")));
        Assert.False(AtomicSettingsFile.HaveEquivalentAccess(inherited, Security("D:P(A;;FA;;;BA)(A;;FR;;;WD)")));
        Assert.False(AtomicSettingsFile.HaveEquivalentAccess(inherited, Security("D:P(A;;FA;;;BA)(A;IO;FR;;;SY)")));
    }

    [Fact]
    public void AccessComparisonDoesNotIgnoreAllowDenyOrderOrMergeGrantSets()
    {
        Assert.False(AtomicSettingsFile.HaveEquivalentAccess(
            Security("D:(A;;FR;;;SY)(D;;FR;;;SY)"), Security("D:(D;;FR;;;SY)(A;;FR;;;SY)")));
        Assert.False(AtomicSettingsFile.HaveEquivalentAccess(Security("D:"), Security("D:(A;;FA;;;WD)")));
    }

    [Fact]
    public void AccessComparisonPreservesUnsupportedCallbackAceExactly()
    {
        Assert.True(AtomicSettingsFile.HaveEquivalentAccess(CallbackSecurity(AceFlags.Inherited), CallbackSecurity(AceFlags.Inherited)));
        Assert.False(AtomicSettingsFile.HaveEquivalentAccess(CallbackSecurity(AceFlags.Inherited), CallbackSecurity(AceFlags.None)));
    }

    private static FileSecurity CallbackSecurity(AceFlags flags)
    {
        var acl = new RawAcl(GenericAcl.AclRevision, 1);
        acl.InsertAce(0, new CommonAce(flags, AceQualifier.AccessAllowed, 1,
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), true, new byte[4]));
        var descriptor = new RawSecurityDescriptor(ControlFlags.DiscretionaryAclPresent, null, null, null, acl);
        var bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);
        var security = new FileSecurity();
        security.SetSecurityDescriptorBinaryForm(bytes, AccessControlSections.Access);
        return security;
    }

    private static FileSecurity Security(string sddl)
    {
        var security = new FileSecurity();
        security.SetSecurityDescriptorSddlForm(sddl, AccessControlSections.Access);
        return security;
    }

    [Fact]
    public void RestrictiveExistingBackupIsNeverReplacedWithBroaderAcl()
    {
        File.WriteAllText(SettingsPath, "{\"Theme\":\"old\"}");
        File.Copy(SettingsPath, SettingsPath + ".bak");
        Restrict(SettingsPath + ".bak");
        var expected = Permissions(SettingsPath + ".bak");
        using var root = (IDisposable)Create();
        Assert.Throws<IOException>(() => ((IConfigurationRoot)root)["Theme"] = "new");
        Assert.Equal(expected, Permissions(SettingsPath + ".bak"));
        Assert.Equal("old", ((IConfigurationRoot)root)["Theme"]);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp-*"));
    }

    [Fact]
    public void PermissionFailureOccursBeforeConfidentialBytes()
    {
        File.WriteAllText(SettingsPath, "{\"Secret\":\"synthetic-only\"}");
        using var root = (IDisposable)Create();
        var configuration = (IConfigurationRoot)root;
        var provider = (WritableJsonConfigurationProvider)configuration.Providers.Single();
        var before = File.ReadAllBytes(SettingsPath);
        provider.AtomicWriteCheckpoint = (stage, temporary) =>
        {
            if (stage != AtomicWriteStage.PermissionsApplied) return;
            Assert.Equal(0, new FileInfo(temporary).Length);
            throw new UnauthorizedAccessException("injected permission failure");
        };
        Assert.Throws<UnauthorizedAccessException>(() => configuration["Secret"] = "new");
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp-*"));
    }

    [Fact]
    public void LockedBackupMakesReplacementFailWithoutDiscardingEitherFile()
    {
        File.WriteAllText(SettingsPath, "{\"Theme\":\"old\"}");
        File.Copy(SettingsPath, SettingsPath + ".bak");
        using var root = (IDisposable)Create();
        using var locked = new FileStream(SettingsPath + ".bak", FileMode.Open, FileAccess.Read, FileShare.Read);
        var before = File.ReadAllBytes(SettingsPath);
        Assert.ThrowsAny<IOException>(() => ((IConfigurationRoot)root)["Theme"] = "new");
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
        Assert.Equal(before, File.ReadAllBytes(SettingsPath + ".bak"));
        Assert.Equal("old", ((IConfigurationRoot)root)["Theme"]);
    }

    [Fact]
    public void FirstSaveNeverOverwritesAFileCreatedByAnotherWriter()
    {
        using var root = (IDisposable)Create();
        var configuration = (IConfigurationRoot)root;
        var provider = (WritableJsonConfigurationProvider)configuration.Providers.Single();
        provider.AtomicWriteCheckpoint = (stage, _) =>
        {
            if (stage == AtomicWriteStage.BeforeCommit) File.WriteAllText(SettingsPath, "{\"Other\":\"kept\"}");
        };
        Assert.Throws<IOException>(() => configuration["Theme"] = "new");
        Assert.Equal("{\"Other\":\"kept\"}", File.ReadAllText(SettingsPath));
        Assert.Equal("kept", configuration["Other"]);
        Assert.Null(configuration["Theme"]);
    }

    private static void Restrict(string path)
    {
        var acl = new FileSecurity();
        acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(acl);
    }

    private static string Permissions(string path, bool isDirectory = false)
    {
        FileSystemSecurity acl = isDirectory
            ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access)
            : new FileInfo(path).GetAccessControl(AccessControlSections.Access);
        acl.SetAccessRuleProtection(true, true);
        return acl.GetSecurityDescriptorSddlForm(AccessControlSections.Access);
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
