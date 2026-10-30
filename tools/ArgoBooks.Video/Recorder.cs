using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform;

namespace ArgoBooks.Video;

/// <summary>
/// Records one take of the app in real time at a fixed frame rate, moving a pretend mouse and
/// logging where it was on every frame, so the edit can draw the cursor and play the clicks.
/// The take is encoded as it goes by an ffmpeg process that reads the raw frames.
/// </summary>
internal sealed class Recorder
{
    public const int Fps = 30;
    public const double Scale = 2;

    private readonly Window _window;
    private readonly string _videoPath;
    private readonly Stopwatch _clock = new();
    private readonly BlockingCollection<(byte[] Pixels, int Copies)> _queue = new(boundedCapacity: 6);
    private readonly ConcurrentBag<byte[]> _spare = [];
    private readonly JsonArray _frames = [];
    private readonly JsonArray _events = [];
    private readonly JsonObject _marks = [];
    private readonly JsonArray _lateAt = [];
    private readonly JsonArray _stalls = [];
    private TimeSpan _paused;
    private readonly List<double> _captureMs = [];
    private readonly Random _random = new(42);
    private Process? _ffmpeg;
    private Task? _writer;
    private int _written;
    private int _late;
    private Point _cursor;
    private bool _down;
    private string? _carrying;

    public Recorder(Window window, string videoPath, Point cursor)
    {
        _window = window;
        _videoPath = videoPath;
        _cursor = cursor;
        _window.MouseMove(cursor);
        // Windows wakes a waiting task only every 15 ms by default, which is too coarse to land each frame on time.
        TimeBeginPeriod(1);
    }

    /// <summary>The most frames a late capture is repeated for; a longer stall pauses the take's clock instead.</summary>
    private const int MaxRepeat = 2;

    private TimeSpan Elapsed => _clock.Elapsed - _paused;

    /// <summary>
    /// Draws frames for a while without keeping them, so the first frames' one-off costs land before the take starts.
    /// </summary>
    public async Task WarmUpAsync(double seconds)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            _spare.Add(Capture());
            await Task.Delay(1000 / Fps);
        }
    }

    /// <summary>Seconds into the take of the next frame.</summary>
    public double Now => _written / (double)Fps;

    public Point Cursor => _cursor;

    public void Mark(string name) => _marks[name] = Math.Round(Now, 3);

    public void Event(string kind) => _events.Add(new JsonObject { ["t"] = Math.Round(Now, 3), ["kind"] = kind });

    public async Task HoldAsync(double seconds)
    {
        var frames = (int)Math.Round(seconds * Fps);
        for (var i = 0; i < frames; i++)
            await TickAsync();
    }

    public async Task WaitForAsync(Func<bool> done, double timeoutSeconds, string what)
    {
        var limit = Now + timeoutSeconds;
        while (!done())
        {
            if (Now > limit)
                throw new TimeoutException($"Gave up after {timeoutSeconds} s waiting for {what}");
            await TickAsync();
        }
    }

    /// <summary>Glides the cursor to a point along a gentle arc, easing in and out like a hand does.</summary>
    public async Task MoveToAsync(Point target, double seconds, Action<Point>? onFrame = null)
    {
        var start = _cursor;
        var delta = target - start;
        var length = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
        var bend = new Point(-delta.Y, delta.X) * (length > 0 ? 0.08 : 0);
        var frames = Math.Max(1, (int)Math.Round(seconds * Fps));
        for (var i = 1; i <= frames; i++)
        {
            var t = (double)i / frames;
            var eased = t * t * t * (t * (t * 6 - 15) + 10);
            _cursor = start + delta * eased + bend * Math.Sin(Math.PI * eased);
            if (onFrame != null)
                onFrame(_cursor);
            else
                _window.MouseMove(_cursor);
            await TickAsync();
        }
    }

    public async Task ClickAsync()
    {
        Event("click");
        _window.MouseDown(_cursor, MouseButton.Left);
        _down = true;
        await HoldAsync(0.1);
        _window.MouseUp(_cursor, MouseButton.Left);
        _down = false;
        await TickAsync();
    }

    /// <summary>Looks and sounds like a click in the edit, without sending one to the app.</summary>
    public async Task PressAsync()
    {
        Event("click");
        _down = true;
        await HoldAsync(0.1);
        _down = false;
        await TickAsync();
    }

    public async Task TypeAsync(string text)
    {
        foreach (var c in text)
        {
            Event("key");
            _window.KeyTextInput(c.ToString());
            await HoldAsync(0.05 + _random.NextDouble() * 0.06);
        }
    }

    /// <summary>
    /// Drags a file in from off screen and drops it at the target, as if from the desktop. The edit
    /// draws the file's picture under the cursor while it is carried.
    /// </summary>
    public async Task DragFileAsync(string path, Point from, Point to, double seconds)
    {
        var item = new DataTransferItem();
        item.SetFile(await LocalFile.OpenAsync(_window, path));
        var data = new DataTransfer();
        data.Add(item);

        var inside = false;
        _cursor = from;
        _carrying = "file";
        await MoveToAsync(to, seconds, point =>
        {
            if (!new Rect(_window.ClientSize).Contains(point))
                return;
            _window.DragDrop(point, inside ? RawDragEventType.DragOver : RawDragEventType.DragEnter, data, DragDropEffects.Copy);
            inside = true;
        });
        Event("drop");
        _window.DragDrop(to, RawDragEventType.Drop, data, DragDropEffects.Copy);
        _carrying = null;
        await TickAsync();
    }

    /// <summary>
    /// Waits for the frame's moment, captures it and queues it for encoding. A capture that comes in
    /// late is repeated over the slots it missed, so the take keeps the app's own pace.
    /// </summary>
    public async Task TickAsync()
    {
        if (!_clock.IsRunning)
            _clock.Start();
        var due = TimeSpan.FromSeconds(_written / (double)Fps);
        while (Elapsed < due)
            await Task.Delay(1);

        var started = Elapsed.TotalMilliseconds;
        var pixels = Capture();
        _captureMs.Add(Elapsed.TotalMilliseconds - started);
        var copies = Math.Max(1, (int)(Elapsed.TotalSeconds * Fps) - _written + 1);
        if (copies > MaxRepeat + 1)
        {
            // A long stall is the app building a page at twice the size a screen shows it, so it is cut like a wait in an edit rather than shown as a frozen cursor.
            _stalls.Add(new JsonArray(Math.Round(Now, 2), copies - 1));
            _paused += TimeSpan.FromSeconds((copies - 1) / (double)Fps);
            copies = 1;
        }
        else if (copies > 1)
        {
            _late += copies - 1;
            _lateAt.Add(new JsonArray(Math.Round(Now, 2), copies - 1, Math.Round(_captureMs[^1])));
        }
        for (var i = 0; i < copies; i++)
        {
            _frames.Add(new JsonArray(
                Math.Round(_cursor.X * Scale, 1), Math.Round(_cursor.Y * Scale, 1), _down ? 1 : 0, _carrying));
        }
        _written += copies;
        _queue.Add((pixels, copies));
    }

    private byte[] Capture()
    {
        using var bitmap = _window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nothing was rendered");
        using var buffer = bitmap.Lock();
        var size = buffer.RowBytes * buffer.Size.Height;
        if (_ffmpeg == null)
            StartEncoder(buffer.Size, buffer.RowBytes, buffer.Format);
        if (!_spare.TryTake(out var pixels) || pixels.Length != size)
            pixels = new byte[size];
        Marshal.Copy(buffer.Address, pixels, 0, size);
        return pixels;
    }

    private void StartEncoder(PixelSize size, int rowBytes, PixelFormat format)
    {
        if (rowBytes != size.Width * 4)
            throw new InvalidOperationException($"Rows are {rowBytes} bytes, expected {size.Width * 4}");
        var pixelFormat = format == PixelFormat.Rgba8888 ? "rgba" : "bgra";
        _ffmpeg = Process.Start(new ProcessStartInfo(Tools.Ffmpeg)
        {
            ArgumentList =
            {
                "-hide_banner", "-loglevel", "error", "-y",
                "-f", "rawvideo", "-pix_fmt", pixelFormat, "-video_size", $"{size.Width}x{size.Height}",
                "-framerate", Fps.ToString(), "-i", "-",
                "-c:v", "libx264", "-preset", "ultrafast", "-crf", "10", "-pix_fmt", "yuv444p", _videoPath,
            },
            RedirectStandardInput = true,
            UseShellExecute = false,
        })!;
        var input = _ffmpeg.StandardInput.BaseStream;
        _writer = Task.Run(() =>
        {
            foreach (var (pixels, copies) in _queue.GetConsumingEnumerable())
            {
                for (var i = 0; i < copies; i++)
                    input.Write(pixels);
                _spare.Add(pixels);
            }
            input.Close();
        });
    }

    public async Task FinishAsync(JsonObject? extra = null)
    {
        _queue.CompleteAdding();
        if (_writer != null)
            await _writer;
        if (_ffmpeg != null)
        {
            await _ffmpeg.WaitForExitAsync();
            if (_ffmpeg.ExitCode != 0)
                throw new InvalidOperationException($"ffmpeg stopped with code {_ffmpeg.ExitCode}");
        }
        TimeEndPeriod(1);

        var log = new JsonObject
        {
            ["fps"] = Fps,
            ["scale"] = Scale,
            ["frames"] = _frames,
            ["events"] = _events,
            ["marks"] = _marks,
            ["late_frames"] = _late,
            ["late_at"] = _lateAt,
            ["stalls"] = _stalls,
            ["capture_ms"] = new JsonObject
            {
                ["median"] = Math.Round(_captureMs.Order().ElementAt(_captureMs.Count / 2)),
                ["p95"] = Math.Round(_captureMs.Order().ElementAt(_captureMs.Count * 95 / 100)),
                ["max"] = Math.Round(_captureMs.Max()),
            },
        };
        if (extra != null)
            foreach (var (key, value) in extra)
                log[key] = value?.DeepClone();
        await File.WriteAllTextAsync(Path.ChangeExtension(_videoPath, ".json"), log.ToJsonString());
        Console.WriteLine($"{Path.GetFileName(_videoPath)}: {_written} frames ({Now:0.0} s), {_late} repeated to keep time");
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint milliseconds);
}

internal static class Tools
{
    /// <summary>ffmpeg from the PATH, or where winget installs it.</summary>
    public static string Ffmpeg { get; } = Find();

    private static string Find()
    {
        var winget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(winget))
        {
            var found = Directory.GetDirectories(winget, "Gyan.FFmpeg*")
                .SelectMany(d => Directory.GetFiles(d, "ffmpeg.exe", SearchOption.AllDirectories))
                .FirstOrDefault();
            if (found != null)
                return found;
        }
        return "ffmpeg";
    }
}
