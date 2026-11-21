using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ortilo.App.Input;
using Ortilo.Core.Models;
using Ortilo.Platform.Windows.Input;

namespace Ortilo.App.Application;

internal sealed class OrtiloConfig
{
    private static readonly OrtiloConfigJson ReadableJson = new(
        new JsonSerializerOptions(OrtiloConfigJson.Default.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

    public GapConfig Gaps { get; set; } = new();
    public BorderConfig FocusedWindowBorder { get; set; } = new(true, null, 1);
    public BorderConfig ParentBorder { get; set; } = new(true, null, 2);
    public IndicatorColorConfig WorkspaceIndicator { get; set; } = new();
    public IndicatorColorConfig TabIndicator { get; set; } = new();
    public MonitorConfig Monitors { get; set; } = new();
    public Dictionary<string, string[]> Keybindings { get; set; } = [];
    public bool ShowTopBar { get; set; } = true;
    public LayoutType DefaultLayout { get; set; } = LayoutType.ContainerTree;
    public double ResizeStep { get; set; } = 0.05;
    public bool LiveDragPreview { get; set; } = true;

    internal static string FilePath => ResolvePath(migrateLegacy: false);

    private static string ResolvePath(bool migrateLegacy)
    {
        if (Environment.GetEnvironmentVariable("ORTILO_CONFIG") is { Length: > 0 } path)
            return Path.GetFullPath(path);

        string destination = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ortilo", "config.json");
        if (migrateLegacy)
        {
            string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Ortilo", "config.json");
            MigrateLegacyConfig(legacy, destination);
        }

        return destination;
    }

    private static void MigrateLegacyConfig(string legacy, string destination)
    {
        if (File.Exists(destination) || !File.Exists(legacy))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = $"{destination}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.Copy(legacy, temporary);
            File.Move(temporary, destination);
        }
        catch (IOException) when (File.Exists(destination))
        {
            // Another Ortilo command created the destination first.
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    internal static OrtiloConfig Load(bool createIfMissing)
    {
        string path = createIfMissing ? EnsureExists() : ResolvePath(migrateLegacy: true);
        using var input = File.OpenRead(path);
        return JsonSerializer.Deserialize(input, OrtiloConfigJson.Default.OrtiloConfig) ??
            throw new InvalidDataException("The configuration must be a JSON object.");
    }

    internal static string EnsureExists()
    {
        string path = ResolvePath(migrateLegacy: true);
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = new FileStream(path, FileMode.CreateNew);
            var defaults = new OrtiloConfig
            {
                Keybindings = DefaultHotkeys.Create().GroupBy(FriendlyBindingName).ToDictionary(
                    group => group.Key, group => group.Select(FormatHotkey).ToArray())
            };
            JsonSerializer.Serialize(stream, defaults, ReadableJson.OrtiloConfig);
        }

        return path;
    }

    internal IReadOnlyList<HotkeyDefinition> Validate()
    {
        if (Gaps is null || FocusedWindowBorder is null || ParentBorder is null ||
            WorkspaceIndicator is null || TabIndicator is null || Monitors is null || Keybindings is null)
            throw new InvalidDataException("Configuration sections cannot be null.");

        if (new[] { Gaps.Inner, Gaps.Outer, Gaps.TopBar, Gaps.LayoutHeader, Gaps.Taskbar }
            .Any(gap => gap is < 0 or > 256))
            throw new InvalidDataException("Gap values must be between 0 and 256.");

        FocusedWindowBorder.Validate();
        ParentBorder.Validate();
        _ = WorkspaceIndicator.GetColors();
        _ = TabIndicator.GetColors();
        if (!double.IsFinite(ResizeStep) || ResizeStep is <= 0 or > 0.5)
            throw new InvalidDataException("Resize step must be greater than 0 and at most 0.5.");
        if (!Enum.IsDefined(DefaultLayout) || !Enum.IsDefined(Monitors.Mode) ||
            Monitors.SelectedDeviceNames is null || Monitors.WorkspaceDeviceNames is null ||
            Monitors.SelectedDeviceNames.Any(string.IsNullOrWhiteSpace) ||
            (Monitors.Mode == MonitorManagementMode.Selected && Monitors.SelectedDeviceNames.Count == 0) ||
            Monitors.WorkspaceDeviceNames.Any(pair => pair.Key is < 1 or > 9 || string.IsNullOrWhiteSpace(pair.Value)))
            throw new InvalidDataException("Check the default layout, monitor mode, display names, and workspace numbers.");

        var bindings = new List<HotkeyDefinition>();
        var defaults = DefaultHotkeys.Create().GroupBy(BindingName)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var aliases = defaults.Values.ToDictionary(group => FriendlyBindingName(group[0]),
            group => BindingName(group[0]), StringComparer.OrdinalIgnoreCase);

        foreach (var (action, chords) in Keybindings)
        {
            string name = aliases.GetValueOrDefault(action, action);
            if (!defaults.Remove(name, out var original) || chords is null)
                throw new InvalidDataException($"Unknown or invalid hotkey action '{action}'.");

            foreach (string chord in chords)
                bindings.Add(ParseHotkey(original[0], chord));
        }

        bindings.AddRange(defaults.Values.SelectMany(group => group));
        if (bindings.GroupBy(binding => (binding.VirtualKey, binding.Modifiers)).Any(group => group.Count() > 1))
            throw new InvalidDataException("Two actions use the same hotkey.");
        return bindings;
    }

    private static string BindingName(HotkeyDefinition binding) =>
        binding.Argument is int number ? $"{binding.ActionName}.{number}" : binding.ActionName;

    private static string FriendlyBindingName(HotkeyDefinition binding) => binding.ActionName switch
    {
        HotkeyActions.FocusLeft => "Focus left",
        HotkeyActions.FocusRight => "Focus right",
        HotkeyActions.FocusUp => "Focus up",
        HotkeyActions.FocusDown => "Focus down",
        HotkeyActions.FocusParent => "Focus parent container",
        HotkeyActions.FocusChild => "Focus child container",
        HotkeyActions.MoveLeft => "Move window left",
        HotkeyActions.MoveRight => "Move window right",
        HotkeyActions.MoveUp => "Move window up",
        HotkeyActions.MoveDown => "Move window down",
        HotkeyActions.ResizeLeft => "Resize left",
        HotkeyActions.ResizeRight => "Resize right",
        HotkeyActions.ResizeUp => "Resize up",
        HotkeyActions.ResizeDown => "Resize down",
        HotkeyActions.SwitchWorkspace => $"Switch to workspace {binding.Argument}",
        HotkeyActions.MoveToWorkspace => $"Move window to workspace {binding.Argument}",
        HotkeyActions.MoveWorkspaceToNextMonitor => "Move workspace to next monitor",
        HotkeyActions.MoveWorkspaceToPreviousMonitor => "Move workspace to previous monitor",
        HotkeyActions.ToggleSplit => "Toggle split direction",
        HotkeyActions.ContainerTree => "Use split layout",
        HotkeyActions.Tabbed => "Use tabbed layout",
        HotkeyActions.Stacking => "Use stacked layout",
        HotkeyActions.MasterStack => "Use master stack layout",
        HotkeyActions.Fullscreen => "Toggle fullscreen",
        HotkeyActions.Minimize => "Minimize window",
        HotkeyActions.ToggleFloating => "Toggle floating",
        HotkeyActions.Reapply => "Reapply layout",
        HotkeyActions.ReloadConfig => "Reload configuration",
        HotkeyActions.Reset => "Reset Ortilo",
        _ => throw new InvalidOperationException($"Missing display name for '{binding.ActionName}'.")
    };

    internal static void FormatKeybindings()
    {
        string path = ResolvePath(migrateLegacy: true);
        byte[] bytes = File.ReadAllBytes(path);
        using var input = new MemoryStream(bytes, writable: false);
        var config = JsonSerializer.Deserialize(input, OrtiloConfigJson.Default.OrtiloConfig) ??
            throw new InvalidDataException("The configuration must be a JSON object.");
        config.Validate();

        var names = DefaultHotkeys.Create().GroupBy(BindingName).ToDictionary(
            group => group.Key, group => FriendlyBindingName(group.First()), StringComparer.OrdinalIgnoreCase);
        int bom = bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0;
        var reader = new Utf8JsonReader(bytes.AsSpan(bom), new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });
        using var rewritten = new MemoryStream();
        int copied = 0;
        bool inBindings = false;
        while (reader.Read())
        {
            if (reader.CurrentDepth == 1 && reader.TokenType == JsonTokenType.PropertyName)
                inBindings = reader.ValueTextEquals("keybindings"u8);

            if (!inBindings ||
                (reader.CurrentDepth != 2 || reader.TokenType != JsonTokenType.PropertyName) &&
                (reader.CurrentDepth != 3 || reader.TokenType != JsonTokenType.String))
                continue;

            string value = reader.GetString()!;
            if (reader.TokenType == JsonTokenType.PropertyName)
                value = names.GetValueOrDefault(value, value);

            int start = checked((int)reader.TokenStartIndex + bom);
            // Property-name tokens include the colon in BytesConsumed. Copy only the quoted token.
            int end = start + reader.ValueSpan.Length + 2;
            var encoded = JsonEncodedText.Encode(value, JavaScriptEncoder.UnsafeRelaxedJsonEscaping);
            rewritten.Write(bytes.AsSpan(copied, start - copied));
            rewritten.WriteByte((byte)'"');
            rewritten.Write(encoded.EncodedUtf8Bytes);
            rewritten.WriteByte((byte)'"');
            copied = end;
        }

        if (copied == 0)
            return;

        rewritten.Write(bytes.AsSpan(copied));
        byte[] formatted = rewritten.ToArray();
        if (bytes.AsSpan().SequenceEqual(formatted))
            return;

        string temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temp, formatted);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    internal static string FormatHotkey(HotkeyDefinition binding)
    {
        var parts = new List<string>();
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(binding.VirtualKey switch
        {
            0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down", 0x20 => "Space",
            0x09 => "Tab", 0x0D => "Enter", 0x1B => "Esc", 0x08 => "Backspace",
            0x24 => "Home", 0x23 => "End", 0x21 => "PageUp", 0x22 => "PageDown",
            0x2D => "Insert", 0x2E => "Delete", 0xBA => ";", 0xBC => ",", 0xBE => ".", 0xBD => "-",
            >= 0x70 and <= 0x87 => $"F{binding.VirtualKey - 0x6F}",
            _ => ((char)binding.VirtualKey).ToString()
        });
        return string.Join("+", parts);
    }

    private static HotkeyDefinition ParseHotkey(HotkeyDefinition definition, string chord)
    {
        var parts = chord?.Split('+', StringSplitOptions.TrimEntries);
        if (parts is null || parts.Length < 2)
            throw new InvalidDataException($"Hotkey '{chord}' needs a modifier and a key.");

        var modifiers = HotkeyModifiers.NoRepeat;
        foreach (string part in parts[..^1])
        {
            var modifier = part.ToUpperInvariant() switch
            {
                "ALT" => HotkeyModifiers.Alt,
                "CTRL" or "CONTROL" => HotkeyModifiers.Control,
                "SHIFT" => HotkeyModifiers.Shift,
                "WIN" => HotkeyModifiers.Win,
                _ => throw new InvalidDataException($"Unknown modifier '{part}' in '{chord}'.")
            };
            if (modifiers.HasFlag(modifier))
                throw new InvalidDataException($"Duplicate modifier '{part}' in '{chord}'.");
            modifiers |= modifier;
        }

        string key = parts[^1].ToUpperInvariant();
        uint virtualKey = key switch
        {
            "LEFT" => 0x25, "UP" => 0x26, "RIGHT" => 0x27, "DOWN" => 0x28,
            "SPACE" => 0x20, "TAB" => 0x09, "ENTER" => 0x0D, "ESC" or "ESCAPE" => 0x1B,
            "HOME" => 0x24, "END" => 0x23, "PAGEUP" => 0x21, "PAGEDOWN" => 0x22,
            "BACKSPACE" => 0x08, "INSERT" => 0x2D, "DELETE" => 0x2E,
            ";" => 0xBA, "," => 0xBC, "." => 0xBE, "-" => 0xBD,
            _ when key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]) => key[0],
            _ when key.StartsWith('F') && uint.TryParse(key.AsSpan(1), out uint f) && f is >= 1 and <= 24
                => 0x6F + f,
            _ => throw new InvalidDataException($"Unknown key '{key}' in '{chord}'.")
        };
        return definition with { VirtualKey = virtualKey, Modifiers = modifiers };
    }
}

internal sealed record GapConfig(int Inner = 0, int Outer = 0, int TopBar = 0, int LayoutHeader = 0, int Taskbar = 0);

internal sealed class MonitorConfig
{
    public MonitorManagementMode Mode { get; set; } = MonitorManagementMode.All;
    public List<string> SelectedDeviceNames { get; set; } = [];
    public Dictionary<int, string> WorkspaceDeviceNames { get; set; } = [];

    internal MonitorManagementOptions ToOptions() => new()
    {
        Mode = Mode,
        SelectedDeviceNames = SelectedDeviceNames,
        WorkspaceDeviceNames = WorkspaceDeviceNames
    };
}

internal sealed record IndicatorColorConfig(string? Light = null, string? Dark = null)
{
    internal (uint? Light, uint? Dark) GetColors() =>
        (ConfigColor.Parse(Light), ConfigColor.Parse(Dark));
}

internal static class ConfigColor
{
    internal static uint? Parse(string? color)
    {
        if (color is null)
            return null;
        if (color.Length != 7 || color[0] != '#' ||
            !uint.TryParse(color.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint rgb))
            throw new InvalidDataException("Colors must use #RRGGBB.");

        return ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | (rgb >> 16);
    }
}

internal sealed record BorderConfig(bool Enabled = true, string? Color = null, int? Thickness = null)
{
    internal uint? GetColorRef() => ConfigColor.Parse(Color);

    internal void Validate()
    {
        _ = GetColorRef();
        if (Thickness is < 1 or > 16)
            throw new InvalidDataException("Border thickness must be between 1 and 16.");
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    WriteIndented = true)]
[JsonSerializable(typeof(OrtiloConfig))]
internal sealed partial class OrtiloConfigJson : JsonSerializerContext;
