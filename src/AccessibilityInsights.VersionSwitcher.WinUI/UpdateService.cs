// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.SetupLibrary;
using AccessibilityInsights.VersionSwitcher;
using System.Diagnostics;

namespace AccessibilityInsights_VersionSwitcher_WinUI;

public interface IUpdateService
{
    Task RunAsync(IProgress<int> progress);
}

public sealed class UpdateService : IUpdateService
{
    public Task RunAsync(IProgress<int> progress) => Task.Run(() =>
    {
        var history = new ExecutionHistory();
        var timer = Stopwatch.StartNew();
        try
        {
            history.TypedExecutionResult = ResultExecutionWrapper.Execute(
                ExecutionResult.ErrorInstallingMsi,
                () => App.GetString("UnableToCompleteInstallation"),
                () =>
                {
                    if (UpdateBootstrap.RelaunchFromProtectedStaging(Environment.GetCommandLineArgs().Skip(1).ToArray()))
                    {
                        Environment.Exit(0);
                    }
                    string? appPath;
                    try
                    {
                        appPath = MsiUtilities.GetAppInstalledPath();
                    }
                    catch (InvalidOperationException exception)
                    {
                        history.AddLocalDetail(exception.ToString());
                        appPath = null;
                    }

                    return new InstallationEngine("Accessibility Insights For Windows v1.1", appPath, history)
                        .PerformInstallation(progress.Report);
                });
        }
        catch (ResultBearingException exception)
        {
            history.TypedExecutionResult = exception.Result;
            history.AddLocalDetail(exception.ToString());
            throw;
        }
        finally
        {
            history.ExecutionTimeInMilliseconds = timer.ElapsedMilliseconds;
            FileHelpers.SerializeDataToJSON(history, ExecutionHistory.GetDataFilePath());
        }
    });
}
