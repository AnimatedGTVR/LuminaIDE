// Shell session for the LuminaIDE terminal panel.
//
//   Linux / macOS : a real pseudo-terminal (forkpty) running $SHELL.
//   Windows       : cmd.exe on plain pipes (no ConPTY). The panel is line-based, so this is enough for
//                   commands and scripts; it does not echo input, the UI echoes it instead.
//
// All calls are non-blocking so the UI thread can poll from a timer.

#ifdef _WIN32
// ------------------------------------------------------------------ Windows --
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#define LT_API extern "C" __declspec(dllexport)

namespace {
struct Term {
    HANDLE              in_write;  // our end of the child's stdin
    HANDLE              out_read;  // our end of the child's stdout+stderr
    PROCESS_INFORMATION pi;
};
} // namespace

// Starts cmd.exe in `cwd` (UTF-8). rows/cols are ignored: pipes have no size.
LT_API void *lt_open(int, int, const char *cwd) {
    SECURITY_ATTRIBUTES sa{};
    sa.nLength        = sizeof sa;
    sa.bInheritHandle = TRUE;

    HANDLE in_r = nullptr, in_w = nullptr, out_r = nullptr, out_w = nullptr;
    if (!CreatePipe(&in_r, &in_w, &sa, 0)) return nullptr;
    if (!CreatePipe(&out_r, &out_w, &sa, 0)) { CloseHandle(in_r); CloseHandle(in_w); return nullptr; }
    // The child must not inherit our ends of the pipes.
    SetHandleInformation(in_w, HANDLE_FLAG_INHERIT, 0);
    SetHandleInformation(out_r, HANDLE_FLAG_INHERIT, 0);

    wchar_t wcwd[32768] = L"";
    if (cwd && *cwd) MultiByteToWideChar(CP_UTF8, 0, cwd, -1, wcwd, static_cast<int>(sizeof wcwd / sizeof wcwd[0]));

    STARTUPINFOW si{};
    si.cb          = sizeof si;
    si.dwFlags     = STARTF_USESTDHANDLES | STARTF_USESHOWWINDOW;
    si.wShowWindow = SW_HIDE;
    si.hStdInput   = in_r;
    si.hStdOutput  = out_w;
    si.hStdError   = out_w;

    // /Q: echo off (no prompt noise). chcp 65001: UTF-8 output.
    wchar_t cmdline[] = L"cmd.exe /Q /K chcp 65001>nul";
    PROCESS_INFORMATION pi{};
    BOOL ok = CreateProcessW(nullptr, cmdline, nullptr, nullptr, TRUE, CREATE_NO_WINDOW, nullptr,
                             wcwd[0] ? wcwd : nullptr, &si, &pi);
    CloseHandle(in_r);
    CloseHandle(out_w);
    if (!ok) { CloseHandle(in_w); CloseHandle(out_r); return nullptr; }
    CloseHandle(pi.hThread);
    return new Term{in_w, out_r, pi};
}

// >0 bytes read, 0 nothing available right now, -1 session ended.
LT_API int lt_read(void *h, char *buf, int cap) {
    if (!h || !buf || cap <= 0) return -1;
    auto *t = static_cast<Term *>(h);
    DWORD avail = 0;
    if (!PeekNamedPipe(t->out_read, nullptr, 0, nullptr, &avail, nullptr)) return -1; // pipe closed: child is gone
    if (avail == 0) {
        DWORD code = 0;
        GetExitCodeProcess(t->pi.hProcess, &code);
        return code == STILL_ACTIVE ? 0 : -1;
    }
    DWORD n = 0;
    DWORD want = avail < static_cast<DWORD>(cap) ? avail : static_cast<DWORD>(cap);
    if (!ReadFile(t->out_read, buf, want, &n, nullptr)) return -1;
    return static_cast<int>(n);
}

LT_API int lt_write(void *h, const char *data, int len) {
    if (!h || !data || len < 0) return -1;
    auto *t = static_cast<Term *>(h);
    int done = 0;
    while (done < len) {
        DWORD n = 0;
        if (!WriteFile(t->in_write, data + done, static_cast<DWORD>(len - done), &n, nullptr)) return -1;
        done += static_cast<int>(n);
    }
    return done;
}

LT_API int lt_resize(void *, int, int) { return 0; }

LT_API void lt_close(void *h) {
    if (!h) return;
    auto *t = static_cast<Term *>(h);
    TerminateProcess(t->pi.hProcess, 0);
    WaitForSingleObject(t->pi.hProcess, 500);
    CloseHandle(t->pi.hProcess);
    CloseHandle(t->in_write);
    CloseHandle(t->out_read);
    delete t;
}

#else
// ------------------------------------------------------------ Linux / macOS --
#include <cerrno>
#include <cstdio>
#include <csignal>
#include <cstdlib>
#include <fcntl.h>
#include <sys/ioctl.h>
#include <sys/wait.h>
#include <unistd.h>

#ifdef __APPLE__
#include <util.h>
#else
#include <pty.h>
#endif

#define LT_API extern "C" __attribute__((visibility("default")))

namespace {
struct Term {
    int   fd;
    pid_t pid;
};
} // namespace

// Starts $SHELL in `cwd` on a new PTY. Returns an opaque handle, or null.
LT_API void *lt_open(int rows, int cols, const char *cwd) {
    winsize ws{};
    ws.ws_row = static_cast<unsigned short>(rows);
    ws.ws_col = static_cast<unsigned short>(cols);

    int   fd  = -1;
    pid_t pid = forkpty(&fd, nullptr, nullptr, &ws);
    if (pid < 0) return nullptr;

    if (pid == 0) {
        if (cwd && *cwd && chdir(cwd) != 0) { /* fall back to inherited cwd */ }
        // No terminal emulator on the UI side yet, so ask programs not to
        // emit cursor movement / colour sequences.
        setenv("TERM", "dumb", 1);
        // With TERM=dumb readline ignores the PTY size and assumes ~80 columns, then scrolls long
        // lines with a "<" marker. COLUMNS/LINES tell it the real size.
        char num[16];
        snprintf(num, sizeof num, "%d", cols);
        setenv("COLUMNS", num, 1);
        snprintf(num, sizeof num, "%d", rows);
        setenv("LINES", num, 1);
        const char *sh = getenv("SHELL");
        if (!sh || !*sh) sh = "/bin/sh";
        execlp(sh, sh, static_cast<char *>(nullptr));
        _exit(127);
    }

    fcntl(fd, F_SETFL, fcntl(fd, F_GETFL) | O_NONBLOCK);
    return new Term{fd, pid};
}

// >0 bytes read, 0 nothing available right now, -1 session ended.
LT_API int lt_read(void *h, char *buf, int cap) {
    if (!h || !buf || cap <= 0) return -1;
    ssize_t n = read(static_cast<Term *>(h)->fd, buf, static_cast<size_t>(cap));
    if (n > 0) return static_cast<int>(n);
    if (n < 0 && (errno == EAGAIN || errno == EWOULDBLOCK || errno == EINTR)) return 0;
    return -1;
}

// Returns bytes written, or -1 on failure.
LT_API int lt_write(void *h, const char *data, int len) {
    if (!h || !data || len < 0) return -1;
    int fd = static_cast<Term *>(h)->fd;
    int done = 0;
    while (done < len) {
        ssize_t n = write(fd, data + done, static_cast<size_t>(len - done));
        if (n > 0) { done += static_cast<int>(n); continue; }
        if (n < 0 && (errno == EAGAIN || errno == EINTR)) continue; // PTY buffer momentarily full
        return -1;
    }
    return done;
}

LT_API int lt_resize(void *h, int rows, int cols) {
    if (!h) return -1;
    winsize ws{};
    ws.ws_row = static_cast<unsigned short>(rows);
    ws.ws_col = static_cast<unsigned short>(cols);
    return ioctl(static_cast<Term *>(h)->fd, TIOCSWINSZ, &ws);
}

// Kills the shell, reaps it and frees the handle.
LT_API void lt_close(void *h) {
    if (!h) return;
    auto *t = static_cast<Term *>(h);
    close(t->fd);
    kill(t->pid, SIGKILL);
    waitpid(t->pid, nullptr, 0);
    delete t;
}
#endif
