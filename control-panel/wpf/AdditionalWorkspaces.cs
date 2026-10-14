using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;

// Additive multi-root Workspace support. The Node Gateway already supports
// semicolon-separated DESKTOP_MCP_ALLOWED_ROOTS and canonicalizes each root.
internal sealed partial class ControlPanelRuntime
{
    private readonly List<string> additionalWorkspaces = new List<string>();

    private static string NormalizeWorkspaceRoot(string candidate)
    {
        if (String.IsNullOrWhiteSpace(candidate)) throw new ArgumentException("Workspace must not be empty.");
        string expanded = Environment.ExpandEnvironmentVariables(candidate.Trim());
        if (expanded.Contains(";"))
            throw new ArgumentException("Workspace paths containing a semicolon cannot be used.");
        if (!Path.IsPathFullyQualified(expanded))
            throw new ArgumentException("Workspace must be an absolute folder path.");
        string fullPath = Path.GetFullPath(expanded);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException("Workspace folder does not exist: " + fullPath);
        string driveRoot = Path.GetPathRoot(fullPath);
        return String.Equals(fullPath, driveRoot, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private void LoadAdditionalWorkspaces(string[] paths)
    {
        additionalWorkspaces.Clear();
        if (paths == null) return;
        foreach (string path in paths)
        {
            try
            {
                string normalized = NormalizeWorkspaceRoot(path);
                if (!additionalWorkspaces.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    additionalWorkspaces.Add(normalized);
            }
            catch (Exception error)
            {
                LogRuntimeError("Ignoring unavailable additional Workspace.", error);
            }
        }
    }

    private List<string> GetAllowedWorkspaceRoots()
    {
        List<string> roots = new List<string>();
        // The primary selected Workspace must always exist (existing behavior).
        roots.Add(NormalizeWorkspaceRoot(currentWorkspace));
        foreach (string path in additionalWorkspaces)
        {
            string normalized = NormalizeWorkspaceRoot(path);
            if (!roots.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                roots.Add(normalized);
        }
        return roots;
    }

    private StackPanel authorizedWorkspaceList;
    private TextBlock authorizedWorkspaceHeading;

    // Display the complete effective Workspace list directly in Settings.
    // Keep the original primary Workspace / Change controls above this summary.
    private void RefreshAuthorizedWorkspaceList()
    {
        if (authorizedWorkspaceList == null || authorizedWorkspaceHeading == null) return;

        List<string> roots = new List<string>();
        if (!String.IsNullOrWhiteSpace(currentWorkspace)) roots.Add(currentWorkspace);
        foreach (string folder in additionalWorkspaces)
        {
            if (!String.IsNullOrWhiteSpace(folder) &&
                !roots.Contains(folder, StringComparer.OrdinalIgnoreCase))
                roots.Add(folder);
        }

        authorizedWorkspaceHeading.Text = "Authorized folders (" + roots.Count + ")";
        authorizedWorkspaceHeading.Foreground = BrushFrom(isDarkTheme ? "#FFF5F5F7" : "#FF27272A");
        authorizedWorkspaceList.Children.Clear();

        for (int i = 0; i < roots.Count; i++)
        {
            string folder = roots[i];
            bool primary = i == 0;
            bool available = Directory.Exists(folder);
            Border card = new Border
            {
                Background = BrushFrom(isDarkTheme ? "#FF2C2C30" : "#FFEBEBEF"),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6)
            };
            StackPanel details = new StackPanel();
            details.Children.Add(new TextBlock
            {
                Text = primary ? "PRIMARY" : "ADDITIONAL",
                FontWeight = FontWeights.SemiBold,
                FontSize = 9,
                Foreground = BrushFrom(isDarkTheme ? "#FFA1A1AA" : "#FF71717A"),
                Margin = new Thickness(0, 0, 0, 3)
            });
            details.Children.Add(new TextBlock
            {
                Text = folder,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = BrushFrom(isDarkTheme ? "#FFF5F5F7" : "#FF27272A"),
                ToolTip = folder
            });
            if (!available)
            {
                details.Children.Add(new TextBlock
                {
                    Text = "Folder currently unavailable",
                    FontSize = 10,
                    Foreground = BrushFrom(isDarkTheme ? "#FFFBBF75" : "#FF9A5500"),
                    Margin = new Thickness(0, 4, 0, 0)
                });
            }
            card.Child = details;
            authorizedWorkspaceList.Children.Add(card);
        }
    }

    private void InitializeAdditionalWorkspaceButton()
    {
        Border workspaceCard = Find<Border>("WorkspaceCard");
        if (workspaceCard == null || workspaceCard.Child == null) return;
        UIElement existingContent = workspaceCard.Child;
        workspaceCard.Child = null;

        StackPanel stack = new StackPanel();
        stack.Children.Add(existingContent);
        stack.Children.Add(new Border
        {
            Height = 1,
            Background = BrushFrom(isDarkTheme ? "#FF3F3F46" : "#FFE0E0E5"),
            Margin = new Thickness(0, 12, 0, 10)
        });
        authorizedWorkspaceHeading = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        stack.Children.Add(authorizedWorkspaceHeading);
        authorizedWorkspaceList = new StackPanel();
        stack.Children.Add(authorizedWorkspaceList);

        Button manager = new Button
        {
            Content = "Manage additional folders",
            Margin = new Thickness(0, 5, 0, 0),
            Style = window.FindResource("MiniButton") as Style,
            ToolTip = "Add or remove authorized folders from any disk"
        };
        manager.Click += delegate { ShowAdditionalWorkspaceManager(); };
        stack.Children.Add(manager);
        workspaceCard.Child = stack;
        RefreshAuthorizedWorkspaceList();
    }

    private void ShowAdditionalWorkspaceManager()
    {
        suppressAutoHide = true;
        Window dialog = new Window
        {
            Owner = window, Title = "DeskMCP · Workspace folders",
            Width = 570, Height = 470, MinWidth = 470, MinHeight = 370,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = BrushFrom(isDarkTheme ? "#FF232327" : "#FFFFFFFF")
        };
        StackPanel body = new StackPanel { Margin = new Thickness(18) };
        ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
        dialog.Content = scroll;
        Action refresh = null;
        refresh = delegate
        {
            RefreshAuthorizedWorkspaceList();
            body.Children.Clear();
            body.Children.Add(new TextBlock { Text = "Allowed Workspace folders", FontSize = 19, FontWeight = FontWeights.SemiBold });
            body.Children.Add(new TextBlock
            {
                Text = "Every selected folder is independently authorized. All connected Tunnels share the same Gateway and folder permissions.",
                FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12)
            });
            body.Children.Add(new TextBlock { Text = "Primary workspace (change in DeskMCP Settings):", FontSize = 11, FontWeight = FontWeights.SemiBold });
            body.Children.Add(new TextBlock { Text = currentWorkspace, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 5, 0, 12) });
            foreach (string folder in additionalWorkspaces.ToArray())
            {
                DockPanel row = new DockPanel { Margin = new Thickness(0, 3, 0, 7) };
                Button remove = new Button { Content = "Remove", Padding = new Thickness(12, 6, 12, 6),
                    Style = window.FindResource("MiniButton") as Style };
                remove.Click += delegate
                {
                    if (System.Windows.MessageBox.Show(dialog,
                        "Remove this folder's authorization? All Tunnel connections may briefly reconnect while Gateway restarts.",
                        "Remove workspace", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                    additionalWorkspaces.Remove(folder);
                    SaveSettings();
                    RestartGatewayAsync(selectedProfile);
                    refresh();
                };
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
                row.Children.Add(new TextBlock { Text = folder, TextWrapping = TextWrapping.Wrap, FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
                body.Children.Add(row);
            }
            Button add = new Button { Content = "+ Add a folder on C:, D:, G:, or another disk",
                Style = window.FindResource("MiniButton") as Style, Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 8, 0, 12) };
            add.Click += delegate
            {
                using (Forms.FolderBrowserDialog chooser = new Forms.FolderBrowserDialog())
                {
                    chooser.Description = "Select an additional DeskMCP Workspace root";
                    chooser.ShowNewFolderButton = false;
                    if (chooser.ShowDialog() != Forms.DialogResult.OK) return;
                    try
                    {
                        string folder = NormalizeWorkspaceRoot(chooser.SelectedPath);
                        if (GetAllowedWorkspaceRoots().Contains(folder, StringComparer.OrdinalIgnoreCase)) return;
                        if (System.Windows.MessageBox.Show(dialog,
                            "Allow all files under this folder for every connected Tunnel?\n\n" + folder +
                            "\n\nApplying this setting will restart the Gateway.",
                            "Confirm Workspace access", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                        additionalWorkspaces.Add(folder);
                        SaveSettingsCore();
                        RestartGatewayAsync(selectedProfile);
                        refresh();
                    }
                    catch (Exception error)
                    {
                        System.Windows.MessageBox.Show(dialog, error.Message, "Workspace error");
                    }
                }
            };
            body.Children.Add(add);
            body.Children.Add(new TextBlock { Text =
                "Security note: choose project folders rather than entire drives. Sensitive-path protections continue to apply unless Fully Unlocked is selected.",
                TextWrapping = TextWrapping.Wrap, FontSize = 10 });
        };
        dialog.Closed += delegate { suppressAutoHide = false; };
        refresh();
        dialog.ShowDialog();
    }
}
