using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;

namespace LuminaIDE;

/// <summary>
/// Chat panel for Claude Code and Codex. Each turn runs the agent's CLI headlessly inside the
/// open folder (see core/src/agent.rs); this view only renders the normalized event stream.
/// </summary>
public partial class AgentView : UserControl
{
    sealed record AgentInfo(string Id, string Name, string Install);

    static readonly AgentInfo[] Agents =
    [
        new("claude", "Claude Code", "Install Claude Code (https://claude.com/claude-code), sign in once with `claude`, then try again."),
        new("codex", "Codex", "Install the Codex CLI (`npm i -g @openai/codex`), sign in with `codex login`, then try again."),
    ];

    const int MaxSelectionChars = 4000;

    Settings _settings = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly Dictionary<string, string> _sessions = [];
    readonly StringBuilder _currentText = new();
    IntPtr _handle;
    string _runningAgent = "claude";
    SelectableTextBlock? _current;
    Control? _greeting;
    bool _includeContext = true;
    string? _contextFile;

    /// <summary>The open folder the agent works in, or null.</summary>
    public Func<string?>? RootProvider { get; set; }
    /// <summary>Active file (relative path) and selected text, for the "include open file" option.</summary>
    public Func<(string? File, string? Selection)>? ContextProvider { get; set; }
    public event Action? CloseRequested;
    /// <summary>Raised when a turn ends, so the window can pick up files the agent changed.</summary>
    public event Action? TurnFinished;

    public bool Running => _handle != IntPtr.Zero;
    AgentInfo Current => Agents.First(a => a.Id == _settings.Agent);

    public AgentView()
    {
        AvaloniaXamlLoader.Load(this);
        _timer.Tick += (_, _) => Pump();

        Ctl<Border>("NewChatButton").Tapped += (_, _) => NewChat();
        Ctl<Border>("CloseButton").Tapped += (_, _) => CloseRequested?.Invoke();
        Ctl<Border>("SendButton").Tapped += (_, _) => SendOrStop();
        Ctl<Border>("ContextChip").Tapped += (_, _) => { _includeContext = !_includeContext; UpdateContext(_contextFile); };
        // Tunnel so Enter sends instead of the TextBox inserting a newline (Shift+Enter still does).
        Ctl<TextBox>("Input").AddHandler(KeyDownEvent, OnInputKey, RoutingStrategies.Tunnel);
    }

    T Ctl<T>(string name) where T : Control => this.FindControl<T>(name)!;

    public void Init(Settings settings)
    {
        _settings = settings;
        if (Agents.All(a => a.Id != _settings.Agent)) _settings.Agent = "claude";
        if (_settings.AgentMode != "edit") _settings.AgentMode = "read";
        Refresh();
        ShowGreeting();
    }

    /// <summary>Fills the prompt box and sends it (used by the --ask startup flag).</summary>
    public void Ask(string prompt)
    {
        Ctl<TextBox>("Input").Text = prompt;
        SendOrStop();
    }

    /// <summary>Re-reads the agent and mode from the shared settings (they can change on the Settings page).</summary>
    public void SyncFromSettings()
    {
        if (Running) return;
        if (Agents.All(a => a.Id != _settings.Agent)) _settings.Agent = "claude";
        Refresh();
        ShowGreetingIfEmpty();
    }

    public void FocusInput() => Ctl<TextBox>("Input").Focus();

    /// <summary>Called by the window whenever the active file changes.</summary>
    public void UpdateContext(string? fileName)
    {
        _contextFile = fileName;
        var chip = Ctl<Border>("ContextChip");
        var text = Ctl<TextBlock>("ContextText");
        chip.IsVisible = fileName is not null;
        chip.Classes.Set("active", _includeContext);
        text.Text = _includeContext ? $"Including {fileName}" : $"Not including {fileName}";
    }

    /// <summary>Stops any turn and forgets the conversation (used when the folder changes too).</summary>
    public void NewChat()
    {
        Stop();
        Release();
        _sessions.Clear();
        Ctl<StackPanel>("Transcript").Children.Clear();
        _current = null;
        SetRunning(false);
        ShowGreeting();
    }

    public void Shutdown()
    {
        _timer.Stop();
        Stop();
        Release();
    }

    // ------------------------------------------------------------- chrome --

    void Refresh()
    {
        var agents = Ctl<WrapPanel>("AgentChips");
        agents.Children.Clear();
        foreach (var a in Agents)
            agents.Children.Add(Chip(a.Name, a.Id == _settings.Agent, () => { if (!Running) { _settings.Agent = a.Id; _settings.Save(); Refresh(); ShowGreetingIfEmpty(); } }));

        var modes = Ctl<WrapPanel>("ModeChips");
        modes.Children.Clear();
        modes.Children.Add(Chip("Plan · read-only", _settings.AgentMode == "read", () => SetMode("read")));
        modes.Children.Add(Chip("Edit files", _settings.AgentMode == "edit", () => SetMode("edit")));

        Ctl<TextBlock>("ModeHint").Text = _settings.AgentMode == "edit"
            ? _settings.Agent == "claude"
                ? "Claude may create and change files in this folder. Shell commands still follow your Claude Code permission settings."
                : "Codex may create and change files in this folder, inside its workspace-write sandbox."
            : "The agent can read and search your files but cannot change anything.";
        Ctl<TextBox>("Input").Watermark = $"Ask {Current.Name}…  (Enter to send, Shift+Enter for a new line)";
    }

    void SetMode(string mode)
    {
        if (Running) return;
        _settings.AgentMode = mode;
        _settings.Save();
        Refresh();
    }

    static Border Chip(string text, bool active, Action tap)
    {
        var chip = new Border { Child = new TextBlock { Text = text, FontSize = 12 }, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 4) };
        chip.Classes.Add("chip");
        chip.Classes.Set("active", active);
        chip.Tapped += (_, _) => tap();
        return chip;
    }

    void ShowGreetingIfEmpty()
    {
        if (Ctl<StackPanel>("Transcript").Children.Count <= 1) { Ctl<StackPanel>("Transcript").Children.Clear(); ShowGreeting(); }
    }

    void ShowGreeting()
    {
        var root = RootProvider?.Invoke();
        var folder = root is null ? "the open folder" : Path.GetFileName(root.TrimEnd('/', '\\'));
        var text = Core.AgentAvailable(_settings.Agent)
            ? $"Ask {Current.Name} about {folder}. It runs in that folder; your open file and selection can come along as context."
            : $"{Current.Name} isn't installed or isn't on your PATH. {Current.Install}";
        AddNote(text, error: false);
        _greeting = Ctl<StackPanel>("Transcript").Children[^1];
    }

    void SetRunning(bool running, string status = "")
    {
        Ctl<TextBlock>("SendText").Text = running ? "Stop" : "Send";
        Ctl<TextBlock>("StatusText").Text = status;
    }

    // --------------------------------------------------------- transcript --

    void AddNote(string text, bool error)
    {
        var tb = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        tb.Classes.Add(error ? "error" : "muted");
        Add(tb);
    }

    void AddUser(string text, string? context)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        if (context is not null)
        {
            var c = new TextBlock { Text = context, FontSize = 11 };
            c.Classes.Add("muted");
            stack.Children.Add(c);
        }
        var bubble = new Border { Child = stack };
        bubble.Classes.Add("user");
        Add(bubble);
    }

    void AddTool(string name, string? detail, bool denied = false)
    {
        var tb = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 };
        tb.Classes.Add(denied ? "error" : "muted");
        tb.Inlines!.Add(new Run((denied ? "✕ " : "● ") + name) { FontWeight = FontWeight.SemiBold });
        if (!string.IsNullOrEmpty(detail)) tb.Inlines.Add(new Run("  " + detail));
        Add(tb);
    }

    void Add(Control c)
    {
        Ctl<StackPanel>("Transcript").Children.Add(c);
        ScrollDown();
    }

    void AppendAssistant(string text)
    {
        if (_current is null)
        {
            text = text.TrimStart('\n', '\r');
            if (text.Length == 0) return;
            _current = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
            _currentText.Clear();
            Ctl<StackPanel>("Transcript").Children.Add(_current);
        }
        _currentText.Append(text);
        _current.Text = _currentText.ToString();
    }

    void ScrollDown() => Dispatcher.UIThread.Post(Ctl<ScrollViewer>("Scroll").ScrollToEnd, DispatcherPriority.Background);

    // ---------------------------------------------------------------- run --

    void OnInputKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            if (!Running) SendOrStop();
        }
    }

    void SendOrStop()
    {
        if (Running) { Stop(); return; }

        var input = Ctl<TextBox>("Input");
        var prompt = input.Text?.Trim() ?? "";
        if (prompt.Length == 0) return;

        var root = RootProvider?.Invoke();
        if (root is null) { AddNote("Open a folder first — the agent works inside it.", error: true); return; }
        if (!Core.AgentAvailable(_settings.Agent))
        {
            var t = Ctl<StackPanel>("Transcript");
            if (t.Children.Count == 1 && ReferenceEquals(t.Children[0], _greeting)) SetRunning(false, $"{Current.Name} isn't installed — see above.");
            else AddNote($"{Current.Name} isn't installed or isn't on your PATH. {Current.Install}", error: true);
            return;
        }

        var (file, selection) = ContextProvider?.Invoke() ?? (null, null);
        var full = prompt;
        string? shown = null;
        if (_includeContext && file is not null)
        {
            var sel = string.IsNullOrWhiteSpace(selection) ? null : selection!.Length > MaxSelectionChars ? selection[..MaxSelectionChars] + "\n…" : selection;
            full = $"[Editor context] The user has `{file}` open in their editor" +
                   (sel is null ? "." : $" with this text selected:\n```\n{sel}\n```") + $"\n\n{prompt}";
            shown = sel is null ? $"with {file}" : $"with {file} + selection";
        }

        _runningAgent = _settings.Agent;
        var config = JsonSerializer.Serialize(new
        {
            agent = _runningAgent,
            mode = _settings.AgentMode,
            cwd = root,
            prompt = full,
            session = _sessions.GetValueOrDefault(_runningAgent),
        });

        var handle = Core.AgentStart(config);
        if (handle == IntPtr.Zero) { AddNote("Could not start the agent.", error: true); return; }

        input.Text = "";
        AddUser(prompt, shown);
        _current = null;
        _handle = handle;
        SetRunning(true, $"{Current.Name} is working…");
        _timer.Start();
    }

    void Stop()
    {
        if (Running) Core.AgentStop(_handle);
    }

    void Release()
    {
        if (!Running) return;
        Core.AgentFree(_handle);
        _handle = IntPtr.Zero;
    }

    void Pump()
    {
        if (!Running) { _timer.Stop(); return; }
        bool finished = false;
        foreach (var ev in Core.AgentPoll(_handle))
        {
            switch (ev.k)
            {
                case "session" when ev.id is not null:
                    _sessions[_runningAgent] = ev.id;
                    break;
                case "text" when ev.t is not null:
                    AppendAssistant(ev.t);
                    break;
                case "tool":
                    _current = null;
                    AddTool(ev.name ?? "tool", ev.detail);
                    break;
                case "tool_error":
                    AddNote("  " + ev.t, error: true);
                    break;
                case "denied":
                    _current = null;
                    AddTool($"Blocked {ev.name}", ev.detail, denied: true);
                    if (_settings.AgentMode == "read")
                        AddNote("Plan mode is read-only. Switch to “Edit files” (or allow the tool in your CLI settings) to let it run.", error: false);
                    break;
                case "error":
                    AddNote(ev.t ?? "The agent reported an error.", error: true);
                    break;
                case "done":
                    finished = true;
                    if (ev.session is not null) _sessions[_runningAgent] = ev.session;
                    Finish(ev);
                    break;
            }
        }
        ScrollDown();
        if (finished) TurnFinished?.Invoke();
    }

    void Finish(AgentEvent done)
    {
        _timer.Stop();
        Release();
        _current = null;
        if (done.ok != true && !string.IsNullOrWhiteSpace(done.t))
            AddNote(done.t!, error: done.t != "Stopped.");
        SetRunning(false, done.cost is { } c ? $"Done · ${c:0.0000}" : done.ok == true ? "Done" : "");
    }
}
