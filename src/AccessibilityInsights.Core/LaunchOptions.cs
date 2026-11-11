// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using CommandLine;
using CommandLine.Text;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AccessibilityInsights.Core;

public sealed class LaunchOptions
{
    [Value(0, Required = false)]
    public string? FileToOpen { get; set; }

    [Option('c', nameof(ConfigFolder), Required = false)]
    public string? ConfigFolder { get; set; }

    [Option('u', nameof(UserDataFolder), Required = false)]
    public string? UserDataFolder { get; set; }

    [Option('a', nameof(AttachToDebugger), Required = false)]
    public bool AttachToDebugger { get; set; }

    public static LaunchOptions Parse(IEnumerable<string> arguments)
    {
        using var parser = new Parser(settings =>
        {
            settings.EnableDashDash = true;
            settings.HelpWriter = null;
        });
        var result = parser.ParseArguments<LaunchOptions>(arguments);
        return result.MapResult(
            options => options,
            _ => throw new ArgumentException(HelpText.AutoBuild(result).ToString()));
    }

    public static LaunchOptions ParseRawArguments(string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Contains('\0'))
        {
            throw new ArgumentException("The command line contains a null character.", nameof(arguments));
        }

        var argv = CommandLineToArgvW("AccessibilityInsights.exe " + arguments, out var count);
        if (argv == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var tokens = new string[count - 1];
            for (var index = 1; index < count; index++)
            {
                tokens[index - 1] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, index * nint.Size))
                    ?? throw new InvalidOperationException("The command line contains an invalid argument.");
            }

            return Parse(tokens);
        }
        finally
        {
            LocalFree(argv);
        }
    }

    public void Validate()
    {
        if (FileToOpen is not null)
        {
            FileToOpen = Path.GetFullPath(FileToOpen);
            if (!File.Exists(FileToOpen))
            {
                throw new FileNotFoundException("The selected accessibility file was not found.", FileToOpen);
            }

            var extension = Path.GetExtension(FileToOpen);
            if (!string.Equals(extension, ".a11ytest", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".a11yevent", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Only .a11ytest and .a11yevent files can be opened.");
            }
        }

        if (ConfigFolder is not null)
        {
            ConfigFolder = Path.GetFullPath(ConfigFolder);
        }

        if (UserDataFolder is not null)
        {
            UserDataFolder = Path.GetFullPath(UserDataFolder);
        }
    }

    public void AttachDebugger()
    {
        if (AttachToDebugger && !Debugger.IsAttached && !Debugger.Launch())
        {
            throw new InvalidOperationException("The debugger could not be attached.");
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint CommandLineToArgvW(string commandLine, out int argumentCount);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint LocalFree(nint memory);
}
