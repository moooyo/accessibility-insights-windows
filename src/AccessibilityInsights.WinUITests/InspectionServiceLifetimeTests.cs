// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AccessibilityInsights.WinUITests;

[TestClass]
[DoNotParallelize]
public sealed class InspectionServiceLifetimeTests
{
    [TestMethod]
    public async Task DisposeAsyncIsSafeForConcurrentAndRepeatedInterfaceAliases()
    {
        var service = new InspectionService();
        IInspectionService inspection = service;
        IRecordingService recording = service;

        await Task.WhenAll(
            ((IAsyncDisposable)inspection).DisposeAsync().AsTask(),
            ((IAsyncDisposable)recording).DisposeAsync().AsTask(),
            service.DisposeAsync().AsTask());
        await ((IAsyncDisposable)inspection).DisposeAsync();
        await ((IAsyncDisposable)recording).DisposeAsync();
    }
}
