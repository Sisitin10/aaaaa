using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace RealCam.WindowsHub.Services;

internal sealed class BridgeServer(string pairingCode) : IAsyncDisposable
{
    private const int Port = 47821;
    private static readonly object GameProcessLock = new();
    private static Process? CachedGameProcess;
    private static DateTime NextProcessScanUtc = DateTime.MinValue;
    private readonly string _pairingCode = pairingCode;
    private readonly ConcurrentDictionary<Guid, PhonePeer> _peers = new();
    private readonly Fh6InputDriver _input = new();
    private readonly CancellationTokenSource _shutdown = new();
    private WebApplication? _application;
    private Task? _frameTask;

    public int ConnectedPhones => _peers.Count;

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://0.0.0.0:{Port}");
        var app = builder.Build();
        app.UseWebSockets();
        app.MapGet("/health", () => Results.Text("RealCam FH6 Hub ready"));
        app.Map("/control", HandlePhoneAsync);
        await app.StartAsync();
        _application = app;
        _frameTask = StreamFramesAsync(_shutdown.Token);
    }

    private async Task HandlePhoneAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        if (!string.Equals(context.Request.Query["code"], _pairingCode, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var id = Guid.NewGuid();
        var peer = new PhonePeer(socket);
        _peers[id] = peer;
        try
        {
            await peer.SendTextAsync("ready", _shutdown.Token);
            var buffer = new byte[4096];
            while (socket.State == WebSocketState.Open && !_shutdown.IsCancellationRequested)
            {
                using var stream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), _shutdown.Token);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    if (result.MessageType == WebSocketMessageType.Text) stream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Text)
                    HandleCommand(Encoding.UTF8.GetString(stream.ToArray()));
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            _peers.TryRemove(id, out _);
            _input.ReleaseKeys();
        }
    }

    private void HandleCommand(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeValue) ? typeValue.GetString() : null;
            if (type == "pose")
            {
                _input.ApplyPose(ReadFloat(root, "x"), ReadFloat(root, "y"), ReadFloat(root, "z"), ReadFloat(root, "yaw"), ReadFloat(root, "pitch"));
            }
            else if (type == "zoom")
            {
                _input.SetZoom(root.TryGetProperty("value", out var value) ? Math.Clamp(value.GetDouble(), 0, 1) : 0.5);
            }
            else if (type == "action" && root.TryGetProperty("action", out var action))
            {
                _input.SendAction(action.GetString() ?? "");
            }
        }
        catch (JsonException) { }
        catch (InvalidOperationException) { }
        catch (FormatException) { }
    }

    private async Task StreamFramesAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / 30.0));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (_peers.IsEmpty) continue;
                var frame = CaptureGameWindow();
                if (frame is null) continue;
                await Task.WhenAll(_peers.Values.Select(peer => peer.SendFrameAsync(frame, cancellationToken)));
            }
        }
        catch (OperationCanceledException) { }
    }

    private static byte[]? CaptureGameWindow()
    {
        var process = FindGameProcess();
        if (process is null) return null;
        try
        {
            var handle = process.MainWindowHandle;
            if (handle == IntPtr.Zero || !NativeMethods.GetWindowRect(handle, out var bounds)) return null;
            var width = bounds.Right - bounds.Left;
            var height = bounds.Bottom - bounds.Top;
            if (width < 320 || height < 200) return null;
            using var source = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(source))
            {
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
            }

            const int targetWidth = 1280;
            var targetHeight = Math.Max(1, (int)Math.Round(height * (targetWidth / (double)width)));
            using var resized = new Bitmap(targetWidth, targetHeight, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(resized))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                graphics.DrawImage(source, 0, 0, targetWidth, targetHeight);
            }
            using var output = new MemoryStream();
            var encoder = ImageCodecInfo.GetImageEncoders().First(codec => codec.MimeType == "image/jpeg");
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 64L);
            resized.Save(output, encoder, parameters);
            return output.ToArray();
        }
        catch (ArgumentException) { return null; }
        catch (ExternalException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    public static string? FindGame()
    {
        var process = FindGameProcess();
        if (process is null) return null;
        try { return process.MainWindowTitle.Length > 0 ? process.MainWindowTitle : "Forza Horizon 6"; }
        catch { return "Forza Horizon 6"; }
    }

    private static Process? FindGameProcess()
    {
        lock (GameProcessLock)
        {
            if (CachedGameProcess is not null)
            {
                try
                {
                    if (!CachedGameProcess.HasExited &&
                        IsForzaProcess(CachedGameProcess.ProcessName) &&
                        CachedGameProcess.MainWindowHandle != IntPtr.Zero) return CachedGameProcess;
                }
                catch { }
                CachedGameProcess.Dispose();
                CachedGameProcess = null;
            }

            if (DateTime.UtcNow < NextProcessScanUtc) return null;
            NextProcessScanUtc = DateTime.UtcNow.AddSeconds(1);
            try
            {
                foreach (var candidate in Process.GetProcesses())
                {
                    try
                    {
                        if (IsForzaProcess(candidate.ProcessName) && candidate.MainWindowHandle != IntPtr.Zero)
                        {
                            CachedGameProcess = candidate;
                            return candidate;
                        }
                    }
                    catch { }
                    candidate.Dispose();
                }
            }
            catch { }
            return null;
        }
    }

    internal static bool IsForzaProcess(string name) =>
        name.Contains("ForzaHorizon6", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Forza Horizon 6", StringComparison.OrdinalIgnoreCase);

    private static float ReadFloat(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetSingle(out var number) ? Math.Clamp(number, -10f, 10f) : 0f;

    public async Task StopAsync()
    {
        _shutdown.Cancel();
        _input.ReleaseKeys();
        foreach (var peer in _peers.Values) await peer.CloseAsync();
        if (_frameTask is not null)
        {
            try { await _frameTask; } catch (OperationCanceledException) { }
        }
        if (_application is not null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { await _application.StopAsync(timeout.Token); } catch (OperationCanceledException) { }
            await _application.DisposeAsync();
            _application = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _shutdown.Dispose();
    }

    private sealed class PhonePeer(WebSocket socket)
    {
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public Task SendTextAsync(string text, CancellationToken token) =>
            SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, token);

        public Task SendFrameAsync(byte[] frame, CancellationToken token) =>
            SendAsync(frame, WebSocketMessageType.Binary, token);

        private async Task SendAsync(byte[] bytes, WebSocketMessageType messageType, CancellationToken token)
        {
            if (socket.State != WebSocketState.Open) return;
            await _sendLock.WaitAsync(token);
            try
            {
                if (socket.State == WebSocketState.Open)
                    await socket.SendAsync(bytes, messageType, true, token);
            }
            catch (WebSocketException) { }
            finally { _sendLock.Release(); }
        }

        public async Task CloseAsync()
        {
            if (socket.State != WebSocketState.Open) return;
            try { await socket.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "Hub closing", CancellationToken.None); }
            catch (WebSocketException) { }
        }
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        internal struct Rect { public int Left, Top, Right, Bottom; }
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        internal static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
    }
}
