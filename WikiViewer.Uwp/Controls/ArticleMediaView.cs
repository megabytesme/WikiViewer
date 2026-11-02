using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace WikiViewer.Uwp.Controls
{
    /// <summary>One native transport bar, with bounded preparation and cached-source recovery.</summary>
    internal sealed class ArticleMediaView : UserControl, IDisposable
    {
        private readonly List<NativeMediaSource> _sources;
        private readonly Func<string, CancellationToken, Task<string>> _resolve;
        private readonly CancellationToken _articleToken;
        private readonly MediaPlayerElement _display;
        private readonly bool _video;
        private readonly Button _playPause;
        private readonly Slider _seek;
        private readonly TextBlock _time;
        private readonly DispatcherTimer _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        private bool _updatingPosition;
        private bool _audioMuted;
        private readonly FrameworkElement _poster;
        private readonly ComboBox _choices;
        private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly ProgressBar _progress = new ProgressBar { Height = 4, IsIndeterminate = true };
        private readonly Button _retry = new Button { Content = "Retry media", Visibility = Visibility.Collapsed };
        private readonly HyperlinkButton _open = new HyperlinkButton { Content = "Open media file", Visibility = Visibility.Collapsed };
        private readonly DispatcherTimer _watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private CancellationTokenSource _operation;
        private MediaPlayer _player;
        private MediaSource _source;
        private DateTime? _bufferingSince;
        private bool _attached, _disposed, _ready, _wasPlaying;
        private int _request;
        private readonly double _expectedDuration;
        private Uri _traceUri;

        public ArticleMediaView(NativeArticleNode node, Func<string, CancellationToken, Task<string>> resolve,
            CancellationToken articleToken, FrameworkElement poster = null)
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            _sources = NativeMediaService.PlaybackSources(node.MediaSources);
            string duration; double expected;
            if (node.Attributes.TryGetValue("data-durationhint", out duration) && double.TryParse(duration, NumberStyles.Float, CultureInfo.InvariantCulture, out expected)) _expectedDuration = expected;
            _resolve = resolve; _articleToken = articleToken; _poster = poster;
            _video = node.TagName == "video";
            if (_video)
            {
                _display = new MediaPlayerElement
                {
                    AutoPlay = false, AreTransportControlsEnabled = false, IsHitTestVisible = false,
                    MinHeight = 0,
                    TransportControls = new MediaTransportControls { IsCompact = false, IsSeekBarVisible = true }
                };
                Windows.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_display, "ArticleMediaPlayer");
                Windows.UI.Xaml.Automation.AutomationProperties.SetName(_display, "Video player");
            }
            Windows.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_status, "ArticleMediaStatus");
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(_progress, "Loading media");
            _choices = new ComboBox { ItemsSource = _sources, DisplayMemberPath = "Label", SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch };
            var panel = new StackPanel();
            if (_display != null)
            {
                var viewport = new ArticleVideoViewport();
                string widthValue, heightValue; double width, height;
                if (node.Attributes.TryGetValue("width", out widthValue) && node.Attributes.TryGetValue("height", out heightValue) &&
                    double.TryParse(widthValue, NumberStyles.Float, CultureInfo.InvariantCulture, out width) &&
                    double.TryParse(heightValue, NumberStyles.Float, CultureInfo.InvariantCulture, out height) && width > 0 && height > 0)
                    viewport.AspectRatio = Math.Max(0.25, Math.Min(4, width / height));
                viewport.Children.Add(_display);
                if (_poster != null) viewport.Children.Add(_poster);
                panel.Children.Add(viewport);
            }
            if (_sources.Count > 1) panel.Children.Add(_choices);
            if (_display == null)
            {
                var bar = new Grid { MinHeight = 48, HorizontalAlignment = HorizontalAlignment.Stretch };
                bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                _playPause = new Button { Content = new SymbolIcon(Symbol.Play), Padding = new Thickness(4), MinWidth = 32, VerticalAlignment = VerticalAlignment.Center };
                _seek = new Slider { Minimum = 0, Maximum = 1, IsEnabled = false, MinWidth = 0, Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
                _time = new TextBlock { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, Text = "0:00 / " + Duration(_expectedDuration, true) };
                var mute = new Button { Content = new SymbolIcon(Symbol.Volume), MinWidth = 28, Padding = new Thickness(2), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(mute, 2);
                Grid.SetColumn(_seek, 1); Grid.SetRow(_time, 1); Grid.SetColumnSpan(_time, 3);
                bar.Children.Add(_playPause); bar.Children.Add(_seek); bar.Children.Add(_time); bar.Children.Add(mute); panel.Children.Add(bar);
                Windows.UI.Xaml.Automation.AutomationProperties.SetAutomationId(bar, "ArticleAudioPlayer");
                Windows.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_playPause, "ArticleMediaPlayPause");
                Windows.UI.Xaml.Automation.AutomationProperties.SetName(_playPause, "Play");
                Windows.UI.Xaml.Automation.AutomationProperties.SetName(_seek, "Audio position");
                Windows.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_time, "ArticleMediaTime");
                Windows.UI.Xaml.Automation.AutomationProperties.SetAutomationId(mute, "ArticleMediaMute");
                Windows.UI.Xaml.Automation.AutomationProperties.SetName(mute, "Mute");
                mute.Click += (s, e) =>
                {
                    _audioMuted = !_audioMuted;
                    if (_player != null) _player.IsMuted = _audioMuted;
                    mute.Content = new SymbolIcon(_audioMuted ? Symbol.Mute : Symbol.Volume);
                    Windows.UI.Xaml.Automation.AutomationProperties.SetName(mute, _audioMuted ? "Unmute" : "Mute");
                };
                _playPause.Click += async (s, e) =>
                {
                    if (!_ready) { _wasPlaying = true; await PrepareAsync(true); }
                    else if (_player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) _player.Pause();
                    else _player.Play();
                };
                _seek.ValueChanged += (s, e) =>
                {
                    if (!_updatingPosition && _ready && _player.PlaybackSession.CanSeek)
                        _player.PlaybackSession.Position = TimeSpan.FromSeconds(e.NewValue);
                };
                _positionTimer.Tick += (s, e) => UpdateAudioBar();
            }
            panel.Children.Add(_progress); panel.Children.Add(_status);
            panel.Children.Add(_retry); panel.Children.Add(_open); Content = panel;
            _choices.SelectionChanged += (s, e) => { if (_attached) _ = PrepareAsync(!_video); };
            _retry.Click += (s, e) => { _ = PrepareAsync(true); };
            Loaded += (s, e) =>
            {
                if (_disposed || _attached) return;
                _attached = true;
                if (_playPause != null) _playPause.IsEnabled = true;
                if (_video) _ = PrepareAsync(false);
                else { _progress.Visibility = Visibility.Collapsed; _ = PrepareSavedAudioAsync(); }
            };
            // RichTextBlock overflow reflows can unload an inline control while it
            // remains visible in the article. Its render scope owns its lifetime:
            // recycling the section or leaving the article calls Dispose instead.
            _watchdog.Tick += (s, e) =>
            {
                if (_bufferingSince.HasValue && DateTime.UtcNow - _bufferingSince.Value > TimeSpan.FromSeconds(30))
                    Fail("Media buffering timed out. Retry or open the media file.");
            };
        }

        private bool Current(int request) => !_disposed && _attached && !_articleToken.IsCancellationRequested && request == _request;

        private async Task PrepareSavedAudioAsync()
        {
            int request = _request;
            var selected = _choices.SelectedItem as NativeMediaSource;
            if (selected == null) return;
            try
            {
                var cached = await MediaCacheService.TryGetCachedUriAsync(selected.Uri.AbsoluteUri, _articleToken);
                if (cached != null && Current(request)) await PrepareAsync(true);
            }
            catch (Exception) { }
        }

        private async Task PrepareAsync(bool cachedFirst)
        {
            bool resume = _wasPlaying;
            Stop();
            if (_disposed || !_attached || _articleToken.IsCancellationRequested) return;
            int request = _request;
            _operation = CancellationTokenSource.CreateLinkedTokenSource(_articleToken);
            _operation.CancelAfter(TimeSpan.FromSeconds(75));
            var token = _operation.Token;
            _retry.Visibility = _open.Visibility = Visibility.Collapsed;
            if (_playPause != null) _playPause.IsEnabled = false;
            _progress.Visibility = Visibility.Visible;
            var selected = _choices.SelectedItem as NativeMediaSource;
            int first = Math.Max(0, _sources.IndexOf(selected));
            try
            {
                for (int offset = 0; offset < _sources.Count; offset++)
                {
                    var candidate = _sources[(first + offset) % _sources.Count];
                    _open.NavigateUri = candidate.Uri;
                    _status.Text = "Preparing media…";
                    string cached = null;
                    try { cached = await MediaCacheService.TryGetCachedUriAsync(candidate.Uri.AbsoluteUri, token); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Saved media: " + ex.Message); }
                    if (!Current(request)) return;
                    Uri saved;
                    if (Uri.TryCreate(cached, UriKind.Absolute, out saved) && await OpenAsync(saved, request, token))
                    { Ready(resume); return; }
                    if (!cachedFirst && await OpenAsync(candidate.Uri, request, token))
                    { Ready(resume); return; }
                    if (!Current(request)) return;
                    // A full video can be hundreds of megabytes. Playback tries
                    // streaming alternatives; explicit article downloads own bulk caching.
                    if (_resolve != null && !_video)
                    {
                        _status.Text = "Trying a downloaded copy…";
                        string local = null;
                        try
                        {
                            local = await NetworkOperationTimeout.RunAsync(() => _resolve(candidate.Uri.AbsoluteUri, token),
                                TimeSpan.FromSeconds(35), "Media download timed out.");
                        }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Media recovery: " + ex.Message); }
                        if (!Current(request)) return;
                        Uri uri;
                        if (Uri.TryCreate(local, UriKind.Absolute, out uri) && uri != candidate.Uri && uri.AbsoluteUri != cached && await OpenAsync(uri, request, token))
                        { Ready(resume); return; }
                    }
                    if (cachedFirst && await OpenAsync(candidate.Uri, request, token))
                    { Ready(resume); return; }
                    if (!Current(request)) return;
                }
                if (Current(request)) Fail("Windows could not open these media sources. Retry, open the media file, or use Web view.");
            }
            catch (OperationCanceledException)
            {
                if (Current(request)) Fail("Media preparation timed out. Retry, choose another source, or open the media file.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Native media: " + ex);
                if (Current(request)) Fail("Media could not load. Retry or open the media file.");
            }
        }

        private async Task<bool> OpenAsync(Uri uri, int request, CancellationToken token)
        {
            ClosePlayer();
            token.ThrowIfCancellationRequested();
            var player = new MediaPlayer { AutoPlay = false, IsMuted = _audioMuted };
            _player = player;
            _traceUri = uri;
            var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            player.MediaOpened += (s, e) =>
            {
                Trace("opened", uri, s);
                // Some playable streams report their duration only after playback starts.
                opened.TrySetResult(true);
            };
            player.MediaFailed += (s, e) =>
            {
                Trace("failed " + e.Error + " " + e.ExtendedErrorCode?.HResult.ToString("X8"), uri, s);
                opened.TrySetResult(false);
                Dispatch(() => { if (_ready && _player == player) Fail("Playback failed. Retry or open the media file."); });
            };
            player.MediaEnded += (s, e) => Dispatch(() =>
            {
                if (_player == player && _ready && player.PlaybackSession.NaturalDuration == TimeSpan.Zero)
                    Fail("The source did not contain playable media. Retry or open the media file.");
            });
            player.PlaybackSession.PlaybackStateChanged += (s, e) => Dispatch(() =>
            {
                if (_player != player) return;
                var state = player.PlaybackSession.PlaybackState;
                if (state == MediaPlaybackState.Playing) _wasPlaying = true;
                else if (state == MediaPlaybackState.Paused) _wasPlaying = false;
                UpdateAudioBar();
                bool buffering = _ready && state == MediaPlaybackState.Buffering;
                _progress.Visibility = buffering ? Visibility.Visible : _ready ? Visibility.Collapsed : Visibility.Visible;
                if (!buffering) _bufferingSince = null;
                else if (!_bufferingSince.HasValue) _bufferingSince = DateTime.UtcNow;
            });
            try
            {
                if (uri.Scheme == "ms-appdata" || uri.Scheme == "ms-appx")
                    _source = MediaSource.CreateFromStorageFile(await StorageFile.GetFileFromApplicationUriAsync(uri));
                else _source = MediaSource.CreateFromUri(uri);
                if (!Current(request)) return false;
                player.Source = _source;
                Trace("opening", uri, player);
                var complete = await Task.WhenAny(opened.Task, Task.Delay(TimeSpan.FromSeconds(20), token));
                token.ThrowIfCancellationRequested();
                if (complete != opened.Task) Trace("timeout", uri, player);
                return Current(request) && complete == opened.Task && await opened.Task;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Media open: " + ex.Message); return false; }
        }

        private void Ready(bool resume)
        {
            _operation?.CancelAfter(Timeout.Infinite);
            _ready = true; _status.Text = ""; _progress.Visibility = Visibility.Collapsed;
            if (_display != null)
            {
                _display.Visibility = Visibility.Visible;
                _display.SetMediaPlayer(_player);
                _display.AreTransportControlsEnabled = true; _display.IsHitTestVisible = true;
            }
            if (_playPause != null) { _playPause.IsEnabled = true; _positionTimer.Start(); UpdateAudioBar(); }
            if (_poster != null) _poster.Visibility = Visibility.Collapsed;
            _watchdog.Start();
            if (resume) _player.Play();
        }

        private void Fail(string message)
        {
            Stop(); _status.Text = message; _progress.Visibility = Visibility.Collapsed;
            _retry.Visibility = _video ? Visibility.Visible : Visibility.Collapsed;
            _open.Visibility = Visibility.Visible;
            if (_playPause != null) _playPause.IsEnabled = true;
        }

        private async void Dispatch(Action action)
        {
            if (_disposed) return;
            try { await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => { if (!_disposed && _attached) action(); }); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Media dispatcher: " + ex.Message); }
        }

        private void ClosePlayer()
        {
            _ready = false; _bufferingSince = null; _watchdog.Stop();
            _positionTimer.Stop();
            if (_display != null)
            {
                _display.AreTransportControlsEnabled = false; _display.IsHitTestVisible = false;
                _display.SetMediaPlayer(null);
            }
            if (_seek != null) _seek.IsEnabled = false;
            var player = _player; _player = null;
            if (player != null) { player.Pause(); player.Source = null; player.Dispose(); }
            _source?.Dispose(); _source = null;
            if (_poster != null) _poster.Visibility = Visibility.Visible;
        }

        private void Stop()
        {
            if (_player != null && _traceUri != null) Trace("stopping attached=" + _attached + " disposed=" + _disposed + " articleCancelled=" + _articleToken.IsCancellationRequested, _traceUri, _player);
            _request++; _operation?.Cancel(); _operation?.Dispose(); _operation = null;
            ClosePlayer();
        }

        public void Dispose() { if (_disposed) return; _disposed = true; _attached = false; Stop(); }

        private void UpdateAudioBar()
        {
            if (_playPause == null || _player == null || !_ready) return;
            var session = _player.PlaybackSession;
            bool playing = session.PlaybackState == MediaPlaybackState.Playing;
            _playPause.Content = new SymbolIcon(playing ? Symbol.Pause : Symbol.Play);
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(_playPause, playing ? "Pause" : "Play");
            _updatingPosition = true;
            try
            {
                double duration = session.NaturalDuration > TimeSpan.Zero ? session.NaturalDuration.TotalSeconds : _expectedDuration;
                _seek.Maximum = Math.Max(1, duration);
                _seek.Value = Math.Max(0, Math.Min(_seek.Maximum, session.Position.TotalSeconds));
                _seek.IsEnabled = session.CanSeek;
                _time.Text = Duration(session.Position.TotalSeconds) + " / " + Duration(duration, true);
            }
            finally { _updatingPosition = false; }
        }

        private static string Duration(double seconds, bool unknownWhenZero = false)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 || seconds >= TimeSpan.MaxValue.TotalSeconds) return "--:--";
            if (seconds == 0) return unknownWhenZero ? "--:--" : "0:00";
            var time = TimeSpan.FromSeconds(seconds);
            return time.TotalHours >= 1 ? ((long)time.TotalHours) + ":" + time.Minutes.ToString("D2") + ":" + time.Seconds.ToString("D2") : ((long)time.TotalMinutes) + ":" + time.Seconds.ToString("D2");
        }

        private static readonly object TraceGate = new object();
        private static void Trace(string stage, Uri uri, MediaPlayer player)
        {
            try
            {
                string state = player == null ? "idle" : player.PlaybackSession.PlaybackState + " duration=" + player.PlaybackSession.NaturalDuration.TotalSeconds;
                lock (TraceGate)
                {
                    var path = System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path, "media-preparation.log");
                    var previous = System.IO.File.Exists(path) ? System.IO.File.ReadAllLines(path) : new string[0];
                    var lines = new List<string>(previous);
                    lines.Add(DateTime.UtcNow.ToString("O") + " " + stage + " " + uri.Scheme + " " + uri.Host + " " +
                        state);
                    if (lines.Count > 30) lines.RemoveRange(0, lines.Count - 30);
                    System.IO.File.WriteAllLines(path, lines);
                }
            }
            catch (Exception) { }
        }
    }
}
