// Smallway Desktop MCP Project Connections V1.0 (2026-10-20).
// Local-only, explicitly user-confirmed connection enrollment and per-operation approval.
// Never copy secret values into the registry, dialog, message boxes, logs or MCP replies.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;

internal sealed class ProjectConnectionRegistry
{
    public int version { get; set; } = 1;
    public List<ProjectConnectionEntry> connections { get; set; } = new List<ProjectConnectionEntry>();
}
internal sealed class ProjectConnectionEntry
{
    public string id { get; set; }
    public string project_path { get; set; }
    public string source_file { get; set; }
    public string section { get; set; } = "";
    public string kind { get; set; }
    public string host { get; set; }
    public string username { get; set; } = "";
    public string secret_label { get; set; } = "";
    public string host_key_sha256 { get; set; } = "";
    public string approved_utc { get; set; }
    public string Display { get { return kind + " • " + host + " • " + Path.GetFileName(source_file); } }
}
internal sealed class ProjectMdChoice
{
    public string FullPath { get; set; }
    public string Display { get; set; }
}
internal sealed class ProjectPendingChoice
{
    public string FullPath { get; set; }
    public string RawJson { get; set; }
    public string Display { get; set; }
}

internal sealed partial class ControlPanelRuntime
{
    private static string ProjectConnectionsStore =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "DesktopMCP", "project-connections");
    private static string ProjectConnectionsRegistryFile =>
        Path.Combine(ProjectConnectionsStore, "registry.json");

    private void InitializeProjectConnectionsButton()
    {
        Find<Button>("ProjectConnectionsManageButton").Click += delegate { ShowProjectConnectionsUi(); };
    }

    private static void SafeSaveRegistry(ProjectConnectionRegistry registry)
    {
        Directory.CreateDirectory(ProjectConnectionsStore);
        string file = ProjectConnectionsRegistryFile;
        string tmp = Path.Combine(ProjectConnectionsStore, Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(tmp, JsonSerializer.Serialize(registry,
            new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        File.Move(tmp, file, true);
    }

    private static ProjectConnectionRegistry LoadLocalRegistry()
    {
        if (!File.Exists(ProjectConnectionsRegistryFile)) return new ProjectConnectionRegistry();
        FileInfo fi = new FileInfo(ProjectConnectionsRegistryFile);
        if (fi.Length > 256 * 1024) throw new InvalidDataException("Connection registry is too large.");
        ProjectConnectionRegistry registry = JsonSerializer.Deserialize<ProjectConnectionRegistry>(
            File.ReadAllText(ProjectConnectionsRegistryFile, Encoding.UTF8));
        if (registry == null || registry.version != 1 || registry.connections == null)
            throw new InvalidDataException("Connection registry format is invalid.");
        return registry;
    }

    private static bool IsInside(string folder, string child)
    {
        string prefix = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(child).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
    private static List<ProjectMdChoice> FindProjectMdFiles(string folder)
    {
        string root = Path.GetFullPath(folder);
        List<ProjectMdChoice> found = new List<ProjectMdChoice>();
        Queue<Tuple<string, int>> queue = new Queue<Tuple<string, int>>();
        queue.Enqueue(Tuple.Create(root, 0));
        string[] omit = { ".git", "node_modules", ".venv", "dist", "build", "runtime", "_backup_code", "backup", "backups" };
        while (queue.Count > 0 && found.Count < 150)
        {
            var item = queue.Dequeue();
            foreach (string child in Directory.EnumerateFileSystemEntries(item.Item1))
            {
                if (!IsInside(root, child)) continue;
                FileAttributes attrs;
                try { attrs = File.GetAttributes(child); } catch { continue; }
                if ((attrs & FileAttributes.ReparsePoint) != 0) continue;
                if ((attrs & FileAttributes.Directory) != 0)
                {
                    if (item.Item2 < 3 && queue.Count < 150 &&
                        !omit.Contains(Path.GetFileName(child).ToLowerInvariant()))
                        queue.Enqueue(Tuple.Create(child, item.Item2 + 1));
                }
                else if (Path.GetExtension(child).Equals(".md", StringComparison.OrdinalIgnoreCase))
                {
                    FileInfo info = new FileInfo(child);
                    if (info.Length <= 256 * 1024)
                        found.Add(new ProjectMdChoice {
                            FullPath = info.FullName,
                            Display = Path.GetRelativePath(root, info.FullName)
                        });
                    if (found.Count >= 150) break;
                }
            }
        }
        return found.OrderBy(e => e.Display, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void ShowProjectConnectionsUi()
    {
        Window dialog = new Window {
            Title = "Smallway Desktop MCP · Project Connections", Owner = window,
            Width = 690, Height = 775, MinWidth = 570, MinHeight = 575,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(248, 249, 251))
        };
        StackPanel stack = new StackPanel { Margin = new Thickness(20) };
        ScrollViewer scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroller.Content = stack; dialog.Content = scroller;
        Func<string, TextBlock> header = title => {
            TextBlock label = new TextBlock {
                Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Black, Margin = new Thickness(0, 12, 0, 5)
            };
            stack.Children.Add(label); return label;
        };
        Func<string, string, TextBox> input = (label, initial) => {
            header(label);
            TextBox box = new TextBox { Text = initial, FontSize = 12, Padding = new Thickness(8, 5, 8, 5) };
            stack.Children.Add(box); return box;
        };
        Func<string, Button> button = label => new Button {
            Content = label, Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(4, 5, 4, 5), FontSize = 12
        };
        TextBlock warning = new TextBlock {
            Text = "Only local MD references are saved. SSH/API secrets are never displayed. " +
                   "Every non-read-only remote operation requires a separate local confirmation.",
            Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, FontSize = 11
        };
        stack.Children.Add(warning);
        // 2026-10-20: Require an explicit project selection; never default-scan an entire Home/Workspace.
        TextBox folder = input("* Project folder (search only inside this folder)", "");
        StackPanel folderControls = new StackPanel { Orientation = Orientation.Horizontal };
        Button browse = button("Choose folder");
        Button scan = button("Scan MD names");
        folderControls.Children.Add(browse); folderControls.Children.Add(scan);
        stack.Children.Add(folderControls);
        ListBox files = new ListBox { Height = 100, DisplayMemberPath = "Display" };
        stack.Children.Add(files);
        header("Connection details — confirm them from your MD locally");
        ComboBox kind = new ComboBox { Margin = new Thickness(0, 3, 0, 5) };
        foreach (string name in new[] { "ssh-password", "ssh-key", "cloudflare-token", "wordpress-app-password" })
            kind.Items.Add(name);
        kind.SelectedIndex = 0; stack.Children.Add(kind);
        TextBox host = input("Approved host (SSH hostname/IP, or site domain; Cloudflare: api.cloudflare.com)", "");
        TextBox user = input("Approved username (SSH/WordPress; optional for Cloudflare)", "");
        TextBox section = input("Markdown heading (optional; useful when MD contains multiple accounts)", "");
        TextBox secretLabel = input("Exact MD password/token field name (optional if unambiguous)", "");
        TextBox fingerprint = input("SSH verified server host key fingerprint (SHA256:..., required for SSH)", "");
        Button add = button("Approve selected MD connection");
        stack.Children.Add(add);
        header("Already approved (three ChatGPT tunnels share this local list)");
        ListBox approved = new ListBox { Height = 80, DisplayMemberPath = "Display" };
        stack.Children.Add(approved);
        Button remove = button("Revoke selected connection");
        stack.Children.Add(remove);
        header("Pending SSH / API operation approvals");
        ListBox pending = new ListBox { Height = 88, DisplayMemberPath = "Display" };
        stack.Children.Add(pending);
        TextBox preview = new TextBox {
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 85, FontSize = 11
        };
        stack.Children.Add(preview);
        StackPanel pendingButtons = new StackPanel { Orientation = Orientation.Horizontal };
        Button refresh = button("Refresh requests");
        Button approve = button("Approve exact operation");
        Button reject = button("Reject request");
        pendingButtons.Children.Add(refresh); pendingButtons.Children.Add(approve); pendingButtons.Children.Add(reject);
        stack.Children.Add(pendingButtons);
        TextBlock status = new TextBlock {
            Text = UiLocalization.Translate("Nothing has been scanned or approved."),
            Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap,
            FontSize = 11, Margin = new Thickness(0, 10, 0, 10)
        };
        stack.Children.Add(status);
        Action refreshApproved = () => {
            approved.ItemsSource = null;
            approved.ItemsSource = LoadLocalRegistry().connections;
        };
        Action refreshPending = () => {
            var list = new List<ProjectPendingChoice>();
            string pendingDir = Path.Combine(ProjectConnectionsStore, "pending");
            if (Directory.Exists(pendingDir))
            {
                foreach (string file in Directory.EnumerateFiles(pendingDir, "*.json").Take(100))
                {
                    try {
                        string raw = File.ReadAllText(file, Encoding.UTF8);
                        JsonNode node = JsonNode.Parse(raw);
                        if (node == null || node["approved"]?.GetValue<bool>() == true) continue;
                        DateTime created = DateTime.Parse(node["created"]!.GetValue<string>()).ToUniversalTime();
                        if (DateTime.UtcNow - created > TimeSpan.FromMinutes(15)) continue;
                        string summary = (node["type"]?.ToString() ?? "") + " / " +
                            (node["connection_id"]?.ToString() ?? "") + " / " +
                            Path.GetFileNameWithoutExtension(file);
                        list.Add(new ProjectPendingChoice { FullPath = file, RawJson = raw, Display = summary });
                    }
                    catch { /* Invalid requests remain unapproved. */ }
                }
            }
            pending.ItemsSource = null; pending.ItemsSource = list;
        };
        browse.Click += delegate {
            using (Forms.FolderBrowserDialog picker = new Forms.FolderBrowserDialog()) {
                picker.SelectedPath = Directory.Exists(folder.Text) ? folder.Text : currentWorkspace;
                picker.Description = UiLocalization.Translate("Choose one project folder. No other folders will be scanned.");
                if (picker.ShowDialog() == Forms.DialogResult.OK) folder.Text = picker.SelectedPath;
            }
        };
        scan.Click += delegate {
            try {
                if (!Directory.Exists(folder.Text)) throw new InvalidOperationException("Folder not found.");
                var candidates = FindProjectMdFiles(folder.Text);
                files.ItemsSource = candidates;
                status.Text = UiLocalization.Translate("Found ") + candidates.Count + UiLocalization.Translate(" Markdown files (names only, no credentials read).");
            } catch (Exception ex) { status.Text = "Scan failed: " + ex.Message; }
        };
        add.Click += delegate {
            try {
                ProjectMdChoice selected = files.SelectedItem as ProjectMdChoice;
                if (selected == null) throw new InvalidOperationException("Choose an MD file from the scan results.");
                string project = Path.GetFullPath(folder.Text);
                string file = Path.GetFullPath(selected.FullPath);
                if (!IsInside(project, file) || Path.GetExtension(file).ToLowerInvariant() != ".md")
                    throw new InvalidOperationException("MD must reside inside the selected project.");
                string type = kind.SelectedItem as string;
                string target = host.Text.Trim().ToLowerInvariant();
                string account = user.Text.Trim();
                if (target.Length == 0 || target.Length > 253 ||
                    target.Contains("/") || target.Contains("\\") || target.Contains("@") ||
                    target.Contains(" ") || target.Contains(":"))
                    throw new InvalidOperationException("Enter only the approved hostname/IP, without URL or port.");
                if ((type == "ssh-password" || type == "ssh-key" || type == "wordpress-app-password") && account.Length == 0)
                    throw new InvalidOperationException("SSH / WordPress requires a username.");
                string pin = fingerprint.Text.Trim();
                if (type.StartsWith("ssh-") && !Regex.IsMatch(pin, @"^SHA256:[A-Za-z0-9+/]{43}$"))
                    throw new InvalidOperationException("SSH requires the externally verified SHA256 host key fingerprint.");
                if (type == "cloudflare-token" && target != "api.cloudflare.com")
                    throw new InvalidOperationException("Cloudflare API host must be api.cloudflare.com.");
                ProjectConnectionRegistry registry = LoadLocalRegistry();
                if (registry.connections.Any(e => e.source_file.Equals(file, StringComparison.OrdinalIgnoreCase) &&
                    e.kind == type && e.host.Equals(target, StringComparison.OrdinalIgnoreCase) &&
                    e.username == account && e.section == section.Text.Trim()))
                    throw new InvalidOperationException("This connection is already approved.");
                if (System.Windows.MessageBox.Show(dialog,
                    UiLocalization.Translate("Approve this exact MD source and target?") + "\n\n" + file + "\n\n" + type + " / " + target + " / " + account +
                    "\n\n" + UiLocalization.Translate("No passwords will be copied into Smallway Desktop MCP's registry."),
                    UiLocalization.Translate("Approve project connection"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                registry.connections.Add(new ProjectConnectionEntry {
                    id = Guid.NewGuid().ToString("D"), project_path = project, source_file = file,
                    section = section.Text.Trim(), kind = type, host = target, username = account,
                    secret_label = secretLabel.Text.Trim(), host_key_sha256 = pin,
                    approved_utc = DateTime.UtcNow.ToString("O")
                });
                SafeSaveRegistry(registry);
                refreshApproved();
                status.Text = UiLocalization.Translate("Approved and saved locally. No credentials were copied.");
            } catch (Exception ex) { status.Text = "Approval failed: " + ex.Message; }
        };
        remove.Click += delegate {
            ProjectConnectionEntry selected = approved.SelectedItem as ProjectConnectionEntry;
            if (selected == null) return;
            if (System.Windows.MessageBox.Show(dialog, UiLocalization.Translate("Revoke this connection for all tunnels?"), UiLocalization.Translate("Revoke"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try {
                ProjectConnectionRegistry registry = LoadLocalRegistry();
                registry.connections.RemoveAll(e => e.id == selected.id);
                SafeSaveRegistry(registry); refreshApproved(); status.Text = UiLocalization.Translate("Connection revoked.");
            } catch (Exception ex) { status.Text = "Revoke failed: " + ex.Message; }
        };
        refresh.Click += delegate { try { refreshPending(); } catch (Exception ex) { status.Text = ex.Message; } };
        pending.SelectionChanged += delegate {
            ProjectPendingChoice selected = pending.SelectedItem as ProjectPendingChoice;
            if (selected == null) { preview.Text = ""; return; }
            try {
                JsonNode n = JsonNode.Parse(selected.RawJson);
                preview.Text = UiLocalization.Translate("Type: ") + n["type"] + "\n" + UiLocalization.Translate("SSH command: ") + n["command"] +
                    "\nAPI method/path: " + n["method"] + " " + n["api_path"] +
                    "\nRequest body:\n" + n["body"];
            } catch { preview.Text = UiLocalization.Translate("Invalid pending request."); }
        };
        approve.Click += delegate {
            ProjectPendingChoice selected = pending.SelectedItem as ProjectPendingChoice;
            if (selected == null) return;
            try {
                string current = File.ReadAllText(selected.FullPath, Encoding.UTF8);
                if (current != selected.RawJson) throw new InvalidOperationException("Request changed. Refresh before approval.");
                JsonNode n = JsonNode.Parse(current);
                DateTime created = DateTime.Parse(n["created"]!.GetValue<string>()).ToUniversalTime();
                if (DateTime.UtcNow - created > TimeSpan.FromMinutes(15)) throw new InvalidOperationException("Request expired.");
                if (n["approved"]!.GetValue<bool>()) throw new InvalidOperationException("Already approved.");
                if (System.Windows.MessageBox.Show(dialog,
                    UiLocalization.Translate("Approve exactly the command / HTTP operation shown in the preview above?\n\nIt may modify a remote production system."),
                    UiLocalization.Translate("Approve remote operation"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                n["approved"] = true; n["approved_utc"] = DateTime.UtcNow.ToString("O");
                string tmp = selected.FullPath + ".tmp";
                File.WriteAllText(tmp, n.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
                File.Move(tmp, selected.FullPath, true);
                status.Text = UiLocalization.Translate("Approved for one execution. Return to ChatGPT and resume the request.");
                refreshPending();
            } catch (Exception ex) { status.Text = "Request approval failed: " + ex.Message; }
        };
        reject.Click += delegate {
            ProjectPendingChoice selected = pending.SelectedItem as ProjectPendingChoice;
            if (selected == null) return;
            try {
                File.Move(selected.FullPath, selected.FullPath + ".rejected");
                status.Text = UiLocalization.Translate("Request rejected without execution.");
                refreshPending();
            } catch (Exception ex) { status.Text = "Reject failed: " + ex.Message; }
        };
        try { refreshApproved(); refreshPending(); } catch (Exception ex) { status.Text = "Configuration error: " + ex.Message; }
        // 2026-10-29: Localize the whole modal immediately rather than awaiting the 2-second timer.
        UiLocalization.Apply(dialog);
        dialog.ShowDialog();
    }
}
