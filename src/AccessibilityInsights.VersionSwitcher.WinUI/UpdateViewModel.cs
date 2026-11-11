// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.Globalization;

namespace AccessibilityInsights_VersionSwitcher_WinUI;

public sealed partial class UpdateViewModel(IUpdateService service, Func<string, string> strings) : ObservableObject
{
    [ObservableProperty]
    public partial int Progress { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; } = true;

    partial void OnProgressChanged(int value) =>
        Status = string.Format(CultureInfo.CurrentCulture, strings("ProgressIndicatorFormat"), value);

    [RelayCommand]
    private async Task RunAsync()
    {
        IsBusy = true;
        Status = string.Format(CultureInfo.CurrentCulture, strings("ProgressIndicatorFormat"), 0);
        try
        {
            await service.RunAsync(new Progress<int>(value => Progress = value));
        }
        catch (Exception exception)
        {
            Trace.TraceError("{0}", exception);
            Error = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
