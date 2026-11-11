// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using System.Diagnostics;
using System.Security.Principal;

namespace AccessibilityInsights_VersionSwitcher_WinUI;

internal static class UpdateBootstrap
{
    private const string ParentVariable = "ACCESSIBILITY_INSIGHTS_UPDATE_PARENT";

    public static bool RelaunchFromProtectedStaging(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Length < 3)
        {
            return false;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var installationRoot = Path.Combine(programFiles, "AccessibilityInsights");
        var source = Path.GetFullPath(AppContext.BaseDirectory);
        if (!source.StartsWith(installationRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Updates must be launched from the trusted installed application.");
        }

        for (var directory = new DirectoryInfo(source); directory is not null &&
            !string.Equals(directory.FullName, programFiles, StringComparison.OrdinalIgnoreCase); directory = directory.Parent)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("The update location must not contain directory links.");
            }
        }

        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            throw new InvalidOperationException("The update helper requires administrator approval.");
        }

        var stagingRoot = Path.Combine(installationRoot, "UpdateStaging");
        if (source.StartsWith(stagingRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllText(Path.Combine(source, "updater.pid"),
                Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (int.TryParse(Environment.GetEnvironmentVariable(ParentVariable), out var parentId))
            {
                try
                {
                    using var parent = Process.GetProcessById(parentId);
                    if (!parent.WaitForExit(30000))
                    {
                        throw new TimeoutException("The installed update helper did not exit.");
                    }
                }
                catch (ArgumentException)
                {
                    // The bootstrap process may have exited before the child starts.
                }
            }
            return false;
        }

        RemoveCompletedStages(stagingRoot);
        var destination = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        CopyPayload(source, destination);
        var start = new ProcessStartInfo(Path.Combine(destination, "AccessibilityInsights.VersionSwitcher.exe"))
        {
            UseShellExecute = false,
            WorkingDirectory = destination,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        start.Environment[ParentVariable] = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var child = Process.Start(start)
            ?? throw new InvalidOperationException("The staged update helper did not start.");
        return true;
    }

    private static void RemoveCompletedStages(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }
        foreach (var directory in new DirectoryInfo(root).EnumerateDirectories())
        {
            var marker = Path.Combine(directory.FullName, "updater.pid");
            if (!Guid.TryParseExact(directory.Name, "N", out _) ||
                (directory.Attributes & FileAttributes.ReparsePoint) != 0 ||
                !File.Exists(marker) ||
                !int.TryParse(File.ReadAllText(marker), out var processId))
            {
                continue;
            }
            try
            {
                using var process = Process.GetProcessById(processId);
                if (!process.HasExited)
                {
                    continue;
                }
            }
            catch (ArgumentException)
            {
                // A missing process identifies a completed staged updater.
            }
            try
            {
                Directory.Delete(directory.FullName, true);
            }
            catch (IOException exception)
            {
                Trace.TraceWarning("Unable to remove a completed update stage: {0}", exception);
            }
        }
    }

    private static void CopyPayload(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("The update payload must not contain file or directory links.");
            }
            var target = Path.Combine(destination, entry.Name);
            if (entry is DirectoryInfo directory)
            {
                CopyPayload(directory.FullName, target);
            }
            else
            {
                File.Copy(entry.FullName, target, false);
            }
        }
    }
}
