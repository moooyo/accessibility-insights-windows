// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
namespace AccessibilityInsights.Core;

public interface ISnapshotFilePicker
{
    Task<string?> OpenAsync();
    Task<string?> SaveAsync();
}
