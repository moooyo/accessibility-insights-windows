// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
namespace AccessibilityInsights.Core;

public interface IInspectionService : IAsyncDisposable
{
    IReadOnlyList<TargetWindow> GetWindows();
    Task<InspectionSnapshot> CaptureAsync(nint window, bool runTests);
    Task<InspectionSnapshot> OpenAsync(string path);
    Task SaveAsync(string path);
}
