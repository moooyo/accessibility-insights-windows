// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using Axe.Windows.Core.Bases;
using Axe.Windows.Core.Types;
using System.Drawing;

namespace AccessibilityInsights.Core;

internal static class AxePropertyCompatibility
{
    internal static void PrepareForCleanup(A11yElement element)
    {
        if (element.Properties is not null &&
            element.Properties.TryGetValue(PropertyType.UIA_ClickablePointPropertyId, out var property) &&
            property is not ManagedPointProperty &&
            property.Value is Point point)
        {
            element.Properties[property.Id] = new ManagedPointProperty(property, point);
            property.Value = null;
            property.Dispose();
        }
    }

    private sealed class ManagedPointProperty(A11yProperty property, Point point)
        : A11yProperty(property.Id, point, property.Name)
    {
        protected override void Dispose(bool disposing)
        {
            // Axe.Windows calls VariantClear on Point, which cannot be marshalled as a COM record on .NET.
            if (disposing)
            {
                Value = null;
            }

            base.Dispose(disposing);
        }
    }
}
