using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;

// Consent-only mouse click recorder. Never captures typed content, window titles,
// URLs, screen images, clipboard data or UIA Value/TextPattern values.
internal sealed class OperationRecorder : IDisposable
{
    const int WH_MOUSE_LL = 14, WM_LBUTTONUP = 0x0202;
    const int MaxSteps = 200;
    static readonly Regex Sensitive = new Regex(
        @"(?i)password|passwd|secret|token|api.?key|otp|驗證碼|密碼|金鑰|信用卡|身分證",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    [StructLayout(LayoutKind.Sequential)]
    struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    struct MouseData
    {
        public Point Point;
        public uint MouseDataValue, Flags, Time;
        public IntPtr Extra;
    }
    delegate IntPtr HookFn(int code, IntPtr message, IntPtr param);
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int kind, HookFn fn, IntPtr module, uint thread);
    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr msg, IntPtr param);

    sealed class Click
    {
        public int step { get; set; }
        public string action { get; set; }
        public string application { get; set; }
        public string role { get; set; }
        public string automation_id { get; set; }
        public string class_name { get; set; }
        public string label { get; set; }
        public string note { get; set; }
    }

    readonly Dispatcher dispatcher;
    readonly List<Click> clicks = new List<Click>();
    readonly string folder;
    HookFn callback;
    IntPtr hook;
    DispatcherTimer deadline;
    DateTime started;
    string id, application;
    int processId;
    bool active;
    public event Action StateChanged;
    public bool IsRecording { get { return active; } }
    public int StepCount { get { return clicks.Count; } }
    public string Folder { get { return folder; } }

    public OperationRecorder(Dispatcher dispatcher, string workspace)
    {
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        folder = Path.Combine(Path.GetFullPath(workspace), "DeskMCP-Recordings");
    }

    public void Start()
    {
        if (active) throw new InvalidOperationException("Recording already active");
        if (!Directory.Exists(Path.GetDirectoryName(folder))) throw new DirectoryNotFoundException("Workspace not available");
        clicks.Clear();
        processId = 0;
        application = null;
        id = Guid.NewGuid().ToString("N");
        started = DateTime.UtcNow;
        callback = MouseCallback;
        hook = SetWindowsHookEx(WH_MOUSE_LL, callback, IntPtr.Zero, 0);
        if (hook == IntPtr.Zero)
        {
            callback = null;
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Mouse recorder hook failed");
        }
        active = true;
        deadline = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        deadline.Tick += delegate
        {
            if (active && (DateTime.UtcNow - started >= TimeSpan.FromMinutes(10) || clicks.Count >= MaxSteps))
            {
                try { Stop(); } catch { Cancel(); }
            }
        };
        deadline.Start();
        StateChanged?.Invoke();
    }

    IntPtr MouseCallback(int code, IntPtr message, IntPtr param)
    {
        if (code >= 0 && active && message.ToInt32() == WM_LBUTTONUP)
        {
            try
            {
                MouseData item = Marshal.PtrToStructure<MouseData>(param);
                int x = item.Point.X, y = item.Point.Y;
                dispatcher.BeginInvoke(new Action(delegate { Capture(x, y); }), DispatcherPriority.Background);
            }
            catch { }
        }
        return CallNextHookEx(hook, code, message, param);
    }

    static string Safe(string input, int max)
    {
        if (String.IsNullOrWhiteSpace(input)) return null;
        string value = Regex.Replace(input, @"[\r\n\t\p{C}]+", " ").Trim();
        if (Sensitive.IsMatch(value)) return "[sensitive label withheld]";
        return value.Length <= max ? value : null;
    }

    void Capture(int x, int y)
    {
        if (!active || clicks.Count >= MaxSteps) return;
        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(x, y));
            if (element == null) return;
            int pid = element.Current.ProcessId;
            if (pid <= 0 || pid == Process.GetCurrentProcess().Id) return;
            string process;
            try { process = Process.GetProcessById(pid).ProcessName; } catch { return; }


            string role = element.Current.ControlType.ProgrammaticName.Replace("ControlType.", "");
            if (role == "Text" || role == "Document" || role == "Pane" || role == "Window") return;
            if (processId == 0) { processId = pid; application = process; }
            if (!String.Equals(process, application, StringComparison.OrdinalIgnoreCase)) return;
            bool input = role == "Edit" || element.Current.IsPassword;
            bool named = role == "Button" || role == "MenuItem" ||
                role == "CheckBox" || role == "RadioButton" || role == "TabItem";
            clicks.Add(new Click
            {
                step = clicks.Count + 1,
                action = input ? "input_placeholder" : "click",
                application = Safe(process, 60),
                role = role,
                automation_id = Safe(element.Current.AutomationId, 80),
                class_name = Safe(element.Current.ClassName, 80),
                label = !input && named ? Safe(element.Current.Name, 60) : null,
                note = input ? "Value intentionally not recorded. Request it at replay." : null
            });
            StateChanged?.Invoke();
        }
        catch { /* UIA inaccessible / secure desktop: never circumvent */ }
    }

    void Release()
    {
        active = false;
        if (deadline != null) { deadline.Stop(); deadline = null; }
        if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        callback = null;
        StateChanged?.Invoke();
    }

    public string Stop()
    {
        if (!active) throw new InvalidOperationException("No recording active");
        Release();
        Directory.CreateDirectory(folder);
        string jsonPath = Path.Combine(folder, "recording-" + id + ".json");
        var data = new
        {
            schema_version = 1, recording_id = id, created_utc = started.ToString("O"),
            ended_utc = DateTime.UtcNow.ToString("O"), application,
            privacy = "No keys, typed values, URLs, window titles, screenshots, or clipboard",
            complete = false, steps = clicks.ToArray()
        };
        string temp = jsonPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        File.Move(temp, jsonPath);
        string draft = Path.Combine(folder, "drafts", "recorded-" + id.Substring(0, 12));
        Directory.CreateDirectory(draft);
        File.WriteAllText(Path.Combine(draft, "SKILL.md"), SkillDraft(), new UTF8Encoding(false));
        return jsonPath;
    }

    string SkillDraft()
    {
        var lines = new List<string> {
            "---", "name: recorded-" + id.Substring(0, 12),
            "description: User-demonstrated UI workflow; GPT review is mandatory before replay.",
            "---", "", "# Unreviewed recording", "",
            "This Skill is model-readable instructions, not an executable macro.",
            "Use DeskMCP desktop_ui_windows, desktop_ui_snapshot and desktop_ui_action with fresh UI observations.",
            "Never replay by old screen coordinates. Ask for all missing input values.",
            "Confirm before irreversible send, publish, purchase, payment, or delete.", "",
            "## Observed controls"
        };
        foreach (var step in clicks)
        {
            string control = step.role ?? "unknown";
            if (!String.IsNullOrEmpty(step.label)) control += " [" + step.label.Replace("[", "").Replace("]", "") + "]";
            if (!String.IsNullOrEmpty(step.automation_id)) control += " (AutomationId: " + step.automation_id + ")";
            lines.Add(step.step + ". " + (step.action == "input_placeholder" ? "Supply runtime input to " : "Interact with ") + control);
        }
        lines.AddRange(new [] {
            "", "## Mandatory GPT review before activation",
            "- Compare each control with a fresh semantic UI snapshot.",
            "- Add branching, error recovery, waits, and verifiable success checks.",
            "- Typed values, shortcuts, navigation and some dynamic UI were not recorded.",
            "- Test the reviewed workflow before installing with desktop_skill_manage.",
            ""
        });
        return String.Join("\n", lines);
    }

    public void Cancel() { if (active) Release(); clicks.Clear(); }
    public void Dispose() { Cancel(); }
}
