using System.Net;
using System.Text;
using LuminaIDE;
using Xunit;

namespace LuminaIDE.Tests;

public class ImageClassificationTests
{
    [Theory]
    [InlineData("https://example.com/a.png", ImageKind.Remote)]
    [InlineData("http://localhost:5173/logo.png", ImageKind.Remote)]     // dev servers
    [InlineData("http://127.0.0.1:8080/logo.png", ImageKind.Remote)]
    [InlineData("//cdn.example.com/a.png", ImageKind.Remote)]            // protocol-relative means https
    [InlineData("http://example.com/a.png", ImageKind.Blocked)]          // insecure
    [InlineData("file:///etc/passwd", ImageKind.Blocked)]
    [InlineData("javascript:alert(1)", ImageKind.Blocked)]
    [InlineData("ftp://example.com/a.png", ImageKind.Blocked)]
    [InlineData("data:text/html,<script>alert(1)</script>", ImageKind.Blocked)]
    [InlineData("data:image/png;base64,AAAA", ImageKind.Data)]
    [InlineData("", ImageKind.Blocked)]
    [InlineData("   ", ImageKind.Blocked)]
    public void Only_safe_sources_are_loadable(string src, ImageKind expected) =>
        Assert.Equal(expected, MarkdownImages.Classify(src, "/docs", "/proj", allowRemote: true).Kind);

    [Fact]
    public void Turning_remote_images_off_blocks_web_images_but_not_local_or_data_ones()
    {
        var web = MarkdownImages.Classify("https://example.com/a.png", "/docs", "/proj", allowRemote: false);
        Assert.Equal(ImageKind.Blocked, web.Kind);
        Assert.Contains("Settings", web.Reason);   // the UI offers "click to load this one" for exactly this reason
        Assert.Equal(ImageKind.Local, MarkdownImages.Classify("a.png", "/docs", "/proj", allowRemote: false).Kind);
        Assert.Equal(ImageKind.Data, MarkdownImages.Classify("data:image/png;base64,AAAA", "/docs", "/proj", allowRemote: false).Kind);
    }

    [Fact]
    public void Relative_and_root_relative_paths_resolve_like_GitHub()
    {
        if (Platform.IsWindows) return; // paths below are POSIX
        Assert.Equal("/docs/img/a.png", MarkdownImages.Classify("img/a.png", "/docs", "/proj", true).Location);
        Assert.Equal("/docs/img/a.png", MarkdownImages.Classify("img/a.png?raw=true#frag", "/docs", "/proj", true).Location);
        Assert.Equal("/docs/my pic.png", MarkdownImages.Classify("my%20pic.png", "/docs", "/proj", true).Location);
        Assert.Equal("/docs/a.png", MarkdownImages.Classify("./a.png", "/docs", "/proj", true).Location);
        Assert.Equal("/proj/assets/a.png", MarkdownImages.Classify("/assets/a.png", "/docs", "/proj", true).Location); // "/" = the open folder
        Assert.Equal("/docs/assets/a.png", MarkdownImages.Classify("/assets/a.png", "/docs", null, true).Location);
        Assert.Equal(ImageKind.Blocked, MarkdownImages.Classify("a.png", null, null, true).Kind);
    }
}

public class ImageRootTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "lumina-root-" + Guid.NewGuid().ToString("N"));
    public ImageRootTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    [Fact]
    public void Root_relative_paths_fall_back_to_folders_above_the_markdown_file()
    {
        if (Platform.IsWindows) return;
        // The open folder is a parent that contains many projects, so "/assets/x.png" isn't under it...
        var r = MarkdownImages.Classify("/assets/x.png", "/work/proj/docs/guide", "/work", true);
        Assert.Equal("/work/assets/x.png", r.Location);
        // ...but it is under one of the folders above the README, and those are offered as alternatives, nearest first.
        Assert.Equal(["/work/proj/docs/guide/assets/x.png", "/work/proj/docs/assets/x.png", "/work/proj/assets/x.png"], r.Alternatives!.Take(3));
        Assert.DoesNotContain("/work/assets/x.png", r.Alternatives!);
        // Plain relative paths never search upward.
        Assert.Null(MarkdownImages.Classify("assets/x.png", "/work/proj/docs", "/work", true).Alternatives);
    }

    [Fact]
    public void The_project_root_is_the_nearest_git_repository_else_the_open_folder()
    {
        var repo = Path.Combine(_tmp, "outer", "repo");
        var deep = Path.Combine(repo, "docs", "deep");
        Directory.CreateDirectory(deep);
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        Assert.Equal(repo, MarkdownImages.FindProjectRoot(deep, _tmp));            // git root wins over the open folder
        Assert.Equal(_tmp, MarkdownImages.FindProjectRoot(Path.Combine(_tmp, "outer"), _tmp)); // no .git above: open folder
        Assert.Null(MarkdownImages.FindProjectRoot(null, null));

        var submodule = Path.Combine(_tmp, "sub");
        Directory.CreateDirectory(submodule);
        File.WriteAllText(Path.Combine(submodule, ".git"), "gitdir: ../x");        // submodules/worktrees use a .git *file*
        Assert.Equal(submodule, MarkdownImages.FindProjectRoot(submodule, _tmp));
    }
}

public class ImageBytesTests
{
    [Fact]
    public void Data_uris_decode_base64_and_percent_encoding_and_reject_garbage_or_huge_ones()
    {
        Assert.Equal("hello"u8.ToArray(), MarkdownImages.DecodeDataUri("data:image/png;base64,aGVsbG8="));
        Assert.Equal("<svg/>"u8.ToArray(), MarkdownImages.DecodeDataUri("data:image/svg+xml;utf8,%3Csvg%2F%3E"));
        Assert.Null(MarkdownImages.DecodeDataUri("data:image/png;base64,!!!not base64!!!"));
        Assert.Null(MarkdownImages.DecodeDataUri("data:image/png;base64"));                               // no payload
        Assert.Null(MarkdownImages.DecodeDataUri("data:image/png;base64," + new string('A', 400), maxBytes: 100));
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>", true)]
    [InlineData("<?xml version=\"1.0\"?>\n<!-- c -->\n<SVG width=\"1\"/>", true)]
    [InlineData("﻿  \n<svg/>", true)]
    [InlineData("<html><body>not an image</body></html>", false)]
    [InlineData("\u0089PNG\r\n", false)]
    [InlineData("", false)]
    public void Svg_sniffing(string text, bool isSvg) =>
        Assert.Equal(isSvg, MarkdownImages.LooksLikeSvg(Encoding.UTF8.GetBytes(text)));
}

/// <summary>Talks to a real HTTP server on localhost: limits, failures, caching and redirect safety.</summary>
public sealed class ImageFetchTests : IDisposable
{
    readonly HttpListener _server = new();
    readonly string _base;
    readonly Task _loop;
    int _hits;

    public ImageFetchTests()
    {
        int port;
        using (var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0)) { probe.Start(); port = ((IPEndPoint)probe.LocalEndpoint).Port; }
        _base = $"http://127.0.0.1:{port}";
        _server.Prefixes.Add(_base + "/");
        _server.Start();
        _loop = Task.Run(async () =>
        {
            while (_server.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _server.GetContextAsync(); } catch { return; }
                Interlocked.Increment(ref _hits);
                var res = ctx.Response;
                switch (ctx.Request.Url!.AbsolutePath)
                {
                    case "/ok.png": res.OutputStream.Write("PNGDATA"u8); break;
                    case "/big.bin": res.OutputStream.Write(new byte[5000]); break;
                    case "/missing": res.StatusCode = 404; break;
                    case "/to-local": res.StatusCode = 302; res.RedirectLocation = "/ok.png"; break;
                    case "/to-insecure": res.StatusCode = 302; res.RedirectLocation = "http://example.com/x.png"; break;
                    case "/loop": res.StatusCode = 302; res.RedirectLocation = "/loop"; break;
                }
                res.Close();
            }
        });
    }

    public void Dispose() { _server.Stop(); _server.Close(); }

    [Fact]
    public async Task Downloads_bytes_and_caches_so_re_rendering_does_not_re_download()
    {
        Assert.Equal("PNGDATA"u8.ToArray(), await MarkdownImages.FetchAsync(_base + "/ok.png"));
        int after = _hits;
        Assert.Equal("PNGDATA"u8.ToArray(), await MarkdownImages.FetchAsync(_base + "/ok.png"));
        Assert.Equal(after, _hits);
    }

    [Fact]
    public async Task Refuses_oversized_responses_and_errors()
    {
        Assert.Null(await MarkdownImages.FetchAsync(_base + "/big.bin", maxBytes: 1000));
        Assert.Equal(5000, (await MarkdownImages.FetchAsync(_base + "/big.bin", maxBytes: 10_000))!.Length);
        Assert.Null(await MarkdownImages.FetchAsync(_base + "/missing"));
        Assert.Null(await MarkdownImages.FetchAsync("http://127.0.0.1:1/nothing-listens-here")); // connection refused
    }

    [Fact]
    public async Task Follows_safe_redirects_but_never_downgrades_to_insecure_http_or_loops_forever()
    {
        Assert.Equal("PNGDATA"u8.ToArray(), await MarkdownImages.FetchAsync(_base + "/to-local"));
        int hits = _hits;
        Assert.Null(await MarkdownImages.FetchAsync(_base + "/to-insecure"));
        Assert.Equal(hits + 1, _hits); // only the first request was made; example.com was never contacted
        Assert.Null(await MarkdownImages.FetchAsync(_base + "/loop"));
    }

    [Fact]
    public async Task Non_web_schemes_are_never_fetched() =>
        Assert.Null(await MarkdownImages.FetchAsync("ftp://example.com/a.png"));
}
