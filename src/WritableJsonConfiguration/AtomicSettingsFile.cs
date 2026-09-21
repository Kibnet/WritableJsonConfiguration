using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;

namespace WritableJsonConfiguration
{
    internal enum AtomicWriteStage { PermissionsApplied, BeforeFlush, BeforeCommit, AfterCommit }

    internal static class AtomicSettingsFile
    {
        internal static void EnsureSupported()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                throw new PlatformNotSupportedException("Atomic configuration writes are supported only on Windows.");
        }

        internal static void Write(string path, string json, bool exists, Action<AtomicWriteStage, string> checkpoint)
        {
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            var backup = path + ".bak";
            var permissions = exists ? ReadPermissions(path) : null;
            if (exists && File.Exists(backup) &&
                !HaveEquivalentAccess(permissions, ReadPermissions(backup)))
                throw new IOException("Configuration backup permissions differ; refusing to broaden access.");

            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    // The file is still empty: do not place secrets under inherited directory ACLs.
                    if (permissions != null) new FileInfo(temporary).SetAccessControl(permissions);
                    checkpoint?.Invoke(AtomicWriteStage.PermissionsApplied, temporary);
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true))
                    {
                        writer.Write(json);
                    }
                    checkpoint?.Invoke(AtomicWriteStage.BeforeFlush, temporary);
                    stream.Flush(flushToDisk: true);
                }
                checkpoint?.Invoke(AtomicWriteStage.BeforeCommit, temporary);
                if (exists) File.Replace(temporary, path, backup);
                else File.Move(temporary, path);
                checkpoint?.Invoke(AtomicWriteStage.AfterCommit, path);
            }
            finally
            {
                // Never remove another writer's temp, the main file, or a usable backup.
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static FileSecurity ReadPermissions(string path)
        {
            var security = new FileInfo(path).GetAccessControl(AccessControlSections.Access);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: true);
            return security;
        }

        internal static bool HaveEquivalentAccess(FileSecurity left, FileSecurity right)
        {
            return AccessFingerprint(left) == AccessFingerprint(right);
        }

        private static string AccessFingerprint(FileSecurity security)
        {
            var descriptor = new RawSecurityDescriptor(security.GetSecurityDescriptorBinaryForm(), 0);
            var dacl = descriptor.DiscretionaryAcl;
            if (dacl == null) return "null-dacl";
            var result = new StringBuilder();
            var group = new List<string>();
            int previousType = -1;
            for (int index = 0; index < dacl.Count; index++)
            {
                var ace = dacl[index];
                // Windows removes inherited provenance and can normalize propagation flags when
                // persisting an ACL on a file. Those flags only control inheritance by children;
                // a file cannot have children. InheritOnly still changes access to this file and
                // must remain significant. Never reorder deny across allow: their relative
                // position can change effective permissions.
                var common = ace as CommonAce;
                bool canReorder = common != null && !common.IsCallback &&
                    (common.AceQualifier == AceQualifier.AccessAllowed || common.AceQualifier == AceQualifier.AccessDenied);
                int type = canReorder ? (int)ace.AceType : 256 + index;
                if (type != previousType)
                {
                    AppendGroup(result, group, previousType);
                    previousType = type;
                }
                var bytes = new byte[ace.BinaryLength];
                ace.GetBinaryForm(bytes, 0);
                var normalized = GenericAce.CreateFromBinaryForm(bytes, 0);
                if (canReorder)
                    normalized.AceFlags &= ~(AceFlags.Inherited | AceFlags.ObjectInherit |
                        AceFlags.ContainerInherit | AceFlags.NoPropagateInherit);
                normalized.GetBinaryForm(bytes, 0);
                group.Add(Convert.ToBase64String(bytes));
            }
            AppendGroup(result, group, previousType);
            return result.ToString();
        }

        private static void AppendGroup(StringBuilder result, List<string> group, int type)
        {
            if (group.Count == 0) return;
            group.Sort(StringComparer.Ordinal);
            result.Append(type).Append(':');
            string previous = null;
            bool appended = false;
            foreach (var entry in group)
            {
                // File.Replace can retain the same effective ACE once as explicit and once as
                // inherited. After provenance normalization, exact duplicates do not change access.
                if (entry == previous) continue;
                if (appended) result.Append(',');
                result.Append(entry);
                previous = entry;
                appended = true;
            }
            result.Append(';');
            group.Clear();
        }
    }
}
