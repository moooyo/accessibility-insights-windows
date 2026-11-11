// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.Globalization;

namespace AccessibilityInsights.Core;

public readonly record struct RgbColor(byte Red, byte Green, byte Blue);

public interface IScreenColorPicker
{
    Task<RgbColor?> PickAsync(CancellationToken cancellationToken);
}

public sealed partial class ContrastViewModel : ObservableObject
{
    private readonly IScreenColorPicker picker;
    private readonly Func<string, string> strings;

    public ContrastViewModel(IScreenColorPicker picker, Func<string, string> strings)
    {
        this.picker = picker;
        this.strings = strings;
        UpdateResult();
    }

    [ObservableProperty]
    public partial RgbColor Foreground { get; set; }

    [ObservableProperty]
    public partial RgbColor Background { get; set; } = new(255, 255, 255);

    [ObservableProperty]
    public partial string Result { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string Error { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PickForegroundCommand))]
    [NotifyCanExecuteChangedFor(nameof(PickBackgroundCommand))]
    public partial bool IsPicking { get; set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    partial void OnForegroundChanged(RgbColor value) => UpdateResult();
    partial void OnBackgroundChanged(RgbColor value) => UpdateResult();

    private void UpdateResult()
    {
        var ratio = ColorContrast.Calculate(Foreground.Red, Foreground.Green, Foreground.Blue,
            Background.Red, Background.Green, Background.Blue);
        Result = string.Format(CultureInfo.CurrentCulture, strings("ContrastResultDetailed"),
            Math.Truncate(ratio * 1000) / 1000,
            strings(ratio >= 4.5 ? "Pass" : "Fail"),
            strings(ratio >= 3 ? "Pass" : "Fail"),
            strings(ratio >= 7 ? "Pass" : "Fail"));
    }

    private bool CanPick() => !IsPicking;

    [RelayCommand(CanExecute = nameof(CanPick), IncludeCancelCommand = true)]
    private Task PickForegroundAsync(CancellationToken cancellationToken) => PickAsync(true, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanPick), IncludeCancelCommand = true)]
    private Task PickBackgroundAsync(CancellationToken cancellationToken) => PickAsync(false, cancellationToken);

    private async Task PickAsync(bool foreground, CancellationToken cancellationToken)
    {
        IsPicking = true;
        Error = string.Empty;
        Status = strings("ScreenPickInstructions");
        try
        {
            var color = await picker.PickAsync(cancellationToken);
            if (color is { } selected)
            {
                if (foreground)
                {
                    Foreground = selected;
                }
                else
                {
                    Background = selected;
                }
            }

            Status = strings(color is null ? "ScreenPickCancelled" : "ScreenPickComplete");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Status = strings("ScreenPickCancelled");
        }
        catch (Exception exception)
        {
            Trace.TraceError("{0}", exception);
            Error = exception.Message;
            Status = strings("ScreenPickFailed");
        }
        finally
        {
            IsPicking = false;
        }
    }
}
