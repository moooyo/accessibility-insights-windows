// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System.Xml.Linq;

namespace AccessibilityInsights.WinUITests;

internal static class TestResources
{
    private static readonly IReadOnlyDictionary<string, string> Strings =
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestFiles", "WinUIResources.xml"))
            .Root!.Elements("data").ToDictionary(entry => (string)entry.Attribute("name")!, entry => entry.Element("value")!.Value);

    public static string GetString(string key) => Strings[key];
}
