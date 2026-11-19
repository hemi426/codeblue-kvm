using System.Diagnostics;
using LibVLCSharp.Shared;

namespace CodeBlue.Viewer.Video;

/// <summary>
/// One run of the capture: its own player, and its own window to draw in.
///
/// libVLC 3 can hang for good stopping a capture whose device has stalled (some
/// dongles stop delivering frames when the target's display sleeps), and its
/// other player calls then wait behind the stuck stop. So each session gets a
/// fresh player, stops on a thread of its own, and if the stop hangs, is simply
/// abandoned: its window hidden, the player left to finish if it ever does, and
/// the next session started with new ones. The UI thread never waits on a stop.
/// </summary>
internal sealed class VideoSession
{
    private const int NoFramesMs = 3000; // libVLC publishes its counts about once a second

    private readonly Media _media;
    private readonly object _lock = new(); // keeps statistics and snapshots away from the release
    private volatile bool _stopping;
    private bool _released;
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Frame rate, from the timer thread only.
    private int _lastFrames = -1;
    private long _lastFramesMs;
    private double _fps;

    public MediaPlayer Player { get; }

    /// <summary>The window libVLC draws in; hidden once the session stops.</summary>
    public Panel Surface { get; }

    /// <summary>Completes once the player has stopped and been released (which may be never).</summary>
    public Task Stopped => _stopped.Task;

    public VideoSession(LibVLC libVlc, Control host, string[] options)
    {
        Surface = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
        host.Controls.Add(Surface);
        Surface.BringToFront(); // over any abandoned session's (hidden) window
        Player = new MediaPlayer(libVlc)
        {
            // Input belongs to us, not to VLC's own hotkeys and mouse handling.
            EnableKeyInput = false,
            EnableMouseInput = false,
            Hwnd = Surface.Handle,
        };
        _media = new Media(libVlc, "dshow://", FromType.FromLocation, options);
    }

    public bool Play() => Player.Play(_media);

    /// <summary>
    /// Starts stopping, on a thread of its own, and returns at once. When the stop
    /// finishes, the player is released and <paramref name="onStopped"/> runs (on that
    /// thread) with how long it took.
    /// </summary>
    public void BeginStop(Action<long> onStopped)
    {
        lock (_lock)
        {
            if (_stopping) return;
            _stopping = true;
        }
        Surface.Visible = false;
        var thread = new Thread(() =>
        {
            var clock = Stopwatch.StartNew();
            try
            {
                Player.Stop();
                lock (_lock)
                {
                    _released = true;
                    Player.Dispose();
                    _media.Dispose();
                }
            }
            finally
            {
                _stopped.TrySetResult();
                onStopped(clock.ElapsedMilliseconds);
            }
        })
        {
            IsBackground = true, // a stop that never returns mustn't keep the viewer alive
            Name = "libVLC stop",
        };
        thread.Start();
    }

    /// <summary>
    /// Frames shown per second, for a timer thread to call a few times a second.
    /// 0 when no frame has arrived for a few seconds; null before there's anything
    /// to go on, or once the session is stopping.
    /// </summary>
    public double? SampleFrameRate()
    {
        if (_stopping) return null;
        int frames;
        // A snapshot holds the lock while it waits for a frame; skip a sample rather than wait.
        if (!Monitor.TryEnter(_lock)) return _lastFrames < 0 ? null : _fps;
        try
        {
            if (_stopping) return null;
            frames = _media.Statistics.DisplayedPictures;
        }
        finally
        {
            Monitor.Exit(_lock);
        }
        long now = Environment.TickCount64;
        if (_lastFrames < 0 || frames < _lastFrames)
        {
            (_lastFrames, _lastFramesMs) = (frames, now);
            return null;
        }
        if (frames > _lastFrames)
        {
            _fps = (frames - _lastFrames) * 1000.0 / Math.Max(1, now - _lastFramesMs);
            (_lastFrames, _lastFramesMs) = (frames, now);
        }
        else if (now - _lastFramesMs > NoFramesMs)
        {
            _fps = 0;
        }
        return _fps;
    }

    /// <summary>Saves the current frame; call off the UI thread (it waits for a frame). False if it couldn't.</summary>
    public bool TakeSnapshot(string path)
    {
        lock (_lock)
            return !_released && Player.TakeSnapshot(0, path, 0, 0);
    }
}
