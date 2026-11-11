// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using AccessibilityInsights.Models;
using AccessibilityInsights.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Xml.Linq;

namespace AccessibilityInsights.WinUITests;

[TestClass]
public sealed class GitHubIssueTests
{
    [TestMethod]
    public void DraftRetainsTheOriginalWpfIssueFormatAndEscapesQueryParametersIndependently()
    {
        var original = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestFiles", "GitHubResources.xml"));
        string Resource(string key) => original.Root!.Elements("data")
            .Single(entry => (string?)entry.Attribute("name") == key).Element("value")!.Value;
        foreach (var key in new[] { "NoFailureIssueTitle", "NoFailureIssueBody", "SingleFailureIssueTitle", "SingleFailureIssueBody" })
        {
            Assert.AreEqual(Resource(key), TestResources.GetString(key));
        }
        var rule = new RuleRow("Fail", "A & B #C", "Name is missing", "Set an accessible name.",
            "WCAG 4.1.2", "https://example.com/help?x=1&y=2", "Button: A & B #C");
        var draft = GitHubIssueService.CreateDraft("Test app", "Window", rule, TestResources.GetString);
        Assert.AreEqual("WCAG 4.1.2: (Test app/Button: A & B #C) Name is missing", draft.Title);
        StringAssert.Contains(draft.Body, "**App:** Test app");
        StringAssert.Contains(draft.Body, "**How To Fix:** Set an accessible name.");
        var uri = GitHubIssueService.GetNewIssueUri("https://github.com/owner/repo/", draft);
        var values = uri.Query.TrimStart('?').Split('&')
            .Select(pair => pair.Split('=', 2)).ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));
        Assert.AreEqual(draft.Title, values["title"]);
        Assert.AreEqual(draft.Body, values["body"]);
        Assert.AreEqual(string.Empty, uri.Fragment);
        var manual = GitHubIssueService.CreateDraft("Test app", "Window", null, TestResources.GetString);
        StringAssert.Contains(manual.Title, "needs investigation");
        StringAssert.Contains(manual.Body, "**Issue Details:** []");
    }

    [DataTestMethod]
    [DataRow("https://github.com/owner/repo?body=unexpected")]
    [DataRow("https://github.com/owner/repo#unexpected")]
    [DataRow("https://github.com.evil.example/owner/repo")]
    [DataRow("http://github.com/owner/repo")]
    [DataRow("https://github.com/owner/..")]
    [DataRow("https://github.com/owner/repo/issues")]
    public void InvalidRepositoriesCannotAlterTheIssueDestination(string repository)
    {
        Assert.ThrowsException<ArgumentException>(() => GitHubIssueService.ValidateRepository(repository));
    }
}
