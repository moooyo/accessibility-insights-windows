// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
namespace AccessibilityInsights.Core;

public interface IUiDispatcher
{
    bool HasThreadAccess { get; }
    bool TryEnqueue(Action callback);
    Task EnqueueAsync(Action callback);
}
