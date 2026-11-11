// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
namespace AccessibilityInsights.WinUITestTarget;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var form = new Form
        {
            Text = "WinUI migration test target",
            Width = 480,
            Height = 260,
            StartPosition = FormStartPosition.CenterScreen,
        };
        var name = new TextBox { Name = "TargetName", AccessibleName = "Name", Text = "Migration fixture", Left = 24, Top = 24, Width = 240 };
        var save = new Button { Name = "TargetSave", Text = "Save", Left = 24, Top = 72, Width = 120 };
        // An intentionally unnamed control proves that the migrated UI displays real rule failures.
        var unnamed = new Button { Name = "UnnamedButton", Left = 160, Top = 72, Width = 120 };
        var swatch = new Panel
        {
            Name = "BlackColorSwatch",
            AccessibleName = "Black color swatch",
            BackColor = Color.Black,
            Left = 24,
            Top = 128,
            Width = 120,
            Height = 48,
        };
        var whiteSwatch = new Panel
        {
            Name = "WhiteColorSwatch",
            AccessibleName = "White color swatch",
            BackColor = Color.White,
            Left = 160,
            Top = 128,
            Width = 120,
            Height = 48,
        };
        form.Controls.AddRange([name, save, unnamed, swatch, whiteSwatch]);
        Application.Run(form);
    }
}
