// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using Microsoft.UI.Dispatching;

namespace AccessibilityInsights_WinUI.Services;

internal sealed class DispatcherQueueService(DispatcherQueue queue) : IUiDispatcher
{
    public bool HasThreadAccess => queue.HasThreadAccess;

    public bool TryEnqueue(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (queue.HasThreadAccess)
        {
            callback();
            return true;
        }

        return queue.TryEnqueue(() => callback());
    }

    public Task EnqueueAsync(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (queue.HasThreadAccess)
        {
            try
            {
                callback();
                return Task.CompletedTask;
            }
            catch (Exception exception)
            {
                return Task.FromException(exception);
            }
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!queue.TryEnqueue(() =>
            {
                try
                {
                    callback();
                    completion.SetResult();
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            }))
        {
            completion.SetException(new InvalidOperationException("The application dispatcher is shutting down."));
        }

        return completion.Task;
    }

    public Task<T> EnqueueAsync<T>(Func<Task<T>> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (queue.HasThreadAccess)
        {
            return callback();
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!queue.TryEnqueue(async () =>
            {
                try
                {
                    completion.SetResult(await callback());
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            }))
        {
            completion.SetException(new InvalidOperationException("The application dispatcher is shutting down."));
        }

        return completion.Task;
    }
}
