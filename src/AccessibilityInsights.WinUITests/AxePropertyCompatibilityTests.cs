// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using Axe.Windows.Core.Bases;
using Axe.Windows.Core.Types;
using Axe.Windows.Desktop.UIAutomation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System.Drawing;

namespace AccessibilityInsights.WinUITests;

[TestClass]
public sealed class AxePropertyCompatibilityTests
{
    [TestMethod]
    public void ClickablePointKeepsItsValueAndLegacyJsonButCanBeClearedRepeatedly()
    {
        var property = new A11yProperty(PropertyType.UIA_ClickablePointPropertyId, new Point(12, 34));
        var text = property.TextValue;
        var json = JsonConvert.SerializeObject(property);
        var element = new A11yElement
        {
            Properties = new Dictionary<int, A11yProperty> { [property.Id] = property },
        };

        AxePropertyCompatibility.PrepareForCleanup(element);
        AxePropertyCompatibility.PrepareForCleanup(element);

        var compatible = element.Properties[property.Id];
        Assert.AreEqual(new Point(12, 34), (Point)compatible.Value);
        Assert.AreEqual(text, compatible.TextValue);
        Assert.AreEqual(json, JsonConvert.SerializeObject(compatible));
        element.Clear();
        compatible.Dispose();
        Assert.IsNull(element.Properties);
        Assert.IsNull((object?)compatible.Value);
    }
}
