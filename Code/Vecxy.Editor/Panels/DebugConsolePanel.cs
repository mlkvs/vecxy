using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using ImGuiNET;
using Vecxy.Assets;
using Vecxy.Diagnostics.Console;

namespace Vecxy.Editor;

public sealed class DebugConsolePanel
{
    private const string DockedWindowTitle = "Debug Console";
    private const string FloatingWindowTitle = "Debug Console Overlay";
    private const uint SearchBufferSize = 256;
    private const uint CommandBufferSize = 2048;

    private readonly IDebugConsole _console;
    private readonly IConsoleSuggestionProvider _suggestions;
    private readonly ISystemFileDialog _fileDialog;
    private readonly HashSet<ConsoleLogLevel> _enabledLevels =
        Enum.GetValues<ConsoleLogLevel>().ToHashSet();
    private readonly List<string> _history = [];
    private string _commandInput = string.Empty;
    private string _historyDraft = string.Empty;
    private string _search = string.Empty;
    private string _selectedLogDetails = string.Empty;
    private IReadOnlyList<ConsoleSuggestion> _currentSuggestions = [];
    private ConsoleLogEntry? _selectedLogEntry;
    private int _selectedSuggestionIndex = -1;
    private int _historyIndex = -1;
    private int _pendingCursorPosition = -1;
    private bool _applySuggestionRequested;
    private int _previousEntryCount;
    private bool _autoScroll = true;
    private bool _requestInputFocus = true;
    private bool _wasVisible;
    private bool _wasAtBottom = true;

    public DebugConsolePanel(
        IDebugConsole console,
        IConsoleSuggestionProvider suggestions,
        ISystemFileDialog fileDialog)
    {
        _console = console;
        _suggestions = suggestions;
        _fileDialog = fileDialog;
    }

    public bool ShouldRender => _console.IsOpen;

    public void Draw(bool editorOverlayVisible)
    {
        if (!_console.IsOpen)
        {
            _wasVisible = false;
            return;
        }

        if (!_wasVisible)
        {
            RequestInputFocus(_commandInput.Length);
            _wasVisible = true;
        }

        var selectedSuggestion = TryGetSelectedSuggestion();
        _currentSuggestions =
            _commandInput.Length == 0 ||
            _historyIndex >= 0
            ? []
            : _suggestions.GetSuggestions(
                _commandInput,
                _commandInput.Length);
        _selectedSuggestionIndex = ResolveSelectedSuggestionIndex(selectedSuggestion);

        if (!editorOverlayVisible)
        {
            ImGui.SetNextWindowSize(new Vector2(760.0f, 420.0f), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSizeConstraints(new Vector2(420.0f, 220.0f), new Vector2(float.MaxValue, float.MaxValue));
        }

        var open = true;
        var flags = editorOverlayVisible
            ? ImGuiWindowFlags.None
            : ImGuiWindowFlags.NoCollapse;

        if (!ImGui.Begin(
                editorOverlayVisible ? DockedWindowTitle : FloatingWindowTitle,
                ref open,
                flags))
        {
            ImGui.End();
            if (!open)
                _console.Close();
            return;
        }

        DrawToolbar();
        ImGui.Separator();

        var showSuggestions = _currentSuggestions.Count > 0;
        var suggestionsHeight = showSuggestions
            ? Math.Min(140.0f, _currentSuggestions.Count * 24.0f + 8.0f)
            : 0.0f;
        var inputHeight = ImGui.GetFrameHeight() + 8.0f;
        var spacingHeight = showSuggestions
            ? ImGui.GetStyle().ItemSpacing.Y
            : 0.0f;
        var bottomHeight = suggestionsHeight + inputHeight + spacingHeight + 8.0f;
        var entriesHeight = Math.Max(
            140.0f,
            ImGui.GetContentRegionAvail().Y - bottomHeight);

        DrawEntries(entriesHeight);

        ImGui.Separator();
        if (ImGui.BeginChild(
                "debug_console_bottom",
                new Vector2(0.0f, bottomHeight),
                ImGuiChildFlags.None))
        {
            if (showSuggestions)
                DrawSuggestions(suggestionsHeight);

            DrawInput();
            ImGui.EndChild();
        }
        else
        {
            ImGui.EndChild();
        }

        ImGui.End();

        if (!open)
            _console.Close();
    }

    private void DrawToolbar()
    {
        if (ImGui.Button("Clear"))
        {
            _console.Clear();
            _previousEntryCount = 0;
            _wasAtBottom = true;
            _selectedLogEntry = null;
            _selectedLogDetails = string.Empty;
        }

        ImGui.SameLine();
        if (ImGui.Button("Save"))
            ImGui.OpenPopup("debug_console_save_popup");

        if (ImGui.BeginPopup("debug_console_save_popup"))
        {
            if (ImGui.MenuItem("Save All"))
                Save(_console.GetSnapshot());

            var visibleEntries = FilterEntries(_console.GetSnapshot());
            if (ImGui.MenuItem("Save Visible"))
                Save(visibleEntries.Select(item => item.Entry).ToArray());

            ImGui.EndPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Levels"))
            ImGui.OpenPopup("debug_console_levels_popup");

        if (ImGui.BeginPopup("debug_console_levels_popup"))
        {
            if (ImGui.Button("All"))
            {
                _enabledLevels.Clear();
                foreach (var level in Enum.GetValues<ConsoleLogLevel>())
                    _enabledLevels.Add(level);
            }

            ImGui.SameLine();
            if (ImGui.Button("None"))
                _enabledLevels.Clear();

            ImGui.SameLine();
            if (ImGui.Button("Errors"))
            {
                _enabledLevels.Clear();
                _enabledLevels.Add(ConsoleLogLevel.Error);
                _enabledLevels.Add(ConsoleLogLevel.Critical);
            }

            ImGui.Separator();

            foreach (var level in Enum.GetValues<ConsoleLogLevel>())
            {
                var enabled = _enabledLevels.Contains(level);
                if (ImGui.Checkbox(level.ToString(), ref enabled))
                {
                    if (enabled)
                        _enabledLevels.Add(level);
                    else
                        _enabledLevels.Remove(level);
                }
            }

            ImGui.EndPopup();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(240.0f);
        ImGui.InputTextWithHint("##console_search", "Search logs", ref _search, SearchBufferSize);

        ImGui.SameLine();
        ImGui.Checkbox("Auto Scroll", ref _autoScroll);

        var visibleCount = FilterEntries(_console.GetSnapshot()).Count;
        ImGui.SameLine();
        ImGui.TextDisabled($"Visible: {visibleCount}");
    }

    private void DrawEntries(float height)
    {
        var entries = _console.GetSnapshot();
        var visibleEntries = FilterEntries(entries);
        var detailsHeight = _selectedLogEntry is null
            ? 0.0f
            : Math.Clamp(height * 0.34f, 100.0f, 220.0f);
        var listHeight = Math.Max(
            64.0f,
            height - detailsHeight - (detailsHeight > 0.0f ? ImGui.GetStyle().ItemSpacing.Y + 1.0f : 0.0f));

        if (!ImGui.BeginChild(
                "debug_console_entries",
                new Vector2(0.0f, listHeight),
                ImGuiChildFlags.Border,
                ImGuiWindowFlags.HorizontalScrollbar))
        {
            ImGui.EndChild();
            return;
        }

        var shouldScrollToBottom =
            _autoScroll &&
            entries.Count > _previousEntryCount &&
            _wasAtBottom;

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4.0f, 1.0f));
        DrawLogRows(visibleEntries);
        ImGui.PopStyleVar();

        if (_selectedLogEntry is not null &&
            ImGui.IsWindowFocused() &&
            ImGui.GetIO().KeyCtrl &&
            ImGui.IsKeyPressed(ImGuiKey.C))
        {
            ImGui.SetClipboardText(_selectedLogDetails);
        }

        if (shouldScrollToBottom)
            ImGui.SetScrollHereY(1.0f);

        _previousEntryCount = entries.Count;
        _wasAtBottom = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 4.0f;
        ImGui.EndChild();

        if (_selectedLogEntry is not null)
            DrawSelectedLogDetails(detailsHeight);
    }

    private void DrawLogRows(
        IReadOnlyList<(int Index, ConsoleLogEntry Entry)> visibleEntries)
    {
        var lineHeight = ImGui.GetTextLineHeightWithSpacing();
        var rowHeight = ImGui.GetTextLineHeight();
        var scrollY = ImGui.GetScrollY();
        var visibleHeight = ImGui.GetWindowHeight();
        var firstIndex = Math.Clamp(
            (int)MathF.Floor(scrollY / lineHeight) - 1,
            0,
            visibleEntries.Count);
        var lastIndex = Math.Clamp(
            (int)MathF.Ceiling((scrollY + visibleHeight) / lineHeight) + 1,
            firstIndex,
            visibleEntries.Count);

        if (firstIndex > 0)
            ImGui.Dummy(new Vector2(1.0f, firstIndex * lineHeight));

        for (var index = firstIndex; index < lastIndex; index++)
        {
            var (sourceIndex, entry) = visibleEntries[index];
            var selected = ReferenceEquals(_selectedLogEntry, entry);
            var color = GetLevelColor(entry.Level);
            var rowText = FormatLogRow(entry);

            ImGui.PushID(sourceIndex);
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            if (ImGui.Selectable(
                    $"{rowText}##row",
                    selected,
                    ImGuiSelectableFlags.AllowDoubleClick,
                    new Vector2(0.0f, rowHeight)))
            {
                if (selected)
                    HideLogDetails();
                else
                    SelectLogEntry(entry);
            }
            ImGui.PopStyleColor();

            if (ImGui.BeginPopupContextItem("log_context"))
            {
                if (ImGui.MenuItem("Copy Entry"))
                    ImGui.SetClipboardText(FormatLogDetails(entry));

                if (ImGui.MenuItem("Copy Message"))
                    ImGui.SetClipboardText(entry.Message);

                var hasStackTrace = !string.IsNullOrWhiteSpace(entry.StackTrace);
                if (ImGui.MenuItem("Copy Stack Trace", string.Empty, false, hasStackTrace))
                    ImGui.SetClipboardText(entry.StackTrace!);

                ImGui.EndPopup();
            }

            ImGui.PopID();
        }

        var remaining = visibleEntries.Count - lastIndex;
        if (remaining > 0)
            ImGui.Dummy(new Vector2(1.0f, remaining * lineHeight));
    }

    private void DrawSelectedLogDetails(float height)
    {
        ImGui.Separator();

        if (ImGui.Button("Hide Details"))
        {
            HideLogDetails();
            return;
        }

        ImGui.SameLine();
        if (ImGui.Button("Copy Details"))
            ImGui.SetClipboardText(_selectedLogDetails);

        ImGui.SameLine();
        ImGui.TextDisabled("Select text below or use Ctrl+C");

        var textHeight = Math.Max(52.0f, height - ImGui.GetFrameHeightWithSpacing() - 2.0f);
        var bufferSize = (uint)Math.Max(1024, _selectedLogDetails.Length + 1);
        ImGui.InputTextMultiline(
            "##debug_console_selected_details",
            ref _selectedLogDetails,
            bufferSize,
            new Vector2(-1.0f, textHeight),
            ImGuiInputTextFlags.ReadOnly);
    }

    private void SelectLogEntry(ConsoleLogEntry entry)
    {
        _selectedLogEntry = entry;
        _selectedLogDetails = FormatLogDetails(entry);
    }

    private void HideLogDetails()
    {
        _selectedLogEntry = null;
        _selectedLogDetails = string.Empty;
    }

    private void DrawSuggestions(float height)
    {
        if (!ImGui.BeginChild(
                "debug_console_suggestions",
                new Vector2(0.0f, height),
                ImGuiChildFlags.Border,
                ImGuiWindowFlags.NoNavInputs | ImGuiWindowFlags.NoNavFocus))
        {
            ImGui.EndChild();
            return;
        }

        for (var index = 0; index < _currentSuggestions.Count; index++)
        {
            var selected = index == _selectedSuggestionIndex;
            var suggestion = _currentSuggestions[index];
            if (ImGui.Selectable(
                    $"{suggestion.DisplayText}##console_suggestion_{index}",
                    selected))
            {
                ApplySuggestion(suggestion);
            }

            if (!string.IsNullOrWhiteSpace(suggestion.Description))
            {
                ImGui.SameLine();
                ImGui.TextDisabled($" {suggestion.Description}");
            }
        }

        ImGui.EndChild();
    }

    private unsafe void DrawInput()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(">");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1.0f);

        if (_requestInputFocus)
        {
            ImGui.SetKeyboardFocusHere();
            _requestInputFocus = false;
        }

        var selectedSuggestion = TryGetSelectedSuggestion();
        _applySuggestionRequested = false;

        var submitted = ImGui.InputText(
            "##debug_console_input",
            ref _commandInput,
            CommandBufferSize,
            ImGuiInputTextFlags.EnterReturnsTrue |
            ImGuiInputTextFlags.CallbackCompletion |
            ImGuiInputTextFlags.CallbackHistory |
            ImGuiInputTextFlags.CallbackEdit |
            ImGuiInputTextFlags.CallbackAlways,
            OnCommandInput);

        var focused = ImGui.IsItemActive() || ImGui.IsItemFocused();
        if (focused)
            HandleInputHotkeys();

        if (_applySuggestionRequested)
        {
            var suggestion = selectedSuggestion ??
                _currentSuggestions.FirstOrDefault();
            if (suggestion is not null)
                ApplySuggestion(suggestion);

            return;
        }

        if (submitted)
        {
            if (selectedSuggestion is not null)
                ApplySuggestion(selectedSuggestion);
            else
                ExecuteCurrentCommand();
        }
    }

    private void HandleInputHotkeys()
    {
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _currentSuggestions = [];
            _selectedSuggestionIndex = -1;
        }
    }

    private void ExecuteCurrentCommand()
    {
        var command = _commandInput.Trim();
        if (command.Length == 0)
            return;

        AddHistory(command);
        var result = _console.Execute(command);
        if (result.Success)
        {
            _commandInput = string.Empty;
            _currentSuggestions = [];
            _selectedSuggestionIndex = -1;
        }

        RequestInputFocus(_commandInput.Length);
    }

    private void ApplySuggestion(ConsoleSuggestion suggestion)
    {
        var start = Math.Clamp(suggestion.ReplaceStart, 0, _commandInput.Length);
        var length = Math.Clamp(suggestion.ReplaceLength, 0, _commandInput.Length - start);
        _commandInput =
            _commandInput[..start] +
            suggestion.InsertText +
            _commandInput[(start + length)..];
        _currentSuggestions = _suggestions.GetSuggestions(
            _commandInput,
            Math.Clamp(start + suggestion.InsertText.Length, 0, _commandInput.Length));
        _selectedSuggestionIndex = -1;
        RequestInputFocus(start + suggestion.InsertText.Length);
    }

    private void RequestInputFocus(int cursorPosition)
    {
        _requestInputFocus = true;
        _pendingCursorPosition = Math.Clamp(
            cursorPosition,
            0,
            _commandInput.Length);
    }

    private unsafe int OnCommandInput(ImGuiInputTextCallbackData* data)
    {
        if (data->EventFlag == ImGuiInputTextFlags.CallbackCompletion)
        {
            _applySuggestionRequested = true;
            return 0;
        }

        if (data->EventFlag == ImGuiInputTextFlags.CallbackHistory)
        {
            HandleHistoryInput(data);
            return 0;
        }

        if (data->EventFlag == ImGuiInputTextFlags.CallbackEdit &&
            _historyIndex >= 0)
        {
            _historyIndex = -1;
            _historyDraft = string.Empty;
        }

        if (_pendingCursorPosition < 0)
            return 0;

        var cursorPosition = Math.Clamp(
            _pendingCursorPosition,
            0,
            data->BufTextLen);
        data->CursorPos = cursorPosition;
        data->SelectionStart = cursorPosition;
        data->SelectionEnd = cursorPosition;
        _pendingCursorPosition = -1;
        return 0;
    }

    private unsafe void HandleHistoryInput(ImGuiInputTextCallbackData* data)
    {
        var moveUp = data->EventKey == ImGuiKey.UpArrow;

        if (_historyIndex < 0 &&
            _currentSuggestions.Count > 0)
        {
            _selectedSuggestionIndex = moveUp
                ? _selectedSuggestionIndex < 0
                    ? _currentSuggestions.Count - 1
                    : Math.Max(0, _selectedSuggestionIndex - 1)
                : _selectedSuggestionIndex < 0
                    ? 0
                    : Math.Min(
                        _currentSuggestions.Count - 1,
                        _selectedSuggestionIndex + 1);
            return;
        }

        if (_history.Count == 0)
            return;

        if (_historyIndex < 0)
        {
            if (!moveUp)
                return;

            _historyDraft = Marshal.PtrToStringUTF8(
                (nint)data->Buf,
                data->BufTextLen) ?? string.Empty;
            _historyIndex = _history.Count - 1;
        }
        else if (moveUp)
        {
            _historyIndex = Math.Max(0, _historyIndex - 1);
        }
        else
        {
            _historyIndex++;
        }

        var value = _historyIndex >= _history.Count
            ? _historyDraft
            : _history[_historyIndex];
        if (_historyIndex >= _history.Count)
            _historyIndex = -1;

        var callbackData = new ImGuiInputTextCallbackDataPtr(data);
        callbackData.DeleteChars(0, data->BufTextLen);
        callbackData.InsertChars(0, value);
        data->CursorPos = data->BufTextLen;
        data->SelectionStart = data->CursorPos;
        data->SelectionEnd = data->CursorPos;
    }

    private void AddHistory(string command)
    {
        if (_history.Count == 0 ||
            !string.Equals(_history[^1], command, StringComparison.Ordinal))
        {
            _history.Add(command);
        }

        _historyIndex = -1;
        _historyDraft = string.Empty;
    }

    private List<(int Index, ConsoleLogEntry Entry)> FilterEntries(
        IReadOnlyList<ConsoleLogEntry> entries)
    {
        var filtered = new List<(int Index, ConsoleLogEntry Entry)>(entries.Count);
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (!_enabledLevels.Contains(entry.Level))
                continue;

            if (_search.Length > 0 &&
                !ContainsSearch(entry.Message) &&
                !ContainsSearch(entry.Category) &&
                !ContainsSearch(entry.StackTrace))
            {
                continue;
            }

            filtered.Add((index, entry));
        }

        return filtered;
    }

    private bool ContainsSearch(string? value) =>
        value?.Contains(_search, StringComparison.OrdinalIgnoreCase) == true;

    private static string FormatLogRow(ConsoleLogEntry entry)
    {
        var message = entry.Message
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ');
        var detailsMarker = string.IsNullOrWhiteSpace(entry.StackTrace)
            ? string.Empty
            : "  [details]";

        return $"{GetLevelLabel(entry.Level),-5}  {entry.Timestamp:HH:mm:ss.fff}  {entry.Category}  {message}{detailsMarker}";
    }

    private static string FormatLogDetails(ConsoleLogEntry entry)
    {
        var builder = new StringBuilder(256 + (entry.StackTrace?.Length ?? 0));
        builder.Append("Timestamp: ")
            .AppendLine(entry.Timestamp.ToString("O"))
            .Append("Level: ")
            .AppendLine(entry.Level.ToString())
            .Append("Category: ")
            .AppendLine(entry.Category)
            .AppendLine()
            .AppendLine(entry.Message);

        if (!string.IsNullOrWhiteSpace(entry.StackTrace))
        {
            builder.AppendLine()
                .AppendLine("Stack Trace:")
                .Append(entry.StackTrace);
        }

        return builder.ToString();
    }

    private static string GetLevelLabel(ConsoleLogLevel level) =>
        level switch
        {
            ConsoleLogLevel.Trace => "TRACE",
            ConsoleLogLevel.Debug => "DEBUG",
            ConsoleLogLevel.Information => "INFO",
            ConsoleLogLevel.Warning => "WARN",
            ConsoleLogLevel.Error => "ERROR",
            ConsoleLogLevel.Critical => "FATAL",
            ConsoleLogLevel.Command => ">",
            ConsoleLogLevel.CommandResult => "RESULT",
            _ => level.ToString().ToUpperInvariant()
        };

    private static Vector4 GetLevelColor(ConsoleLogLevel level) =>
        level switch
        {
            ConsoleLogLevel.Trace => new Vector4(0.58f, 0.61f, 0.65f, 1.0f),
            ConsoleLogLevel.Debug => new Vector4(0.50f, 0.76f, 0.88f, 1.0f),
            ConsoleLogLevel.Information => new Vector4(0.88f, 0.90f, 0.92f, 1.0f),
            ConsoleLogLevel.Warning => new Vector4(1.00f, 0.76f, 0.24f, 1.0f),
            ConsoleLogLevel.Error => new Vector4(1.00f, 0.40f, 0.36f, 1.0f),
            ConsoleLogLevel.Critical => new Vector4(1.00f, 0.24f, 0.30f, 1.0f),
            ConsoleLogLevel.Command => new Vector4(0.47f, 0.78f, 1.00f, 1.0f),
            ConsoleLogLevel.CommandResult => new Vector4(0.48f, 0.88f, 0.58f, 1.0f),
            _ => Vector4.One
        };

    private void Save(IReadOnlyList<ConsoleLogEntry> entries)
    {
        try
        {
            var path = _fileDialog.ShowSaveFileDialog(
                "Save Vecxy Console Log",
                $"vecxy-log-{DateTime.Now:yyyy-MM-dd-HHmmss}.log",
                [new FileDialogFilter("Log Files", ["*.log"]), new FileDialogFilter("Text Files", ["*.txt"])]);

            if (string.IsNullOrWhiteSpace(path))
                return;

            var builder = new StringBuilder(entries.Count * 80);
            foreach (var entry in entries)
            {
                builder.Append('[')
                    .Append(entry.Timestamp.ToString("O"))
                    .Append("] [")
                    .Append(entry.Level)
                    .Append("] [")
                    .Append(entry.Category)
                    .Append("] ")
                    .AppendLine(entry.Message);

                if (!string.IsNullOrWhiteSpace(entry.StackTrace))
                    builder.AppendLine(entry.StackTrace);
            }

            File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
            _console.Write(
                new ConsoleLogEntry(
                    DateTime.Now,
                    ConsoleLogLevel.CommandResult,
                    "Console",
                    $"Saved {entries.Count} log entries to '{path}'.",
                    null));
        }
        catch (Exception exception)
        {
            _console.Write(
                new ConsoleLogEntry(
                    DateTime.Now,
                    ConsoleLogLevel.Error,
                    "Console",
                    $"Failed to save console log: {exception.Message}",
                    exception.ToString()));
        }
    }

    private ConsoleSuggestion? TryGetSelectedSuggestion()
    {
        if (_selectedSuggestionIndex < 0 ||
            _selectedSuggestionIndex >= _currentSuggestions.Count)
        {
            return null;
        }

        return _currentSuggestions[_selectedSuggestionIndex];
    }

    private int ResolveSelectedSuggestionIndex(ConsoleSuggestion? selectedSuggestion)
    {
        if (selectedSuggestion is null)
            return -1;

        for (var index = 0; index < _currentSuggestions.Count; index++)
        {
            var current = _currentSuggestions[index];
            if (string.Equals(current.DisplayText, selectedSuggestion.DisplayText, StringComparison.Ordinal) &&
                string.Equals(current.InsertText, selectedSuggestion.InsertText, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}
