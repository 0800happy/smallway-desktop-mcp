using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

internal static class UiLocalization
{
    // UI-only text localization. Protocol identifiers, filenames, API keys, user input and
    // security policy strings are deliberately left unchanged (2026-10-20).
    private sealed class TextState
    {
        internal readonly Dictionary<string, Slot> Slots = new Dictionary<string, Slot>(StringComparer.Ordinal);
    }
    private sealed class Slot
    {
        internal string Original;
        internal string Rendered;
    }
    private static readonly ConditionalWeakTable<DependencyObject, TextState> Tracked =
        new ConditionalWeakTable<DependencyObject, TextState>();
    private static Dictionary<string, string> chinese = new Dictionary<string, string>(StringComparer.Ordinal);
    private static string language = "zh-TW";
    internal static string Language { get { return language; } }
    internal static string Normalize(string value)
    {
        return String.Equals(value, "en-US", StringComparison.OrdinalIgnoreCase) ||
               String.Equals(value, "en", StringComparison.OrdinalIgnoreCase) ? "en-US" : "zh-TW";
    }
    internal static void Initialize(string baseDir, string initialLanguage)
    {
        language = Normalize(initialLanguage);
        string file = Path.Combine(baseDir, "locales", "zh-TW.json");
        try
        {
            using (JsonDocument json = JsonDocument.Parse(File.ReadAllText(file)))
            {
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (JsonProperty item in json.RootElement.GetProperty("translations").EnumerateObject())
                    if (item.Value.ValueKind == JsonValueKind.String) values[item.Name] = item.Value.GetString();
                chinese = values;
            }
        }
        catch (Exception error)
        {
            // Missing localizations should not interrupt Gateway startup. English remains usable.
            System.Diagnostics.Trace.WriteLine("Unable to read UI localization resources: " + error.Message);
        }
    }
    internal static void SetLanguage(string value) { language = Normalize(value); }
    internal static string Translate(string source)
    {
        if (language == "en-US" || String.IsNullOrEmpty(source)) return source;
        string result;
        if (chinese.TryGetValue(source, out result)) return result;
        // Translate only explicitly listed status prefixes. Preserve dynamic paths,
        // identifiers, hostnames, counts and exception text without modification.
        foreach (KeyValuePair<string, string> item in chinese)
        {
            if ((item.Key.EndsWith(": ", StringComparison.Ordinal) ||
                 item.Key.EndsWith(" (", StringComparison.Ordinal)) &&
                source.StartsWith(item.Key, StringComparison.Ordinal))
                return item.Value + source.Substring(item.Key.Length);
        }
        string trimmed = source.Trim();
        if (trimmed != source && chinese.TryGetValue(trimmed, out result))
        {
            int start = source.IndexOf(trimmed, StringComparison.Ordinal);
            return source.Substring(0, start) + result + source.Substring(start + trimmed.Length);
        }
        return source;
    }
    private static void ApplySlot(DependencyObject node, string key, string current, Action<string> assign)
    {
        if (current == null) return;
        TextState state = Tracked.GetOrCreateValue(node);
        Slot slot;
        if (!state.Slots.TryGetValue(key, out slot))
        {
            slot = new Slot { Original = current, Rendered = current };
            state.Slots.Add(key, slot);
        }
        else if (!String.Equals(current, slot.Rendered, StringComparison.Ordinal))
            slot.Original = current;  // The program assigned a NEW dynamic status/message.
        string target = Translate(slot.Original);
        if (!String.Equals(current, target, StringComparison.Ordinal)) assign(target);
        slot.Rendered = target;
    }
    internal static void Apply(Window window)
    {
        if (window == null) return;
        try
        {
            window.Language = System.Windows.Markup.XmlLanguage.GetLanguage(language);
            window.FontFamily = new FontFamily(language == "zh-TW" ? "Microsoft JhengHei UI" : "Segoe UI");
            ApplyNode(window, new HashSet<DependencyObject>());
        }
        catch (Exception error) { System.Diagnostics.Trace.WriteLine("UI translation failed: " + error.Message); }
    }
    internal static int RunSelfTest(string baseDirectory)
    {
        // Isolated switch test: no Gateway, user settings, personal Browser or Tunnel.
        Initialize(baseDirectory, "zh-TW");
        Window sample = new Window();
        TextBlock label = new TextBlock { Text = "Settings" };
        sample.Content = label;
        Apply(sample);
        if (label.Text != "設定") return 3;
        SetLanguage("en-US");
        Apply(sample);
        if (label.Text != "Settings") return 4;
        label.Text = "Running";
        SetLanguage("zh-TW");
        Apply(sample);
        if (label.Text != "執行中") return 5;
        SetLanguage("en-US");
        Apply(sample);
        if (label.Text != "Running") return 6;
        return 0;
    }

    private static void ApplyNode(DependencyObject node, HashSet<DependencyObject> visited)
    {
        if (node == null || !visited.Add(node)) return;
        TextBlock tb = node as TextBlock;
        if (tb != null) ApplySlot(node, "Text", tb.Text, text => tb.Text = text);
        ContentControl content = node as ContentControl;
        if (content != null && content.Content is string)
            ApplySlot(node, "Content", (string)content.Content, text => content.Content = text);
        HeaderedContentControl header = node as HeaderedContentControl;
        if (header != null && header.Header is string)
            ApplySlot(node, "Header", (string)header.Header, text => header.Header = text);
        FrameworkElement fe = node as FrameworkElement;
        if (fe != null && fe.ToolTip is string)
            ApplySlot(node, "ToolTip", (string)fe.ToolTip, text => fe.ToolTip = text);
        Window win = node as Window;
        if (win != null) ApplySlot(node, "Title", win.Title, text => win.Title = text);
        foreach (object child in LogicalTreeHelper.GetChildren(node))
        {
            DependencyObject dependency = child as DependencyObject;
            if (dependency != null) ApplyNode(dependency, visited);
        }
    }
}
