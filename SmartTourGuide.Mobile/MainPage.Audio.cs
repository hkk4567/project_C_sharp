using Plugin.Maui.Audio;

namespace SmartTourGuide.Mobile;

public partial class MainPage
{
    // OnPlayAudioClicked, PlayAudioQueueAsync, PlayRemoteAudioAndWaitAsync
    // ════════════════════════════════════════════════════════════════════════
    //  NÚT PHÁT / DỪNG
    // ════════════════════════════════════════════════════════════════════════
    private async void OnPlayAudioClicked(object? sender, EventArgs e)
    {
        if (_isPlaying) { StopAudio(); return; }
        if (_currentSelectedPoi == null) return;

        PrepareListenSession(_currentSelectedPoi.Id, allowReuseCurrent: true);

        _isPlaying = true;
        btnPlayAudio.Text = "⏹️ Dừng phát";

        _queueCts?.Cancel();
        _queueCts = new CancellationTokenSource();

        try
        {
            await PlayAudioQueueAsync(_currentSelectedPoi, _queueCts.Token);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Lỗi", "Không thể phát âm thanh: " + ex.Message, "OK");
            StopAudio();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  AUDIO QUEUE ENGINE
    // ════════════════════════════════════════════════════════════════════════
    private async Task PlayAudioQueueAsync(PoiModel poi, CancellationToken ct)
    {
        var urls = poi.AudioUrls;

        if (urls == null || urls.Count == 0)
        {
            await SpeakDescription(poi.Description);
            return;
        }

        if (!_poiAudioIndex.TryGetValue(poi.Id, out int startIndex) || startIndex >= urls.Count)
            startIndex = 0;
        // Ghi lại thời điểm bắt đầu phát
        // Dùng để tính tổng thời gian nghe khi dừng hoặc hết audio
        var playStartTime = DateTime.Now;
        _playStartTime = playStartTime;
        _currentAudioPoiId = poi.Id; // ✅ Lưu POI ID hiện tại
        PrepareListenSession(poi.Id, allowReuseCurrent: true);

        for (int i = startIndex; i < urls.Count; i++)
        {
            if (ct.IsCancellationRequested)
            {
                _poiAudioIndex[poi.Id] = i;
                // ✅ LOG KHI USER CANCEL/STOP GIỮA CHỪNG VÀ KHI RỜI VÙNG
                await LogAudioPlaybackAsync(poi.Id, playStartTime);
                return;
            }

            int displayIdx = i + 1;
            int total = urls.Count;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                SetStatus($"🎵 {poi.Name}  ·  {displayIdx}/{total}", priority: 3);
                if (btnPlayAudio != null && total > 1)
                    btnPlayAudio.Text = $"⏹️ Dừng  ({displayIdx}/{total})";
            });

            string rawPath = urls[i].Replace("\\", "/").TrimStart('/');
            string fullUrl = $"{BaseApiUrl.TrimEnd('/')}/{rawPath}";

            try
            {
                await PlayRemoteAudioAndWaitAsync(fullUrl, ct);
            }
            catch (OperationCanceledException)
            {
                int nextIdx = i + 1;
                _poiAudioIndex[poi.Id] = nextIdx < urls.Count ? nextIdx : 0;
                // ✅ LOG KHI USER DỪNG 1 AUDIO (không phải hết hàng)
                await LogAudioPlaybackAsync(poi.Id, playStartTime);
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AudioQueue] Lỗi audio {i}: {ex.Message}");
            }
        }


        // Phát hết toàn bộ → reset về 0
        _poiAudioIndex[poi.Id] = 0;
        await LogAudioPlaybackAsync(poi.Id, playStartTime);
        _currentAudioPoiId = 0; // ✅ Reset POI ID
        _isPlaying = false;
        int played = urls.Count;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            SetStatus($"✅ {poi.Name}  ·  Phát xong {played} audio", priority: 1, autoRevertMs: 3000);
            if (btnPlayAudio != null) btnPlayAudio.Text = "🔊 Nghe lại";
        });
    }
    private async Task PlayRemoteAudioAndWaitAsync(string url, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        string fixedUrl = url.Replace("\\", "/");
        string? localPath = await GetCachedAudioPathAsync(fixedUrl);

        Stream audioStream;
        if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
        {
            audioStream = File.OpenRead(localPath);
        }
        else
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var networkStream = await client.GetStreamAsync(fixedUrl, ct);
            var mem = new MemoryStream();
            await networkStream.CopyToAsync(mem, ct);
            mem.Position = 0;
            audioStream = mem;
        }

        _audioPlayer?.Stop();
        _audioPlayer?.Dispose();

        var currentPlayer = AudioManager.Current.CreatePlayer(audioStream);
        _audioPlayer = currentPlayer;

        currentPlayer.PlaybackEnded += (s, e) =>
        {
            audioStream.Dispose();
            tcs.TrySetResult(true);
        };

        using var reg = ct.Register(() =>
        {
            try { currentPlayer?.Stop(); } catch (Exception) { }
            try { audioStream?.Dispose(); } catch (Exception) { }
            tcs.TrySetCanceled();
        });

        currentPlayer.Volume = 0;
        currentPlayer.Play();
        _ = FadeInVolumeAsync(currentPlayer);

        await tcs.Task;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  LOG AUDIO PLAYBACK (GỌI KHI STOP/CANCEL)
    // ════════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Ghi lại listen event khi user:
    /// - Dừng audio bằng nút "⏹️ Dừng"
    /// - Rời vùng POI lúc đang nghe (cancel token)
    /// - Dừng 1 audio trong queue (OperationCanceledException)
    /// 
    /// Fire-and-forget — không block UI, lỗi mạng được lieca.
    /// </summary>
    private async Task LogAudioPlaybackAsync(int poiId, DateTime startTime)
    {
        try
        {
            if (_isGeofenceVisitActive && _loggedPoisInCurrentGeofenceVisit.Contains(poiId))
                return;

            var durationSec = (int)(DateTime.Now - startTime).TotalSeconds;
            if (durationSec < 1) return; // Lọc bấm nhầm (< 1 giây)

            if (_isGeofenceVisitActive)
                _loggedPoisInCurrentGeofenceVisit.Add(poiId);

            // Không await — fire and forget
            _ = _apiService.LogPoiListenAsync(poiId, durationSec, _deviceId, _currentListenSessionId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Log Playback] Lỗi: {ex.Message}");
            // Không throw — log lỗi xong và tiếp tục
        }
    }

    private void PrepareListenSession(int poiId, bool allowReuseCurrent)
    {
        if (allowReuseCurrent && _currentListenSessionPoiId == poiId && !string.IsNullOrWhiteSpace(_currentListenSessionId))
            return;

        _currentListenSessionPoiId = poiId;
        _currentListenSessionId = Guid.NewGuid().ToString("N");
    }

    // PauseForInterruption, ResumeFromInterruption
    // ════════════════════════════════════════════════════════════════════════
    //  TẠMM DỪNG / TIẾP TỤC KHI CÓ CUỘC GỌI / APP VÀO BACKGROUND
    // ════════════════════════════════════════════════════════════════════════
    private void PauseForInterruption()
    {
        if (_isPlaying && _audioPlayer != null && !_isPausedByInterruption)
        {
            _audioPlayer.Pause();
            _isPausedByInterruption = true;
        }
    }
    private void ResumeFromInterruption()
    {
        if (_isPausedByInterruption && _audioPlayer != null)
        {
            _audioPlayer.Play();
            _isPausedByInterruption = false;
        }
    }
    // PreWarmAudioAsync, CreateSilenceWav, FadeInVolumeAsync
    // ════════════════════════════════════════════════════════════════════════
    //  PRE-WARM AUDIO PIPELINE
    // ════════════════════════════════════════════════════════════════════════
    private async Task PreWarmAudioAsync()
    {
        try
        {
            var silenceBytes = CreateSilenceWav(durationMs: 200);
            var silenceStream = new MemoryStream(silenceBytes);
            var warmupPlayer = AudioManager.Current.CreatePlayer(silenceStream);
            warmupPlayer.Volume = 0;
            warmupPlayer.Play();
            await Task.Delay(250);
            warmupPlayer.Stop();
            warmupPlayer.Dispose();
            silenceStream.Dispose();
        }
        catch { }
    }
    private static byte[] CreateSilenceWav(int durationMs = 200)
    {
        const int sampleRate = 22050;
        const int channels = 1;
        const int bitsPerSample = 16;
        int numSamples = (sampleRate * durationMs) / 1000;
        int dataSize = numSamples * channels * (bitsPerSample / 8);

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * (bitsPerSample / 8));
        writer.Write((short)(channels * (bitsPerSample / 8)));
        writer.Write((short)bitsPerSample);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
        return ms.ToArray();
    }
    private async Task FadeInVolumeAsync(IAudioPlayer player)
    {
        const int steps = 15;
        const int intervalMs = 10;
        const double target = 1.0;

        for (int i = 1; i <= steps; i++)
        {
            await Task.Delay(intervalMs);
            if (player != _audioPlayer || !_isPlaying) break;
            player.Volume = target * i / steps;
        }
        if (player == _audioPlayer && _isPlaying) player.Volume = target;
    }
    // GetLocalAudioPathAsync, SpeakDescription, StopAudio
    // ════════════════════════════════════════════════════════════════════════
    //  CACHE AUDIO
    // ════════════════════════════════════════════════════════════════════════
    private async Task<string?> GetLocalAudioPathAsync(string url)
    {
        try
        {
            string fileName = Path.GetFileName(url.Split('?')[0]);
            string localPath = Path.Combine(FileSystem.CacheDirectory, fileName);
            if (File.Exists(localPath)) return localPath;

            using var client = new HttpClient();
            var data = await client.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(localPath, data);
            return localPath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Cache] Lỗi: {ex.Message}");
            return null;
        }
    }
    // ════════════════════════════════════════════════════════════════════════
    //  TEXT-TO-SPEECH
    // ════════════════════════════════════════════════════════════════════════
    private async Task SpeakDescription(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var ttsCancellationToken = new CancellationTokenSource();

        try
        {
            // Hủy token cũ nếu đang có
            _ttsCancellationToken?.Cancel();
            _ttsCancellationToken?.Dispose();
            _ttsCancellationToken = ttsCancellationToken;

            var locales = await TextToSpeech.GetLocalesAsync();
            if (ttsCancellationToken.IsCancellationRequested) return;

            // Tìm tiếng Việt, nếu không có thì dùng locale mặc định (null)
            var vnLocale = locales?.FirstOrDefault(l => l.Language == "vi");

            await TextToSpeech.SpeakAsync(text, new SpeechOptions
            {
                Locale = vnLocale, // null = giọng mặc định của máy
                Pitch = 1.0f,
                Volume = 1.0f
            }, ttsCancellationToken.Token);

            if (!ttsCancellationToken.IsCancellationRequested)
                _isPlaying = false;

            // Kiểm tra null trước khi gán Text
            if (!ttsCancellationToken.IsCancellationRequested && btnPlayAudio != null)
                btnPlayAudio.Text = "🗣️ Đọc lại";
        }
        catch (OperationCanceledException)
        {
            // Bị hủy bình thường — không cần làm gì
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TTS] Lỗi: {ex.Message}");
            _isPlaying = false;
            if (btnPlayAudio != null)
                btnPlayAudio.Text = "🗣️ Đọc lại";
        }
        finally
        {
            if (ReferenceEquals(_ttsCancellationToken, ttsCancellationToken))
            {
                _ttsCancellationToken = null;
            }
            ttsCancellationToken.Dispose();
        }
    }
    // ════════════════════════════════════════════════════════════════════════
    //  DỪNG TẤT CẢ
    // ════════════════════════════════════════════════════════════════════════
    private void StopAudio()
    {
        var wasPlaying = _isPlaying;
        var selectedPoi = _currentSelectedPoi;

        _queueCts?.Cancel();
        _queueCts = null;
        _isPausedByInterruption = false;
        _statusRevertCts?.Cancel();
        _statusRevertCts = null;
        _statusPriority = 0;

        if (_audioPlayer != null)
        {
            if (_audioPlayer.IsPlaying) _audioPlayer.Stop();
            _audioPlayer.Dispose();
            _audioPlayer = null;
        }

        if (_ttsCancellationToken != null && !_ttsCancellationToken.IsCancellationRequested)
        {
            _ttsCancellationToken.Cancel();
            _ttsCancellationToken = null;
        }

        _isPlaying = false;
        _currentAudioPoiId = 0;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (selectedPoi?.AudioUrls?.Count > 0)
            {
                _poiAudioIndex.TryGetValue(selectedPoi.Id, out int idx);
                int next = (idx < selectedPoi.AudioUrls.Count) ? idx + 1 : 1;
                int total = selectedPoi.AudioUrls.Count;
                btnPlayAudio.Text = total > 1
                    ? $"🔊 Nghe audio ({next}/{total})"
                    : "🔊 Nghe File Ghi Âm";
            }
            else
            {
                btnPlayAudio.Text = "🗣️ Đọc Tự Động (TTS)";
            }
        });
    }
}