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

        public ArticleMediaView(NativeArticleNode node, Func<string, CancellationToken, Task<string>> resolve,
            CancellationToken articleToken, FrameworkElement poster = null)
        {
            _sources = NativeMediaService.PlaybackSources(node.MediaSources);
            string duration; double expected;
            if (node.Attributes.TryGetValue("data-durationhint", out duration) && double.TryParse(duration, NumberStyles.Float, CultureInfo.InvariantCulture, out expected)) _expectedDuration = expected;
            _resolve = resolve; _articleToken = articleToken; _poster = poster;
            bool video = node.TagName == "video";
            _display = new MediaPlayerElement
            {
                AutoPlay = false, AreTransportControlsEnabled = true, Visibility = Visibility.Collapsed,
                MinHeight = video ? 180 : 80, MaxHeight = video ? 480 : 96,
                TransportControls = new MediaTransportControls { IsCompact = true, ShowAndHideAutomatically = video }
            };
            Windows.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_display, "ArticleMediaPlayer");
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(_display, video ? "Video player" : "Audio player");
            Windows.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_status, "ArticleMediaStatus");
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(_progress, "Loading media");
            _choices = new ComboBox { ItemsSource = _sources, DisplayMemberPath = "Label", SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch };
            var panel = new StackPanel();
            if (_poster != null) panel.Children.Add(_poster);
            if (_sources.Count > 1) panel.Children.Add(_choices);
            panel.Children.Add(_display); panel.Children.Add(_progress); panel.Children.Add(_status);
            panel.Children.Add(_retry); panel.Children.Add(_open); Content = panel;
            _choices.SelectionChanged += (s, e) => { if (_attached) _ = PrepareAsync(false); };
            _retry.Click += (s, e) => { _ = PrepareAsync(true); };
            Loaded += (s, e) => { if (_disposed || _attached) return; _attached = true; _ = PrepareAsync(false); };
            Unloaded += (s, e) => { _attached = false; Stop(); };
            _watchdog.Tick += (s, e) =>
            {
                if (_bufferingSince.HasValue && DateTime.UtcNow - _bufferingSince.Value > TimeSpan.FromSeconds(30))
                    Fail("Media buffering timed out. Retry or open the media file.");
            };
        }

        private bool Current(int request) => !_disposed && _attached && !_articleToken.IsCancellationRequested && request == _request;

        private async Task PrepareAsync(bool cachedFirst)
        {
            bool resume = _wasPlaying;
            Stop();
            if (_disposed || !_attached || _articleToken.IsCancellationRequested) return;
            int request = _request;
            _operation = CancellationTokenSource.CreateLinkedTokenSource(_articleToken);
            var token = _operation.Token;
            _retry.Visibility = _open.Visibility = Visibility.Collapsed;
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
                    if (!cachedFirst && await OpenAsync(candidate.Uri, request, token))
                    { Ready(resume); return; }
                    if (!Current(request)) return;
                    if (_resolve != null)
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
                        if (Uri.TryCreate(local, UriKind.Absolute, out uri) && uri != candidate.Uri && await OpenAsync(uri, request, token))
                        { Ready(resume); return; }
                    }
                    if (cachedFirst && await OpenAsync(candidate.Uri, request, token))
                    { Ready(resume); return; }
                    if (!Current(request)) return;
                }
                if (Current(request)) Fail("Windows could not open these media sources. Retry, open the media file, or use Web view.");
            }
            catch (OperationCanceledException) { }
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
            var player = new MediaPlayer { AutoPlay = false };
            _player = player;
            var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool mediaOpened = false;
            player.MediaOpened += (s, e) =>
            {
                mediaOpened = true;
                if (_expectedDuration <= 0 || s.PlaybackSession.NaturalDuration > TimeSpan.Zero) opened.TrySetResult(true);
            };
            player.PlaybackSession.NaturalDurationChanged += (s, e) =>
            { if (mediaOpened && s.NaturalDuration > TimeSpan.Zero) opened.TrySetResult(true); };
            player.MediaFailed += (s, e) =>
            {
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
                _display.SetMediaPlayer(player);
                player.Source = _source;
                var complete = await Task.WhenAny(opened.Task, Task.Delay(TimeSpan.FromSeconds(20), token));
                token.ThrowIfCancellationRequested();
                return Current(request) && complete == opened.Task && await opened.Task;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Media open: " + ex.Message); return false; }
        }

        private void Ready(bool resume)
        {
            _ready = true; _status.Text = ""; _progress.Visibility = Visibility.Collapsed;
            _display.Visibility = Visibility.Visible;
            if (_poster != null) _poster.Visibility = Visibility.Collapsed;
            _watchdog.Start();
            if (resume) _player.Play();
        }

        private void Fail(string message)
        {
            Stop(); _status.Text = message; _progress.Visibility = Visibility.Collapsed;
            _retry.Visibility = _open.Visibility = Visibility.Visible;
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
            _display.Visibility = Visibility.Collapsed;
            _display.SetMediaPlayer(null);
            var player = _player; _player = null;
            if (player != null) { player.Pause(); player.Source = null; player.Dispose(); }
            _source?.Dispose(); _source = null;
            if (_poster != null) _poster.Visibility = Visibility.Visible;
        }

        private void Stop()
        {
            _request++; _operation?.Cancel(); _operation?.Dispose(); _operation = null;
            ClosePlayer();
        }

        public void Dispose() { if (_disposed) return; _disposed = true; Stop(); }
    }
}
