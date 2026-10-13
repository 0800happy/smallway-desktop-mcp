using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;

// The original Tunnel remains managed by DeskMCPControlPanel.cs.
// These are independent additional Tunnels sharing the same local MCP Gateway.
// Keys are never stored in settings.json or command-line arguments.
internal sealed class AdditionalTunnelDefinition
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string TunnelId { get; set; }
    public bool AutoStart { get; set; }
}

internal sealed partial class ControlPanelRuntime
{
    private const int MaxTotalTunnelChannels = 3;
    private static readonly byte[] AdditionalTunnelEntropy = Encoding.UTF8.GetBytes("DeskMCP.additional-tunnel.dpapi.v1");
    private readonly List<AdditionalTunnelDefinition> additionalTunnels = new List<AdditionalTunnelDefinition>();
    private readonly Dictionary<string, Process> additionalTunnelProcesses = new Dictionary<string, Process>();
    private readonly Dictionary<string, DateTime> additionalTunnelNextRetry = new Dictionary<string, DateTime>();
    private bool additionalTunnelsEnabled = true;

    private string AdditionalTunnelRoot
    {
        get { return Path.Combine(dataRoot, "additional-tunnels"); }
    }

    private string AdditionalTunnelSecret(AdditionalTunnelDefinition entry)
    {
        return Path.Combine(AdditionalTunnelRoot, entry.Id + ".key.dpapi");
    }

    private string AdditionalTunnelPid(AdditionalTunnelDefinition entry)
    {
        return Path.Combine(AdditionalTunnelRoot, entry.Id + ".pid");
    }

    private string AdditionalTunnelHealthFile(AdditionalTunnelDefinition entry)
    {
        return Path.Combine(AdditionalTunnelRoot, entry.Id + ".url");
    }

    private string AdditionalTunnelLog(AdditionalTunnelDefinition entry)
    {
        return Path.Combine(AdditionalTunnelRoot, entry.Id + ".log");
    }

    private static bool IsSafeAdditionalTunnelEntry(AdditionalTunnelDefinition entry)
    {
        return entry != null &&
            Guid.TryParseExact(entry.Id, "N", out _) &&
            IsValidTunnelId(entry.TunnelId) &&
            !String.IsNullOrWhiteSpace(entry.Name);
    }

    // Non-interactive, side-effect-free checks for configuration and DPAPI.
    public static int RunAdditionalChannelsSelfTest()
    {
        try
        {
            string validTunnel = "tunnel_" + new string('a', 32);
            AdditionalTunnelDefinition valid = new AdditionalTunnelDefinition {
                Id = Guid.NewGuid().ToString("N"), Name = "Third account",
                TunnelId = validTunnel, AutoStart = false
            };
            if (!IsSafeAdditionalTunnelEntry(valid)) return 1;
            if (IsSafeAdditionalTunnelEntry(new AdditionalTunnelDefinition {
                Id = "not-a-guid", Name = "Invalid", TunnelId = validTunnel
            })) return 2;
            if (IsSafeAdditionalTunnelEntry(new AdditionalTunnelDefinition {
                Id = Guid.NewGuid().ToString("N"), Name = "Invalid", TunnelId = "tunnel_invalid"
            })) return 3;
            byte[] plain = Encoding.UTF8.GetBytes("do-not-store-this-test-secret");
            byte[] cipher = ProtectedData.Protect(plain, AdditionalTunnelEntropy, DataProtectionScope.CurrentUser);
            byte[] actual = ProtectedData.Unprotect(cipher, AdditionalTunnelEntropy, DataProtectionScope.CurrentUser);
            try
            {
                if (!CryptographicOperations.FixedTimeEquals(plain, actual)) return 4;
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
                Array.Clear(cipher, 0, cipher.Length);
                Array.Clear(actual, 0, actual.Length);
            }
            string root = NormalizeWorkspaceRoot("%USERPROFILE%");
            if (!Directory.Exists(root)) return 5;
            return 0;
        }
        catch { return 9; }
    }

    private void LoadAdditionalTunnelSettings(List<AdditionalTunnelDefinition> records)
    {
        additionalTunnels.Clear();
        if (records == null) return;
        HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> tunnelIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (AdditionalTunnelDefinition entry in records)
        {
            if (!IsSafeAdditionalTunnelEntry(entry) ||
                !ids.Add(entry.Id) || !tunnelIds.Add(entry.TunnelId) ||
                String.Equals(entry.TunnelId, tunnelId, StringComparison.Ordinal)) continue;
            additionalTunnels.Add(entry);
            if (additionalTunnels.Count >= MaxTotalTunnelChannels - 1) break;
        }
    }

    private void SaveAdditionalTunnelKey(AdditionalTunnelDefinition entry, string key)
    {
        if (String.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Runtime API Key is required.");
        Directory.CreateDirectory(AdditionalTunnelRoot);
        byte[] plain = Encoding.UTF8.GetBytes(key);
        byte[] encrypted = null;
        try
        {
            encrypted = ProtectedData.Protect(plain, AdditionalTunnelEntropy, DataProtectionScope.CurrentUser);
            RuntimeReliability.WriteAllBytesAtomic(AdditionalTunnelSecret(entry), encrypted, false);
            File.SetAttributes(AdditionalTunnelSecret(entry), FileAttributes.Hidden);
        }
        finally
        {
            Array.Clear(plain, 0, plain.Length);
            if (encrypted != null) Array.Clear(encrypted, 0, encrypted.Length);
        }
    }

    private string LoadAdditionalTunnelKey(AdditionalTunnelDefinition entry)
    {
        byte[] encrypted = File.ReadAllBytes(AdditionalTunnelSecret(entry));
        byte[] plain = null;
        try
        {
            plain = ProtectedData.Unprotect(encrypted, AdditionalTunnelEntropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        finally
        {
            Array.Clear(encrypted, 0, encrypted.Length);
            if (plain != null) Array.Clear(plain, 0, plain.Length);
        }
    }

    private bool AdditionalTunnelRunning(AdditionalTunnelDefinition entry)
    {
        if (!additionalTunnelProcesses.TryGetValue(entry.Id, out Process process)) return false;
        try { return process != null && !process.HasExited; }
        catch { return false; }
    }

    // A surviving PID from "Quit Panel (Keep Services Running)" is never killed
    // by this panel; it must not be mistaken for a newly owned Process object.
    private bool AdditionalTunnelDetached(AdditionalTunnelDefinition entry)
    {
        if (AdditionalTunnelRunning(entry)) return false;
        try
        {
            string file = AdditionalTunnelPid(entry);
            if (!File.Exists(file)) return false;
            if (!Int32.TryParse(File.ReadAllText(file).Trim(), out int pid)) return false;
            using (Process p = Process.GetProcessById(pid))
                return !p.HasExited && String.Equals(p.ProcessName, "tunnel-client", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private string AdditionalTunnelStatus(AdditionalTunnelDefinition entry)
    {
        bool owned = AdditionalTunnelRunning(entry);
        if (!owned && AdditionalTunnelDetached(entry)) return "Running externally";
        if (!owned) return "Stopped";
        try
        {
            string healthPath = AdditionalTunnelHealthFile(entry);
            if (!File.Exists(healthPath)) return "Connecting";
            string url = File.ReadAllText(healthPath).Trim().TrimEnd('/');
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri endpoint) ||
                !String.Equals(endpoint.Host, "127.0.0.1", StringComparison.Ordinal))
                return "Connecting";
            bool readyOk = false;
            string readyBody = null, statusJson = null, metricsText = null;
            using (HttpResponseMessage response = StatusHttpClient.GetAsync(url + "/readyz").GetAwaiter().GetResult())
            {
                readyOk = response.IsSuccessStatusCode;
                readyBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            }
            using (HttpResponseMessage response = StatusHttpClient.GetAsync(url + "/api/status").GetAwaiter().GetResult())
            {
                statusJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    statusJson = "status 401 unauthorized " + statusJson;
            }
            using (HttpResponseMessage response = StatusHttpClient.GetAsync(url + "/metrics").GetAwaiter().GetResult())
            {
                if (response.IsSuccessStatusCode)
                    metricsText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            }
            TunnelRuntimeStatus status = TunnelRuntimeStatusEvaluator.Evaluate(
                entry.TunnelId, readyOk, readyBody, statusJson, metricsText, DateTimeOffset.UtcNow);
            if (status.Ready) return "Ready";
            if (status.CredentialRejected) return "Runtime key rejected";
            if (status.TunnelIdMismatch) return "Tunnel ID mismatch";
            return "Connecting";
        }
        catch { return "Connecting"; }
    }

    private void StartAdditionalTunnel(AdditionalTunnelDefinition entry)
    {
        if (TunnelDisabledByEnvironment() || quitting) throw new InvalidOperationException("Tunnel starting is disabled.");
        if (!additionalTunnelsEnabled) throw new InvalidOperationException("Additional Tunnels are disabled.");
        if (!IsSafeAdditionalTunnelEntry(entry)) throw new InvalidOperationException("Invalid additional Tunnel configuration.");
        if (!File.Exists(AdditionalTunnelSecret(entry))) throw new FileNotFoundException("Missing protected runtime key.");
        if (AdditionalTunnelRunning(entry)) return;
        if (AdditionalTunnelDetached(entry))
            throw new InvalidOperationException("A detached Tunnel is still running. Stop it from its original launcher before restarting.");
        HealthInfo gateway = GetGatewayHealth();
        if (gateway == null || (gateway.desktopRuntime != null && !gateway.desktopRuntime.ready))
            throw new InvalidOperationException("Gateway is not ready.");
        Directory.CreateDirectory(AdditionalTunnelRoot);
        foreach (string staleFile in new string[] { AdditionalTunnelHealthFile(entry), AdditionalTunnelPid(entry) })
        {
            try { if (File.Exists(staleFile)) File.Delete(staleFile); } catch { }
        }
        string args = "run --control-plane.tunnel-id " + entry.TunnelId +
            " --control-plane.api-key env:CONTROL_PLANE_API_KEY" +
            " --mcp.server-url http://127.0.0.1:" + gatewayPort + "/mcp" +
            " --health.listen-addr 127.0.0.1:0" +
            " --health.url-file \"" + AdditionalTunnelHealthFile(entry) + "\"" +
            " --pid.file \"" + AdditionalTunnelPid(entry) + "\"" +
            " --log.file \"" + AdditionalTunnelLog(entry) + "\"" +
            " --log.level info";
        string key = LoadAdditionalTunnelKey(entry);
        ProcessStartInfo psi = NewHiddenProcess(tunnelClientPath, args);
        psi.EnvironmentVariables["CONTROL_PLANE_API_KEY"] = key;
        try
        {
            Process process = Process.Start(psi);
            if (process == null) throw new InvalidOperationException("Could not start Tunnel process.");
            if (additionalTunnelProcesses.TryGetValue(entry.Id, out Process old))
            {
                try { old.Dispose(); } catch { }
            }
            additionalTunnelProcesses[entry.Id] = process;
            additionalTunnelNextRetry[entry.Id] = DateTime.UtcNow.AddSeconds(30);
        }
        finally
        {
            psi.EnvironmentVariables.Remove("CONTROL_PLANE_API_KEY");
            key = null;
        }
    }

    private void StopAdditionalTunnel(AdditionalTunnelDefinition entry)
    {
        if (!additionalTunnelProcesses.TryGetValue(entry.Id, out Process process) || process == null) return;
        // Only a Process started and retained by this panel instance may be terminated.
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                if (!process.WaitForExit(5000)) throw new TimeoutException("Tunnel did not exit.");
            }
            additionalTunnelProcesses.Remove(entry.Id);
            process.Dispose();
        }
        catch (Exception error)
        {
            LogRuntimeError("Stop additional Tunnel " + entry.Id, error);
            throw;
        }
    }

    private void StopAllOwnedAdditionalTunnels()
    {
        foreach (AdditionalTunnelDefinition entry in additionalTunnels)
            try { StopAdditionalTunnel(entry); } catch { }
    }

    private void EnsureAdditionalTunnels(bool gatewayReady)
    {
        if (!gatewayReady || quitting || !onboardingCompleted || updateSecurityHold ||
            TunnelDisabledByEnvironment() || !additionalTunnelsEnabled) return;
        foreach (AdditionalTunnelDefinition entry in additionalTunnels)
        {
            if (!entry.AutoStart || AdditionalTunnelRunning(entry) || AdditionalTunnelDetached(entry) ||
                !File.Exists(AdditionalTunnelSecret(entry))) continue;
            if (additionalTunnelNextRetry.TryGetValue(entry.Id, out DateTime retry) && DateTime.UtcNow < retry) continue;
            try { StartAdditionalTunnel(entry); }
            catch (Exception error)
            {
                additionalTunnelNextRetry[entry.Id] = DateTime.UtcNow.AddMinutes(1);
                LogRuntimeError("Start additional Tunnel " + entry.Id, error);
            }
        }
    }

    private void InitializeChannelManagerButton()
    {
        Border card = Find<Border>("TunnelSettingsCard");
        if (card == null || !(card.Child is StackPanel stack)) return;
        Button manager = new Button
        {
            Content = "Manage Channels (up to 3)",
            Style = window.FindResource("MiniButton") as Style,
            Margin = new Thickness(0, 10, 0, 0),
            ToolTip = "Add and independently control OpenAI Tunnels sharing this Gateway"
        };
        manager.Click += delegate { ShowAdditionalTunnelManager(); };
        stack.Children.Add(manager);
    }

    private static TextBlock SmallLabel(string value)
    {
        return new TextBlock { Text = value, FontSize = 11, Margin = new Thickness(0, 6, 0, 3) };
    }

    private Button ChannelButton(string value, Action action)
    {
        Button button = new Button
        {
            Content = value,
            Style = window.FindResource("MiniButton") as Style,
            Margin = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(9, 5, 9, 5)
        };
        button.Click += delegate { action(); };
        return button;
    }

    private void ShowAdditionalTunnelManager()
    {
        suppressAutoHide = true;
        Window dialog = new Window
        {
            Owner = window, Title = "DeskMCP · Tunnel Channels",
            Width = 520, Height = 520, MinWidth = 470, MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = BrushFrom(isDarkTheme ? "#FF232327" : "#FFFFFFFF")
        };
        StackPanel entries = new StackPanel { Margin = new Thickness(16) };
        ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = entries };
        dialog.Content = scroll;
        Action refresh = null;
        refresh = delegate
        {
            entries.Children.Clear();
            entries.Children.Add(new TextBlock { Text = "Tunnel Channels", FontSize = 20, FontWeight = FontWeights.SemiBold });
            entries.Children.Add(new TextBlock { Text = "One local Gateway · independent credentials and processes · maximum 3 channels",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 14), FontSize = 11 });
            entries.Children.Add(new TextBlock { Text = "1 · Primary Tunnel (existing)", FontWeight = FontWeights.SemiBold, FontSize = 13 });
            entries.Children.Add(new TextBlock { Text = IsValidTunnelId(tunnelId) ? tunnelId : "Not configured",
                FontSize = 11, Margin = new Thickness(0, 3, 0, 10) });

            foreach (AdditionalTunnelDefinition item in additionalTunnels.ToArray())
            {
                Border border = new Border { BorderBrush = BrushFrom(isDarkTheme ? "#FF484850" : "#FFD9D9DF"),
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 10) };
                StackPanel row = new StackPanel();
                row.Children.Add(new TextBlock { Text = item.Name, FontSize = 13, FontWeight = FontWeights.SemiBold });
                row.Children.Add(new TextBlock { Text = item.TunnelId + " · " + AdditionalTunnelStatus(item) +
                    (item.AutoStart ? " · Auto On" : " · Auto Off"), FontSize = 10, Margin = new Thickness(0, 4, 0, 8),
                    TextWrapping = TextWrapping.Wrap });
                StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal };
                actions.Children.Add(ChannelButton("Edit", delegate { EditAdditionalTunnel(item, refresh); }));
                actions.Children.Add(ChannelButton("Start", delegate
                {
                    try { StartAdditionalTunnel(item); refresh(); }
                    catch (Exception error) { System.Windows.MessageBox.Show(dialog, error.Message, "Cannot start Tunnel"); }
                }));
                actions.Children.Add(ChannelButton("Stop", delegate
                {
                    item.AutoStart = false;
                    SaveSettings();
                    bool owned = AdditionalTunnelRunning(item);
                    try
                    {
                        StopAdditionalTunnel(item);
                        refresh();
                        if (!owned)
                            System.Windows.MessageBox.Show(dialog,
                                "Auto-start is now off, but this panel did not own a running process. " +
                                "Any older BAT-managed Tunnel must be stopped from its original launcher.",
                                "No owned Tunnel to stop");
                    }
                    catch (Exception error) { System.Windows.MessageBox.Show(dialog, error.Message, "Cannot stop Tunnel"); }
                }));
                actions.Children.Add(ChannelButton("Remove", delegate
                {
                    if (System.Windows.MessageBox.Show(dialog, "Remove " + item.Name + " and its protected key?", "Remove Tunnel",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                    try
                    {
                        StopAdditionalTunnel(item);
                        additionalTunnels.Remove(item);
                        SaveSettings();
                        if (File.Exists(AdditionalTunnelSecret(item))) File.Delete(AdditionalTunnelSecret(item));
                        refresh();
                    }
                    catch (Exception error) { System.Windows.MessageBox.Show(dialog, error.Message, "Cannot remove Tunnel"); }
                }));
                row.Children.Add(actions);
                border.Child = row;
                entries.Children.Add(border);
            }
            if (additionalTunnels.Count < MaxTotalTunnelChannels - 1)
                entries.Children.Add(ChannelButton("+ Add Channel", delegate { EditAdditionalTunnel(null, refresh); }));
            entries.Children.Add(new TextBlock { Text =
                "For an existing BAT-managed Tunnel, stop the BAT-launched instance before starting it here. " +
                "The manager will not terminate detached or unrelated tunnel-client processes.",
                FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) });
            entries.Children.Add(ChannelButton("Refresh status", refresh));
        };
        System.Windows.Threading.DispatcherTimer timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += delegate { refresh(); };
        dialog.Closed += delegate { timer.Stop(); suppressAutoHide = false; };
        refresh();
        timer.Start();
        dialog.ShowDialog();
    }

    private void EditAdditionalTunnel(AdditionalTunnelDefinition existing, Action refresh)
    {
        if (existing == null && additionalTunnels.Count >= MaxTotalTunnelChannels - 1) return;
        Window edit = new Window
        {
            Owner = window, Title = existing == null ? "Add Tunnel Channel" : "Edit Tunnel Channel",
            Width = 440, Height = 415, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = BrushFrom(isDarkTheme ? "#FF232327" : "#FFFFFFFF")
        };
        StackPanel form = new StackPanel { Margin = new Thickness(20) };
        form.Children.Add(new TextBlock { Text = edit.Title, FontSize = 18, FontWeight = FontWeights.SemiBold });
        form.Children.Add(SmallLabel("Name"));
        TextBox name = new TextBox { Text = existing == null ? "" : existing.Name };
        form.Children.Add(name);
        form.Children.Add(SmallLabel("Tunnel ID"));
        TextBox id = new TextBox { Text = existing == null ? "" : existing.TunnelId };
        form.Children.Add(id);
        form.Children.Add(SmallLabel("Runtime API Key (leave blank to keep existing)"));
        PasswordBox key = new PasswordBox();
        form.Children.Add(key);
        CheckBox autoStart = new CheckBox { Content = "Auto-start this channel", IsChecked = existing != null && existing.AutoStart,
            Margin = new Thickness(0, 14, 0, 10) };
        form.Children.Add(autoStart);
        TextBlock error = new TextBlock { Foreground = Brushes.IndianRed, FontSize = 11, TextWrapping = TextWrapping.Wrap };
        form.Children.Add(error);
        form.Children.Add(new TextBlock { Text = "Use the credentials associated with this Tunnel's OpenAI organization. Each channel must have a unique Tunnel ID.",
            TextWrapping = TextWrapping.Wrap, FontSize = 10, Margin = new Thickness(0, 8, 0, 10) });
        Button save = ChannelButton("Save Channel", delegate
        {
            string tunnel = id.Text.Trim();
            if (!IsValidTunnelId(tunnel)) { error.Text = "Tunnel ID format is invalid."; return; }
            if (String.Equals(tunnel, tunnelId, StringComparison.Ordinal) ||
                additionalTunnels.Exists(x => !Object.ReferenceEquals(x, existing) && x.TunnelId == tunnel))
            { error.Text = "This Tunnel ID is already configured."; return; }
            if (String.IsNullOrWhiteSpace(name.Text)) { error.Text = "Enter a name."; return; }
            AdditionalTunnelDefinition item = existing ?? new AdditionalTunnelDefinition { Id = Guid.NewGuid().ToString("N") };
            if (existing == null && String.IsNullOrWhiteSpace(key.Password)) { error.Text = "Runtime API Key is required."; return; }
            try
            {
                bool changed = existing != null && (existing.TunnelId != tunnel || !String.IsNullOrWhiteSpace(key.Password));
                if (changed) StopAdditionalTunnel(existing);
                if (!String.IsNullOrWhiteSpace(key.Password)) SaveAdditionalTunnelKey(item, key.Password);
                item.Name = name.Text.Trim().Substring(0, Math.Min(64, name.Text.Trim().Length));
                item.TunnelId = tunnel;
                item.AutoStart = autoStart.IsChecked == true;
                if (existing == null) additionalTunnels.Add(item);
                additionalTunnelNextRetry.Remove(item.Id);
                SaveSettingsCore();
                key.Clear();
                edit.Close();
                refresh();
            }
            catch (Exception ex) { error.Text = ex.Message; }
        });
        form.Children.Add(save);
        edit.Content = form;
        edit.ShowDialog();
    }
}
