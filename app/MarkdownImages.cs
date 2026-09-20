using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Svg.Skia;
using Avalonia.Threading;

namespace LuminaIDE;

/// <summary>A decoded picture, plus its frames when it is an animated GIF (Image is then the first frame).</summary>
public sealed record LoadedImage(IImage Image, AnimatedGif? Animation = null);

/// <summary>The frames of a GIF as bitmaps, ready to show one after another.</summary>
public sealed class AnimatedGif
{
    public required WriteableBitmap[] Bitmaps { get; init; }
    public required int[] DelaysMs { get; init; }
    public required int RepeatCount { get; init; }

    /// <summary>Builds the bitmaps (safe off the UI thread).</summary>
    public static AnimatedGif From(GifFrames g)
    {
        var bitmaps = new WriteableBitmap[g.Count];
        for (int i = 0; i < g.Count; i++)
        {
            var bmp = new WriteableBitmap(new PixelSize(g.Width, g.Height), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
            using (var fb = bmp.Lock())
            {
                int rowBytes = g.Width * 4;
                if (fb.RowBytes == rowBytes) System.Runtime.InteropServices.Marshal.Copy(g.Bgra[i], 0, fb.Address, g.Bgra[i].Length);
                else for (int y = 0; y < g.Height; y++) System.Runtime.InteropServices.Marshal.Copy(g.Bgra[i], y * rowBytes, fb.Address + y * fb.RowBytes, rowBytes);
            }
            bitmaps[i] = bmp;
        }
        return new AnimatedGif { Bitmaps = bitmaps, DelaysMs = g.DelaysMs, RepeatCount = g.RepeatCount };
    }
}

/// <summary>
/// Plays one animated GIF on an Image. All playbacks share a single timer, and a playback only runs while its
/// image is on screen (the preview rebuilds its controls on every edit, which detaches the old ones).
/// </summary>
public sealed class GifPlayback
{
    static readonly List<GifPlayback> Active = [];
    static DispatcherTimer? _timer;
    static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

    readonly AnimatedGif _gif;
    readonly Image _image;
    int _index, _plays;
    double _nextAt;
    bool _finished;

    public bool Paused { get; set; }

    public GifPlayback(AnimatedGif gif, Image image)
    {
        _gif = gif; _image = image;
        image.Source = gif.Bitmaps[0];
        image.AttachedToVisualTree += (_, _) => Start();
        image.DetachedFromVisualTree += (_, _) => Stop();
    }

    void Start()
    {
        _index = 0; _plays = 0; _finished = false;
        _image.Source = _gif.Bitmaps[0];
        _nextAt = Clock.Elapsed.TotalMilliseconds + _gif.DelaysMs[0];
        if (!Active.Contains(this)) Active.Add(this);
        _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Tick());
        _timer.Start();
    }

    void Stop()
    {
        Active.Remove(this);
        if (Active.Count == 0) _timer?.Stop();
    }

    static void Tick()
    {
        double now = Clock.Elapsed.TotalMilliseconds;
        foreach (var p in Active.ToArray()) p.Advance(now);
    }

    void Advance(double now)
    {
        if (_finished) return;
        if (Paused) { _nextAt = now + _gif.DelaysMs[_index]; return; } // resume with a full delay for the current frame
        bool changed = false;
        while (now >= _nextAt && !_finished)
        {
            _index++;
            if (_index >= _gif.Bitmaps.Length)
            {
                _plays++;
                if (_gif.RepeatCount >= 0 && _plays > _gif.RepeatCount) { _index = _gif.Bitmaps.Length - 1; _finished = true; changed = true; break; }
                _index = 0;
            }
            _nextAt += _gif.DelaysMs[_index];
            changed = true;
        }
        if (now - _nextAt > 1000) _nextAt = now; // the app was busy or hidden for a while: don't fast-forward through the backlog
        if (changed) _image.Source = _gif.Bitmaps[_index];
    }
}

/// <summary>Where an image reference points, and whether we are willing to load it.</summary>
public enum ImageKind { Local, Remote, Data, Blocked }

/// <param name="Alternatives">Other places to look when a "/root-relative" path isn't under the first choice (see Classify).</param>
public sealed record ImageRef(ImageKind Kind, string Location, string? Reason = null, string[]? Alternatives = null);

/// <summary>
/// Turns image references from Markdown into pixels. Local files, data: URIs and https:// URLs are supported
/// (plus http:// to localhost, for dev servers). Everything remote is size-limited, time-limited and cached,
/// because the preview re-renders as you type.
/// </summary>
public static class MarkdownImages
{
    public const int MaxRemoteBytes = 15 * 1024 * 1024;
    public const int MaxSvgBytes = 2 * 1024 * 1024;
    static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(12);
    static readonly TimeSpan DecodeTimeout = TimeSpan.FromSeconds(6);

    static readonly HttpClient Http = CreateClient();
    static readonly ConcurrentDictionary<string, Task<byte[]?>> Fetches = new();
    static readonly ConcurrentDictionary<string, LoadedImage> Decoded = new();

    static HttpClient CreateClient()
    {
        // Redirects are followed by hand so a redirect can never downgrade https to http.
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = FetchTimeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"LuminaIDE/{AppInfo.Version}");
        client.DefaultRequestHeaders.Accept.ParseAdd("image/*,*/*;q=0.5");
        return client;
    }

    // ---------------------------------------------------------- classification --

    static readonly System.Text.RegularExpressions.Regex SchemePrefix = new(@"^[A-Za-z][A-Za-z0-9+.\-]+:", System.Text.RegularExpressions.RegexOptions.Compiled);

    static bool IsLoopback(Uri u) => u.IsLoopback || u.Host is "localhost";

    /// <summary>
    /// Decides what an image reference is. Pure (no I/O), so the rules are easy to test:
    /// https and data: are fine, http only to localhost, file: and everything else is blocked.
    /// </summary>
    public static ImageRef Classify(string src, string? baseDir, string? root, bool allowRemote)
    {
        src = src.Trim();
        if (src.Length == 0) return new(ImageKind.Blocked, "", "empty image address");

        if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
                ? new(ImageKind.Data, src)
                : new(ImageKind.Blocked, src, "only image data: addresses are allowed");

        // Protocol-relative ("//cdn.example.com/x.png") means https. Checked first: on Linux/macOS .NET would
        // otherwise parse it (and "/assets/x.png") as a file: URI.
        if (src.StartsWith("//")) return allowRemote ? new(ImageKind.Remote, "https:" + src) : new(ImageKind.Blocked, src, "remote images are turned off in Settings");

        // Only treat it as a URL when it really starts with "scheme:" (a single letter is a Windows drive, "C:\x").
        if (SchemePrefix.IsMatch(src) && Uri.TryCreate(src, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "https" || (uri.Scheme is "http" && IsLoopback(uri)))
                return allowRemote ? new(ImageKind.Remote, uri.AbsoluteUri) : new(ImageKind.Blocked, uri.AbsoluteUri, "remote images are turned off in Settings");
            if (uri.Scheme is "http") return new(ImageKind.Blocked, uri.AbsoluteUri, "insecure http:// images aren't loaded");
            return new(ImageKind.Blocked, src, $"{uri.Scheme}: images aren't loaded");
        }

        // Relative to the Markdown file; a leading "/" means relative to the open folder (the GitHub convention).
        var clean = Uri.UnescapeDataString(src.Split('#')[0].Split('?')[0]);
        bool rootRelative = clean.StartsWith('/');
        string? dir = rootRelative ? root ?? baseDir : baseDir;
        if (dir is null) return new(ImageKind.Blocked, src, "no folder to resolve this path against");
        var relative = clean.TrimStart('/', '\\');
        var primary = Path.GetFullPath(Path.Combine(dir, relative));
        if (!rootRelative || baseDir is null) return new(ImageKind.Local, primary);

        // "/assets/x.png" means "from the repository root". If that guess is wrong (a parent folder is open, or there's
        // no .git), also try every folder above the Markdown file, nearest first.
        var alternatives = Ancestors(baseDir).Select(a => Path.GetFullPath(Path.Combine(a, relative))).Where(p => p != primary).ToArray();
        return new(ImageKind.Local, primary, null, alternatives);
    }

    /// <summary>The folder itself, then each parent up to the filesystem root (at most 12).</summary>
    static IEnumerable<string> Ancestors(string dir)
    {
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            yield return dir;
            dir = Path.GetDirectoryName(dir) ?? "";
        }
    }

    /// <summary>
    /// The root that "/path" refers to: the nearest folder above the Markdown file that contains .git (the repository
    /// root, as on GitHub), else the folder that is open in the editor.
    /// </summary>
    public static string? FindProjectRoot(string? markdownDir, string? openFolder)
    {
        if (markdownDir is not null)
            foreach (var dir in Ancestors(markdownDir))
                if (Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git"))) return dir;
        return openFolder;
    }

    public static bool LooksLikeSvg(ReadOnlySpan<byte> bytes)
    {
        var head = Encoding.UTF8.GetString(bytes[..Math.Min(bytes.Length, 1024)]).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        return head.StartsWith("<") && (head.Contains("<svg", StringComparison.OrdinalIgnoreCase));
    }

    // ----------------------------------------------------------------- bytes --

    /// <summary>Raw bytes for a data: URI, or null if it is malformed or too big.</summary>
    public static byte[]? DecodeDataUri(string uri, int maxBytes = 5 * 1024 * 1024)
    {
        int comma = uri.IndexOf(',');
        if (comma < 0) return null;
        var header = uri[..comma];
        var payload = uri[(comma + 1)..];
        try
        {
            var bytes = header.Contains(";base64", StringComparison.OrdinalIgnoreCase)
                ? Convert.FromBase64String(payload)
                : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
            return bytes.Length <= maxBytes ? bytes : null;
        }
        catch (FormatException) { return null; }
    }

    /// <summary>Downloads an https (or localhost http) URL, following up to 5 redirects that stay on allowed schemes.</summary>
    public static Task<byte[]?> FetchAsync(string url, int maxBytes = MaxRemoteBytes) =>
        Fetches.GetOrAdd(url + "|" + maxBytes, _ => FetchCoreAsync(url, maxBytes));

    static async Task<byte[]?> FetchCoreAsync(string url, int maxBytes)
    {
        try
        {
            var current = new Uri(url);
            for (int hop = 0; hop < 6; hop++)
            {
                if (!(current.Scheme == "https" || (current.Scheme == "http" && IsLoopback(current)))) return null;
                using var response = await Http.GetAsync(current, HttpCompletionOption.ResponseHeadersRead);

                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } next)
                {
                    current = next.IsAbsoluteUri ? next : new Uri(current, next);
                    continue;
                }
                if (!response.IsSuccessStatusCode) return null;
                if (response.Content.Headers.ContentLength is { } len && len > maxBytes) return null;

                await using var stream = await response.Content.ReadAsStreamAsync();
                using var buffer = new MemoryStream();
                var chunk = new byte[16 * 1024];
                int n;
                while ((n = await stream.ReadAsync(chunk)) > 0)
                {
                    buffer.Write(chunk, 0, n);
                    if (buffer.Length > maxBytes) return null; // no Content-Length, but it keeps coming
                }
                return buffer.ToArray();
            }
            return null;
        }
        catch { return null; } // offline, DNS failure, timeout, TLS error...
    }

    // ---------------------------------------------------------------- decoding --

    /// <summary>
    /// Decodes PNG, JPEG, GIF (first frame), WebP, BMP or SVG off the UI thread. Returns a Bitmap or an SvgSource
    /// (an SvgImage is an Avalonia object and must be created on the UI thread, which ToImageAsync does).
    /// </summary>
    static object? DecodeRaw(byte[] bytes)
    {
        try
        {
            if (LooksLikeSvg(bytes))
            {
                if (bytes.Length > MaxSvgBytes) return null;
                using var ms = new MemoryStream(bytes);
                return SvgSource.LoadFromStream(ms);
            }
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch { return null; } // corrupt or unsupported: the caller shows a "couldn't load" note
    }

    static async Task<LoadedImage?> DecodeWithTimeoutAsync(byte[] bytes, bool animate)
    {
        // Animated GIFs: decode every frame (memory-capped). Anything else, or a single-frame / oversized GIF, is a still picture.
        if (animate && GifDecoder.IsGif(bytes))
        {
            var work = Task.Run(() => GifDecoder.Decode(bytes) is { } g ? AnimatedGif.From(g) : null);
            if (await Task.WhenAny(work, Task.Delay(DecodeTimeout * 3)) == work && await work is { } gif)
                return new LoadedImage(gif.Bitmaps[0], gif);
        }

        var still = Task.Run(() => DecodeRaw(bytes));
        if (await Task.WhenAny(still, Task.Delay(DecodeTimeout)) != still) return null;
        return await still switch
        {
            Bitmap bitmap => new LoadedImage(bitmap),
            SvgSource source => new LoadedImage(await Dispatcher.UIThread.InvokeAsync(() => (IImage)new SvgImage { Source = source })),
            _ => null,
        };
    }

    /// <summary>Resolves an image reference to a decoded image, using caches. Null when it can't be shown.</summary>
    public static async Task<LoadedImage?> LoadAsync(ImageRef r, bool animate = true)
    {
        string suffix = animate ? "|anim" : "";
        switch (r.Kind)
        {
            case ImageKind.Local:
            {
                var path = new[] { r.Location }.Concat(r.Alternatives ?? []).FirstOrDefault(File.Exists);
                if (path is null) return null;
                var info = new FileInfo(path);
                if (info.Length > MaxRemoteBytes) return null;
                var key = $"file:{path}|{info.LastWriteTimeUtc.Ticks}|{info.Length}{suffix}";
                if (Decoded.TryGetValue(key, out var hit)) return hit;
                return Remember(key, await DecodeWithTimeoutAsync(await File.ReadAllBytesAsync(path), animate));
            }
            case ImageKind.Data:
            {
                var key = r.Location + suffix;
                if (Decoded.TryGetValue(key, out var hit)) return hit;
                var bytes = DecodeDataUri(r.Location);
                return bytes is null ? null : Remember(key, await DecodeWithTimeoutAsync(bytes, animate));
            }
            case ImageKind.Remote:
            {
                var key = r.Location + suffix;
                if (Decoded.TryGetValue(key, out var hit)) return hit;
                var bytes = await FetchAsync(r.Location);
                return bytes is null ? null : Remember(key, await DecodeWithTimeoutAsync(bytes, animate));
            }
            default:
                return null;
        }
    }

    static LoadedImage? Remember(string key, LoadedImage? img)
    {
        if (img is null) return null;
        if (Decoded.Count > 300) Decoded.Clear();
        Decoded[key] = img;
        return img;
    }

    /// <summary>Forgets failed downloads so a later "click to retry" really retries.</summary>
    public static void ForgetFailedFetch(string url) { foreach (var k in Fetches.Keys.Where(k => k.StartsWith(url + "|"))) Fetches.TryRemove(k, out _); }
}

/// <summary>
/// What the preview shows for one image: a quiet placeholder immediately, the picture once it has loaded,
/// or a short note if it couldn't be shown. Loading never blocks the UI.
/// </summary>
public sealed class ImageHost : ContentControl
{
    readonly ImageRef _ref;
    readonly string _alt;
    readonly double? _width;
    readonly double? _height;
    readonly Action<string>? _open;
    readonly string? _href;
    readonly IBrush _muted;
    readonly bool _animate;

    public ImageHost(ImageRef reference, string alt, double? width, double? height, string? href, Action<string>? openLink, IBrush mutedBrush, bool animate = true)
    {
        _animate = animate;
        _ref = reference; _alt = alt; _width = width; _height = height; _href = href; _open = openLink; _muted = mutedBrush;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;

        if (reference.Kind == ImageKind.Blocked) Show(Note($"[{Label}]", why: reference.Reason, clickToLoad: reference.Reason?.Contains("Settings") == true));
        else
        {
            Show(Note($"[{Label}]", why: "Loading…"));
            _ = LoadAsync();
        }
    }

    string Label => string.IsNullOrWhiteSpace(_alt) ? "image" : _alt;

    void Show(Control c) => Content = c;

    Control Note(string text, string? why, bool clickToLoad = false)
    {
        var tb = new TextBlock { Text = text, Foreground = _muted, FontStyle = FontStyle.Italic, FontSize = 12.5 };
        if (why is not null) ToolTip.SetTip(tb, why + (clickToLoad ? " — click to load this one image" : ""));
        if (clickToLoad)
        {
            tb.Cursor = new Cursor(StandardCursorType.Hand);
            tb.Tapped += async (_, _) =>
            {
                Show(Note($"[{Label}]", why: "Loading…"));
                await LoadAsync(force: true);
            };
        }
        return tb;
    }

    async Task LoadAsync(bool force = false)
    {
        var target = force && _ref.Kind == ImageKind.Blocked ? _ref with { Kind = ImageKind.Remote } : _ref;
        LoadedImage? loaded = null;
        try { loaded = await MarkdownImages.LoadAsync(target, _animate); } catch { /* falls through to the failure note */ }

        Dispatcher.UIThread.Post(() =>
        {
            if (loaded is null)
            {
                if (target.Kind == ImageKind.Remote) MarkdownImages.ForgetFailedFetch(target.Location);
                Show(Note($"[{Label}]", why: "This image couldn't be loaded (offline, too large, or not a picture)."));
                return;
            }

            // Honour width= / height= from the Markdown/HTML (either alone keeps the aspect ratio), never exceed 760 px,
            // and let a Viewbox shrink it to fit a narrow pane instead of cropping it.
            var image = loaded.Image;
            var size = image.Size;
            double aspect = size.Width > 0 ? size.Height / size.Width : 1;
            double tw = size.Width, th = size.Height;
            if (_width is { } w && _height is { } h) { tw = w; th = h; }
            else if (_width is { } w2) { tw = w2; th = w2 * aspect; }
            else if (_height is { } h2 && aspect > 0) { th = h2; tw = h2 / aspect; }
            if (tw > 760) { th *= 760 / tw; tw = 760; }
            if (tw < 1 || th < 1) { tw = Math.Max(tw, 1); th = Math.Max(th, 1); }

            var img = new Image { Source = image, Width = tw, Height = th, Stretch = Stretch.Fill };
            var tip = _alt;
            if (loaded.Animation is { } gif)
            {
                var playback = new GifPlayback(gif, img);
                if (_href is null) // a linked GIF opens its link; an unlinked one can be paused and resumed
                {
                    tip = (string.IsNullOrEmpty(_alt) ? "Animated GIF" : _alt) + " — click to pause or play";
                    img.Cursor = new Cursor(StandardCursorType.Hand);
                    img.Tapped += (_, _) => playback.Paused = !playback.Paused;
                }
            }
            if (!string.IsNullOrEmpty(tip)) ToolTip.SetTip(img, tip);
            if (_href is not null && _open is not null)
            {
                img.Cursor = new Cursor(StandardCursorType.Hand);
                img.Tapped += (_, _) => _open(_href);
            }
            var fit = new Viewbox { Child = img, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxWidth = tw, HorizontalAlignment = HorizontalAlignment.Left };
            Show(fit);
        });
    }
}
