//! Runs a coding agent (Claude Code or Codex) headlessly and turns its JSON
//! stream into a small, agent-neutral event vocabulary for the UI:
//!
//! | `k`          | fields                                   |
//! |--------------|------------------------------------------|
//! | `session`    | `id`, `t` (model, optional)              |
//! | `text`       | `t` – append to the assistant message    |
//! | `tool`       | `name`, `detail`                         |
//! | `tool_error` | `t`                                      |
//! | `denied`     | `name`, `detail` – permission refused    |
//! | `error`      | `t`                                      |
//! | `done`       | `ok`, `cost`, `session`, `t` (error)     |
//!
//! The prompt is written to the child's stdin (never argv), and no shell is involved.

use std::collections::{HashSet, VecDeque};
use std::env;
use std::ffi::c_char;
use std::io::{BufRead, BufReader, Read, Write};
use std::path::{Path, PathBuf};
use std::process::{Child, Command, Stdio};
use std::ptr;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc::{channel, Receiver};
use std::sync::{Arc, Mutex};
use std::thread;
use std::time::{Duration, Instant};

use serde::Deserialize;
use serde_json::{json, Value};

use crate::{arg, out};

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
struct Config {
    /// "claude" or "codex".
    agent: String,
    /// "read" (plan / read-only sandbox) or "edit" (may change files in `cwd`).
    mode: String,
    cwd: String,
    prompt: String,
    /// Session/thread id from a previous turn, to continue the conversation.
    session: Option<String>,
}

#[derive(Clone, Copy, PartialEq, Debug)]
enum Kind {
    Claude,
    Codex,
}

impl Kind {
    fn parse(s: &str) -> Option<Kind> {
        match s {
            "claude" => Some(Kind::Claude),
            "codex" => Some(Kind::Codex),
            _ => None,
        }
    }

    fn exe_name(self) -> &'static str {
        match self {
            Kind::Claude => "claude",
            Kind::Codex => "codex",
        }
    }
}

// ------------------------------------------------------------ executables --

fn is_executable(p: &Path) -> bool {
    let Ok(meta) = p.metadata() else { return false };
    if !meta.is_file() {
        return false;
    }
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        meta.permissions().mode() & 0o111 != 0
    }
    #[cfg(not(unix))]
    {
        true
    }
}

/// PATH plus the places CLIs like these usually install to. A desktop-launched
/// GUI often has a much shorter PATH than the user's shell.
fn search_dirs() -> Vec<PathBuf> {
    let mut dirs: Vec<PathBuf> = env::var_os("PATH").map(|p| env::split_paths(&p).collect()).unwrap_or_default();
    if let Some(appdata) = env::var_os("APPDATA") {
        dirs.push(PathBuf::from(appdata).join("npm")); // npm's global bin folder on Windows
    }
    if let Some(home) = env::var_os("HOME").or_else(|| env::var_os("USERPROFILE")).map(PathBuf::from) {
        for d in [".local/bin", ".npm-global/bin", ".bun/bin", ".cargo/bin", ".claude/local", ".volta/bin"] {
            dirs.push(home.join(d));
        }
        if let Ok(versions) = std::fs::read_dir(home.join(".nvm/versions/node")) {
            dirs.extend(versions.filter_map(Result::ok).map(|v| v.path().join("bin")));
        }
    }
    dirs.extend(["/usr/local/bin", "/opt/homebrew/bin", "/usr/bin"].map(PathBuf::from));
    dirs
}

/// Looks for `name` (and, on Windows, `name.exe` / `.cmd` / `.bat`) on PATH and the usual install folders.
pub fn find_executable(name: &str) -> Option<PathBuf> {
    let exts: &[&str] = if cfg!(windows) { &[".exe", ".cmd", ".bat", ""] } else { &[""] };
    for dir in search_dirs() {
        for ext in exts {
            let candidate = dir.join(format!("{name}{ext}"));
            if is_executable(&candidate) {
                return Some(candidate);
            }
        }
    }
    None
}

// ---------------------------------------------------------------- command --

/// Session ids end up in argv, so only accept plain identifiers (no leading `-`).
fn safe_session(s: &str) -> bool {
    !s.is_empty() && !s.starts_with('-') && s.chars().all(|c| c.is_ascii_alphanumeric() || c == '-' || c == '_')
}

fn build_command(kind: Kind, cfg: &Config, exe: &Path) -> Command {
    let edit = cfg.mode == "edit";
    let session = cfg.session.as_deref().filter(|s| safe_session(s));
    let mut c = Command::new(exe);

    match kind {
        Kind::Claude => {
            c.args(["-p", "--output-format", "stream-json", "--verbose", "--include-partial-messages"]);
            c.args(["--permission-mode", if edit { "acceptEdits" } else { "plan" }]);
            if let Some(s) = session {
                c.args(["--resume", s]);
            }
        }
        Kind::Codex => {
            c.args(["exec", "--json", "--skip-git-repo-check"]);
            c.args(["--sandbox", if edit { "workspace-write" } else { "read-only" }]);
            if let Some(s) = session {
                c.args(["resume", s]);
            }
            c.arg("-"); // read the prompt from stdin
        }
    }

    c.current_dir(&cfg.cwd).stdin(Stdio::piped()).stdout(Stdio::piped()).stderr(Stdio::piped());
    c
}

// ----------------------------------------------------------------- parser --

fn text(t: &str) -> Value {
    json!({ "k": "text", "t": t })
}

fn clip(s: &str, max: usize) -> String {
    let one_line = s.split_whitespace().collect::<Vec<_>>().join(" ");
    if one_line.chars().count() > max {
        let cut: String = one_line.chars().take(max).collect();
        format!("{cut}…")
    } else {
        one_line
    }
}

/// The most informative single argument of a tool call, for a one-line summary.
fn tool_detail(input: &Value) -> String {
    for key in ["file_path", "path", "command", "pattern", "url", "query", "description", "prompt"] {
        if let Some(s) = input.get(key).and_then(Value::as_str) {
            return clip(s, 120);
        }
    }
    String::new()
}

/// Tool results are either a string or a list of `{type:"text", text}` blocks.
fn result_text(content: &Value) -> String {
    match content {
        Value::String(s) => s.clone(),
        Value::Array(items) => items.iter().filter_map(|b| b.get("text").and_then(Value::as_str)).collect::<Vec<_>>().join("\n"),
        _ => String::new(),
    }
}

struct Parser {
    kind: Kind,
    /// Claude sends each message twice (streamed deltas, then the whole block); use the deltas.
    saw_partial: bool,
    /// Codex sends whole messages; separate consecutive ones.
    emitted_text: bool,
    announced: HashSet<String>,
    done: bool,
}

impl Parser {
    fn new(kind: Kind) -> Self {
        Parser { kind, saw_partial: false, emitted_text: false, announced: HashSet::new(), done: false }
    }

    fn feed(&mut self, line: &str) -> Vec<Value> {
        let Ok(v) = serde_json::from_str::<Value>(line) else { return vec![] };
        match self.kind {
            Kind::Claude => self.claude(&v),
            Kind::Codex => self.codex(&v),
        }
    }

    fn claude(&mut self, v: &Value) -> Vec<Value> {
        let mut out = vec![];
        match v["type"].as_str() {
            Some("system") if v["subtype"] == "init" => {
                out.push(json!({ "k": "session", "id": v["session_id"], "t": v["model"] }));
            }
            Some("stream_event") => {
                let delta = &v["event"]["delta"];
                if v["event"]["type"] == "content_block_delta" && delta["type"] == "text_delta" {
                    if let Some(t) = delta["text"].as_str() {
                        self.saw_partial = true;
                        out.push(text(t));
                    }
                }
            }
            Some("assistant") => {
                for block in v["message"]["content"].as_array().into_iter().flatten() {
                    match block["type"].as_str() {
                        Some("tool_use") => out.push(json!({
                            "k": "tool", "name": block["name"], "detail": tool_detail(&block["input"]),
                        })),
                        Some("text") if !self.saw_partial => {
                            if let Some(t) = block["text"].as_str() {
                                out.push(text(t));
                            }
                        }
                        _ => {}
                    }
                }
            }
            Some("user") => {
                for block in v["message"]["content"].as_array().into_iter().flatten() {
                    if block["type"] == "tool_result" && block["is_error"] == true {
                        out.push(json!({ "k": "tool_error", "t": clip(&result_text(&block["content"]), 300) }));
                    }
                }
            }
            Some("result") => {
                self.done = true;
                for d in v["permission_denials"].as_array().into_iter().flatten() {
                    out.push(json!({ "k": "denied", "name": d["tool_name"], "detail": tool_detail(&d["tool_input"]) }));
                }
                let failed = v["is_error"] == true || v["subtype"].as_str().is_some_and(|s| s.starts_with("error"));
                out.push(json!({
                    "k": "done", "ok": !failed, "cost": v["total_cost_usd"], "session": v["session_id"],
                    "t": if failed { v["result"].clone() } else { Value::Null },
                }));
            }
            _ => {}
        }
        out
    }

    /// Codex `exec --json` events. Shapes follow the documented item types
    /// (agent_message, command_execution, file_change, ...); unknown events are ignored.
    fn codex(&mut self, v: &Value) -> Vec<Value> {
        let mut out = vec![];
        match v["type"].as_str() {
            Some("thread.started") => out.push(json!({ "k": "session", "id": v["thread_id"] })),
            Some(kind @ ("item.started" | "item.completed")) => {
                let item = &v["item"];
                let completed = kind == "item.completed";
                let id = item["id"].as_str().unwrap_or("").to_string();
                match item["type"].as_str() {
                    Some("agent_message") if completed => {
                        if let Some(t) = item["text"].as_str() {
                            let sep = if self.emitted_text { "\n\n" } else { "" };
                            self.emitted_text = true;
                            out.push(text(&format!("{sep}{t}")));
                        }
                    }
                    Some("command_execution") => {
                        if self.announced.insert(id.clone()) {
                            self.emitted_text = false;
                            out.push(json!({ "k": "tool", "name": "Run", "detail": clip(item["command"].as_str().unwrap_or(""), 120) }));
                        }
                        if completed {
                            if let Some(code) = item["exit_code"].as_i64().filter(|c| *c != 0) {
                                let tail = item["aggregated_output"].as_str().unwrap_or("").lines().last().unwrap_or("");
                                out.push(json!({ "k": "tool_error", "t": format!("exit {code}: {}", clip(tail, 200)) }));
                            }
                        }
                    }
                    Some("file_change") if completed => {
                        self.emitted_text = false;
                        for ch in item["changes"].as_array().into_iter().flatten() {
                            let name = match ch["kind"].as_str() {
                                Some("add") => "Create",
                                Some("delete") => "Delete",
                                _ => "Edit",
                            };
                            out.push(json!({ "k": "tool", "name": name, "detail": ch["path"] }));
                        }
                    }
                    Some("mcp_tool_call") if self.announced.insert(id.clone()) => {
                        self.emitted_text = false;
                        let name = format!("{}.{}", item["server"].as_str().unwrap_or("mcp"), item["tool"].as_str().unwrap_or("tool"));
                        out.push(json!({ "k": "tool", "name": name, "detail": "" }));
                    }
                    Some("web_search") if self.announced.insert(id.clone()) => {
                        self.emitted_text = false;
                        out.push(json!({ "k": "tool", "name": "Search", "detail": clip(item["query"].as_str().unwrap_or(""), 120) }));
                    }
                    Some("error") if completed => out.push(json!({ "k": "error", "t": item["message"] })),
                    _ => {}
                }
            }
            Some("turn.completed") => {
                self.done = true;
                out.push(json!({ "k": "done", "ok": true, "cost": Value::Null, "session": Value::Null, "t": Value::Null }));
            }
            Some("turn.failed") => {
                self.done = true;
                let msg = v["error"]["message"].as_str().unwrap_or("The agent reported a failure.");
                out.push(json!({ "k": "done", "ok": false, "cost": Value::Null, "session": Value::Null, "t": msg }));
            }
            Some("error") => out.push(json!({ "k": "error", "t": v["message"] })),
            _ => {}
        }
        out
    }

    /// Called once the process has exited; guarantees exactly one `done`.
    fn finish(&mut self, exited_ok: bool, stopped: bool, stderr_tail: &str) -> Vec<Value> {
        if self.done {
            return vec![];
        }
        self.done = true;
        if stopped {
            return vec![json!({ "k": "done", "ok": false, "cost": Value::Null, "session": Value::Null, "t": "Stopped." })];
        }
        if exited_ok {
            return vec![json!({ "k": "done", "ok": true, "cost": Value::Null, "session": Value::Null, "t": Value::Null })];
        }
        let msg = if stderr_tail.trim().is_empty() { "The agent exited with an error.".to_string() } else { stderr_tail.trim().to_string() };
        vec![json!({ "k": "done", "ok": false, "cost": Value::Null, "session": Value::Null, "t": msg })]
    }
}

// ------------------------------------------------------------------ agent --

pub struct Agent {
    rx: Mutex<Receiver<Value>>,
    child: Arc<Mutex<Option<Child>>>,
    stopped: Arc<AtomicBool>,
}

impl Agent {
    fn failed(msg: String) -> Agent {
        let (tx, rx) = channel();
        let _ = tx.send(json!({ "k": "done", "ok": false, "cost": Value::Null, "session": Value::Null, "t": msg }));
        Agent { rx: Mutex::new(rx), child: Arc::new(Mutex::new(None)), stopped: Arc::new(AtomicBool::new(false)) }
    }

    fn spawn(kind: Kind, cfg: Config, exe: &Path) -> Agent {
        let mut child = match build_command(kind, &cfg, exe).spawn() {
            Ok(c) => c,
            Err(e) => return Agent::failed(format!("Could not start {}: {e}", exe.display())),
        };

        let stdin = child.stdin.take();
        let stdout = child.stdout.take();
        let stderr = child.stderr.take();
        let child = Arc::new(Mutex::new(Some(child)));
        let stopped = Arc::new(AtomicBool::new(false));
        let (tx, rx) = channel::<Value>();

        // Prompt goes over stdin from its own thread so a big prompt can't deadlock us.
        let prompt = cfg.prompt;
        thread::spawn(move || {
            if let Some(mut si) = stdin {
                let _ = si.write_all(prompt.as_bytes());
            }
        });

        let tail: Arc<Mutex<VecDeque<String>>> = Arc::new(Mutex::new(VecDeque::new()));
        if let Some(se) = stderr {
            let tail = tail.clone();
            thread::spawn(move || {
                for line in BufReader::new(se).lines().map_while(Result::ok) {
                    let mut t = tail.lock().unwrap();
                    if t.len() >= 20 {
                        t.pop_front();
                    }
                    t.push_back(line);
                }
            });
        }

        let parser = Arc::new(Mutex::new(Parser::new(kind)));
        let reader_done = Arc::new(AtomicBool::new(false));
        if let Some(so) = stdout {
            let (parser, tx, reader_done) = (parser.clone(), tx.clone(), reader_done.clone());
            thread::spawn(move || {
                for line in BufReader::new(so.take(64 * 1024 * 1024)).lines().map_while(Result::ok) {
                    for ev in parser.lock().unwrap().feed(&line) {
                        let _ = tx.send(ev);
                    }
                }
                reader_done.store(true, Ordering::SeqCst);
            });
        } else {
            reader_done.store(true, Ordering::SeqCst);
        }

        // Supervisor: notices exit (or a stop request) and guarantees a final `done`.
        {
            let (child, stopped, parser, tail) = (child.clone(), stopped.clone(), parser.clone(), tail.clone());
            thread::spawn(move || {
                let status = loop {
                    let polled = child.lock().unwrap().as_mut().map(|c| c.try_wait());
                    match polled {
                        Some(Ok(Some(st))) => break Some(st),
                        Some(Ok(None)) => thread::sleep(Duration::from_millis(30)),
                        _ => break None,
                    }
                };
                // Let the reader drain what the child wrote before it exited (bounded: tool
                // subprocesses can keep the pipe open).
                let deadline = Instant::now() + Duration::from_millis(1500);
                while !reader_done.load(Ordering::SeqCst) && Instant::now() < deadline {
                    thread::sleep(Duration::from_millis(10));
                }
                let tail_text = tail.lock().unwrap().iter().cloned().collect::<Vec<_>>().join("\n");
                let events = parser.lock().unwrap().finish(status.is_some_and(|s| s.success()), stopped.load(Ordering::SeqCst), &tail_text);
                for ev in events {
                    let _ = tx.send(ev);
                }
            });
        }

        Agent { rx: Mutex::new(rx), child, stopped }
    }

    fn poll(&self) -> Vec<Value> {
        self.rx.lock().map(|rx| rx.try_iter().collect()).unwrap_or_default()
    }

    fn stop(&self) {
        self.stopped.store(true, Ordering::SeqCst);
        if let Some(c) = self.child.lock().unwrap().as_mut() {
            let _ = c.kill();
        }
    }
}

impl Drop for Agent {
    fn drop(&mut self) {
        self.stop();
    }
}

// ---------------------------------------------------------------- C ABI --

/// 1 if the agent's CLI ("claude" | "codex") can be found, else 0.
#[no_mangle]
pub extern "C" fn lumina_agent_available(agent: *const c_char) -> i32 {
    arg(agent).and_then(Kind::parse).and_then(|k| find_executable(k.exe_name())).is_some() as i32
}

/// Starts one agent turn from a JSON config. Always returns a handle (failures
/// arrive as a `done` event) unless the config itself is unusable, then null.
#[no_mangle]
pub extern "C" fn lumina_agent_start(config: *const c_char) -> *mut Agent {
    let Some(cfg) = arg(config).and_then(|s| serde_json::from_str::<Config>(s).ok()) else {
        return ptr::null_mut();
    };
    let Some(kind) = Kind::parse(&cfg.agent) else { return ptr::null_mut() };
    if !Path::new(&cfg.cwd).is_dir() {
        return Box::into_raw(Box::new(Agent::failed(format!("Folder not found: {}", cfg.cwd))));
    }
    let agent = match find_executable(kind.exe_name()) {
        Some(exe) => Agent::spawn(kind, cfg, &exe),
        None => Agent::failed(format!("`{}` was not found. Install it and make sure it is on your PATH.", kind.exe_name())),
    };
    Box::into_raw(Box::new(agent))
}

/// JSON array of events produced since the previous poll.
#[no_mangle]
pub extern "C" fn lumina_agent_poll(h: *mut Agent) -> *mut c_char {
    if h.is_null() {
        return ptr::null_mut();
    }
    out(Value::Array(unsafe { &*h }.poll()).to_string())
}

#[no_mangle]
pub extern "C" fn lumina_agent_stop(h: *mut Agent) {
    if !h.is_null() {
        unsafe { &*h }.stop();
    }
}

/// Stops the agent if it is still running and releases the handle.
#[no_mangle]
pub extern "C" fn lumina_agent_free(h: *mut Agent) {
    if !h.is_null() {
        drop(unsafe { Box::from_raw(h) });
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const CLAUDE_FIXTURE: &str = include_str!("../tests/fixtures/claude_read.jsonl");

    fn run_all(kind: Kind, lines: &str) -> Vec<Value> {
        let mut p = Parser::new(kind);
        lines.lines().flat_map(|l| p.feed(l)).collect()
    }

    fn joined_text(events: &[Value]) -> String {
        events.iter().filter(|e| e["k"] == "text").filter_map(|e| e["t"].as_str()).collect()
    }

    #[test]
    fn claude_real_capture_streams_text_tools_and_result() {
        let ev = run_all(Kind::Claude, CLAUDE_FIXTURE);

        assert_eq!(ev[0]["k"], "session");
        assert_eq!(ev[0]["id"], "1a1a3b79-84d0-4322-b077-74cfdf05291f");

        let tool = ev.iter().find(|e| e["k"] == "tool").expect("a tool call");
        assert_eq!(tool["name"], "Read");
        assert_eq!(tool["detail"], "/work/project/note.txt");

        // Text comes from the streamed deltas only, not duplicated by the final assistant block.
        let text = joined_text(&ev);
        assert!(text.contains("contains the single word \"hello\"."), "got: {text}");
        assert_eq!(text.matches("read-only task").count(), 1);

        let done: Vec<_> = ev.iter().filter(|e| e["k"] == "done").collect();
        assert_eq!(done.len(), 1);
        assert_eq!(done[0]["ok"], true);
        assert!(done[0]["cost"].as_f64().unwrap() > 0.0);
        assert_eq!(done[0]["session"], "1a1a3b79-84d0-4322-b077-74cfdf05291f");
    }

    #[test]
    fn claude_reports_errors_and_permission_denials() {
        let lines = concat!(
            r#"{"type":"user","message":{"content":[{"type":"tool_result","is_error":true,"content":"File not found\nreally"}]}}"#, "\n",
            r#"{"type":"result","subtype":"success","is_error":false,"total_cost_usd":0.5,"session_id":"s1","permission_denials":[{"tool_name":"Bash","tool_input":{"command":"rm -rf build"}}]}"#, "\n",
        );
        let ev = run_all(Kind::Claude, lines);
        assert_eq!(ev[0], json!({ "k": "tool_error", "t": "File not found really" }));
        assert_eq!(ev[1], json!({ "k": "denied", "name": "Bash", "detail": "rm -rf build" }));
        assert_eq!(ev[2]["k"], "done");

        let failed = run_all(Kind::Claude, r#"{"type":"result","subtype":"error_max_turns","is_error":true,"result":"Too many turns"}"#);
        assert_eq!(failed[0]["ok"], false);
        assert_eq!(failed[0]["t"], "Too many turns");
    }

    // Codex is not installed where this was written, so this stream follows the
    // documented `codex exec --json` item types rather than a live capture.
    #[test]
    fn codex_documented_shape() {
        let lines = concat!(
            r#"{"type":"thread.started","thread_id":"0199-abc"}"#, "\n",
            r#"{"type":"turn.started"}"#, "\n",
            r#"{"type":"item.completed","item":{"id":"i0","type":"reasoning","text":"thinking"}}"#, "\n",
            r#"{"type":"item.started","item":{"id":"i1","type":"command_execution","command":"bash -lc ls","status":"in_progress"}}"#, "\n",
            r#"{"type":"item.completed","item":{"id":"i1","type":"command_execution","command":"bash -lc ls","aggregated_output":"a\nboom","exit_code":2,"status":"failed"}}"#, "\n",
            r#"{"type":"item.completed","item":{"id":"i2","type":"file_change","changes":[{"path":"src/a.rs","kind":"update"},{"path":"b.txt","kind":"add"}],"status":"completed"}}"#, "\n",
            r#"{"type":"item.completed","item":{"id":"i3","type":"agent_message","text":"Done."}}"#, "\n",
            r#"{"type":"item.completed","item":{"id":"i4","type":"agent_message","text":"Really."}}"#, "\n",
            "not json at all\n",
            r#"{"type":"turn.completed","usage":{"input_tokens":1,"output_tokens":2}}"#, "\n",
        );
        let ev = run_all(Kind::Codex, lines);
        let kinds: Vec<_> = ev.iter().map(|e| e["k"].as_str().unwrap()).collect();
        assert_eq!(kinds, ["session", "tool", "tool_error", "tool", "tool", "text", "text", "done"]);
        assert_eq!(ev[0]["id"], "0199-abc");
        assert_eq!(ev[1], json!({ "k": "tool", "name": "Run", "detail": "bash -lc ls" })); // announced once
        assert_eq!(ev[2]["t"], "exit 2: boom");
        assert_eq!(ev[3]["name"], "Edit");
        assert_eq!(ev[4]["name"], "Create");
        assert_eq!(joined_text(&ev), "Done.\n\nReally.");
    }

    #[test]
    fn finish_emits_exactly_one_done() {
        let mut p = Parser::new(Kind::Claude);
        assert_eq!(p.finish(false, false, "boom\n")[0]["t"], "boom");
        assert!(p.finish(false, false, "").is_empty());

        let mut p = Parser::new(Kind::Codex);
        p.feed(r#"{"type":"turn.completed"}"#);
        assert!(p.finish(true, false, "").is_empty());

        assert_eq!(Parser::new(Kind::Claude).finish(false, true, "")[0]["t"], "Stopped.");
    }

    fn args(kind: Kind, mode: &str, session: Option<&str>) -> Vec<String> {
        let cfg = Config { agent: String::new(), mode: mode.into(), cwd: ".".into(), prompt: String::new(), session: session.map(String::from) };
        build_command(kind, &cfg, Path::new("/bin/true")).get_args().map(|a| a.to_string_lossy().into_owned()).collect()
    }

    #[test]
    fn commands_match_mode_and_guard_session_ids() {
        let a = args(Kind::Claude, "edit", Some("abc-123"));
        assert!(a.windows(2).any(|w| w == ["--permission-mode", "acceptEdits"]));
        assert!(a.windows(2).any(|w| w == ["--resume", "abc-123"]));

        let a = args(Kind::Claude, "read", None);
        assert!(a.windows(2).any(|w| w == ["--permission-mode", "plan"]));
        assert!(!a.contains(&"--resume".to_string()));

        let a = args(Kind::Codex, "read", Some("--dangerously-bypass-approvals-and-sandbox"));
        assert!(a.windows(2).any(|w| w == ["--sandbox", "read-only"]));
        assert!(!a.iter().any(|x| x.contains("dangerously")), "unsafe session ids must be dropped: {a:?}");
        assert_eq!(a.last().unwrap(), "-");
    }

    #[cfg(unix)]
    #[test]
    fn end_to_end_with_a_fake_cli() {
        use std::os::unix::fs::PermissionsExt;
        let dir = tempfile::tempdir().unwrap();
        let fixture = dir.path().join("out.jsonl");
        std::fs::write(&fixture, CLAUDE_FIXTURE).unwrap();
        let exe = dir.path().join("fake-claude");
        std::fs::write(&exe, format!("#!/bin/sh\ncat > {p}/prompt.txt\ncat '{f}'\n", p = dir.path().display(), f = fixture.display())).unwrap();
        std::fs::set_permissions(&exe, std::fs::Permissions::from_mode(0o755)).unwrap();

        let cfg = Config { agent: "claude".into(), mode: "read".into(), cwd: dir.path().to_string_lossy().into(), prompt: "hi there".into(), session: None };
        let agent = Agent::spawn(Kind::Claude, cfg, &exe);

        let mut events = vec![];
        let deadline = Instant::now() + Duration::from_secs(10);
        while Instant::now() < deadline && !events.iter().any(|e: &Value| e["k"] == "done") {
            events.extend(agent.poll());
            thread::sleep(Duration::from_millis(20));
        }
        assert!(events.iter().any(|e| e["k"] == "done" && e["ok"] == true), "events: {events:?}");
        assert!(joined_text(&events).contains("hello"));
        assert_eq!(std::fs::read_to_string(dir.path().join("prompt.txt")).unwrap(), "hi there", "prompt travels over stdin");
    }

    #[cfg(unix)]
    #[test]
    fn stop_kills_a_running_agent() {
        use std::os::unix::fs::PermissionsExt;
        let dir = tempfile::tempdir().unwrap();
        let exe = dir.path().join("slow");
        std::fs::write(&exe, "#!/bin/sh\nsleep 30\n").unwrap();
        std::fs::set_permissions(&exe, std::fs::Permissions::from_mode(0o755)).unwrap();

        let cfg = Config { agent: "codex".into(), mode: "read".into(), cwd: dir.path().to_string_lossy().into(), ..Default::default() };
        let agent = Agent::spawn(Kind::Codex, cfg, &exe);
        thread::sleep(Duration::from_millis(100));
        agent.stop();

        let deadline = Instant::now() + Duration::from_secs(5);
        let mut done = None;
        while Instant::now() < deadline && done.is_none() {
            done = agent.poll().into_iter().find(|e| e["k"] == "done");
            thread::sleep(Duration::from_millis(20));
        }
        assert_eq!(done.expect("done after stop")["t"], "Stopped.");
    }

    #[test]
    fn missing_folder_and_missing_cli_report_through_done() {
        let a = Agent::failed("nope".into());
        let ev = a.poll();
        assert_eq!(ev[0]["k"], "done");
        assert_eq!(ev[0]["ok"], false);
        assert!(find_executable("definitely-not-a-real-binary-xyz").is_none());
    }
}
