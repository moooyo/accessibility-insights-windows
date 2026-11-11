// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace AccessibilityInsights.Core;

public interface IApplicationTelemetry
{
    bool IsEnabled { get; }
    void AddOrUpdateContextProperty(string propertyName, string propertyValue);
    void PublishEvent(string eventName, IReadOnlyDictionary<string, string>? properties = null);
    void ReportException(Exception exception, IReadOnlyDictionary<string, string>? properties = null);
    void FlushAndShutDown();
}
