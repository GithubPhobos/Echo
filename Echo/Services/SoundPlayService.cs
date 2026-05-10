using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Echo.Services;

/// <inheritdoc cref="ISoundPlayService"/>
/// <param name="assetsProvider"><see cref="IAssetsProvider"/></param>
/// <param name="logger"><see cref="ILogge{SoundPlayService}"/></param>
/// <param name="pushToTalkSettingsOptions"><see cref="IOptions<PushToTalkSettings>"/></param>
internal sealed class SoundPlayService(
    IAssetsProvider assetsProvider,
    ILogger<SoundPlayService> logger,
    IOptions<PushToTalkSettings> pushToTalkSettingsOptions) : ISoundPlayService
{
    private readonly IAssetsProvider _assetsProvider = assetsProvider;
    private readonly ILogger<SoundPlayService> _logger = logger;
    private readonly PushToTalkSettings _pushToTalkSettings = pushToTalkSettingsOptions.Value;

    private float? _volume;
    private bool _canPlaySounds;
    private CachedSound? _onSound;
    private CachedSound? _offSound;
    private WasapiOut? _outputDevice;
    private MixingSampleProvider? _mixer;

    /// <inheritdoc/>
    public void EnsureCanPlaySounds()
    {
        const string StartRecordingFileName = "start-recording.wav";
        const string StopRecordingFileName = "stop-recording.wav";

        if (!_pushToTalkSettings.PlaySound)
            return;

        _logger.LogDebug("{NoteEmoji} Sounds:", LoggerConstants.NoteEmoji);
        _logger.LogDebug("{Tab}{LoadingEmoji} Searching and loading required files into memory...",
            LoggerConstants.Tab, LoggerConstants.LoadingEmoji);

        string? errorMessage = null;
        try
        {
            var startRecordingFilePath = _assetsProvider.GetFilePath(StartRecordingFileName);
            var stopRecordingFilePath = _assetsProvider.GetFilePath(StopRecordingFileName);

            if (!_assetsProvider.CheckIfExists(startRecordingFilePath)
                ||
                !_assetsProvider.CheckIfExists(stopRecordingFilePath))
            {
                errorMessage = "Missing required audio files.";
                _canPlaySounds = false;
            }
            else
            {
                if (_pushToTalkSettings.Volume is not null)
                {
                    _volume = Math.Max(Math.Min(_pushToTalkSettings.Volume.Value, 1.0f), 0f);
                }

                var mixerFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

                _mixer = new MixingSampleProvider(mixerFormat)
                {
                    ReadFully = true
                };

                _onSound = new CachedSound(startRecordingFilePath, mixerFormat);
                _offSound = new CachedSound(stopRecordingFilePath, mixerFormat);

                _outputDevice = new WasapiOut(AudioClientShareMode.Shared, 50);
                _outputDevice.Init(_mixer);
                _outputDevice.Play();

                _canPlaySounds = true;
            }
        }
        catch (Exception exc)
        {
            errorMessage = exc.Message;
            _canPlaySounds = false;
        }

        if (!_canPlaySounds)
        {
            _logger.LogDebug("{Tab}{WarningEmoji} Cannot play sounds: {ErrorMessage}",
                LoggerConstants.Tab, LoggerConstants.WarningEmoji, errorMessage);
        }
        else
        {
            _logger.LogDebug("{Tab}{CheckedEmoji} Audio Engine started and files cached.",
                LoggerConstants.Tab, LoggerConstants.CheckedEmoji);
            _logger.LogDebug("{Tab}{InfoEmoji} Volume is set to: {Volume}",
                LoggerConstants.Tab, LoggerConstants.InfoEmoji, _volume);
        }
    }

    /// <inheritdoc/>
    public Task PlayMicrophoneOnSoundAsync()
    {
        PlayCachedSound(_onSound);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task PlayMicrophoneOffSoundAsync()
    {
        PlayCachedSound(_offSound);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _outputDevice?.Stop();
        _outputDevice?.Dispose();
    }

    /// <summary>
    ///     Injects the requested cached sound into the active audio mixer for instant playback.
    /// </summary>
    /// <param name="sound">The pre-loaded sound data to play.</param>
    private void PlayCachedSound(CachedSound? sound)
    {
        if (!_canPlaySounds
            ||
            sound is null
            ||
            _mixer is null)
            return;

        try
        {
            var soundProvider = new CachedSoundSampleProvider(sound);
            var volumeProvider = new VolumeSampleProvider(soundProvider)
            { 
                Volume = _volume!.Value 
            };

            _mixer.AddMixerInput(volumeProvider);
        }
        catch (Exception exc)
        {
            _logger.LogError("{WarningEmoji} Couldn't play sound: {ErrorMessage}",
                LoggerConstants.WarningEmoji, exc.Message);

            throw;
        }
    }

    /// <summary>
    ///     Represents an audio file fully loaded into memory (RAM) as floating-point samples 
    ///     for instant, zero-latency playback. Automatically resamples audio to match a target format.
    /// </summary>
    private sealed class CachedSound
    {
        /// <summary>
        ///     Gets the raw audio samples represented as 32-bit IEEE floating-point values.
        /// </summary>
        public float[] AudioData { get; }

        /// <summary>
        ///     Gets the wave format (sample rate, channels, etc.) of the cached audio.
        /// </summary>
        public WaveFormat WaveFormat { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="CachedSound"/> class by reading, 
        ///     decoding, and optionally resampling the audio file from disk.
        /// </summary>
        /// <param name="audioFileName">The path to the audio file.</param>
        /// <param name="targetFormat">The target format the audio should be converted to.</param>
        public CachedSound(string audioFileName, WaveFormat targetFormat)
        {
            using AudioFileReader audioFileReader = new(audioFileName);
            WaveFormat = targetFormat;

            ISampleProvider provider = audioFileReader;

            // Align Sample Rate (Resampling)
            if (provider.WaveFormat.SampleRate != targetFormat.SampleRate)
            {
                provider = new WdlResamplingSampleProvider(provider, targetFormat.SampleRate);
            }

            // Align Channels (Mono to Stereo / Stereo to Mono)
            if (provider.WaveFormat.Channels != targetFormat.Channels)
            {
                if (provider.WaveFormat.Channels == 1 && targetFormat.Channels == 2)
                {
                    provider = new MonoToStereoSampleProvider(provider);
                }
                else if (provider.WaveFormat.Channels == 2 && targetFormat.Channels == 1)
                {
                    provider = new StereoToMonoSampleProvider(provider)
                    { 
                        LeftVolume = 0.5f,
                        RightVolume = 0.5f
                    };
                }
                else
                {
                    throw new InvalidOperationException("Unsupported channel conversion.");
                }
            }

            // Read the normalized audio into memory
            var wholeFile = new List<float>();
            var readBuffer = new float[targetFormat.SampleRate * targetFormat.Channels];

            int samplesRead;
            while ((samplesRead = provider.Read(readBuffer, 0, readBuffer.Length)) > 0)
            {
                wholeFile.AddRange(readBuffer.Take(samplesRead));
            }

            AudioData = [.. wholeFile];
        }
    }

    /// <summary>
    ///     Provides audio samples from a <see cref="CachedSound"/> to the audio playback engine. 
    /// </summary>
    /// <remarks>
    ///     Each instance acts as an independent "cursor", tracking its own playback position.
    /// </remarks>
    private sealed class CachedSoundSampleProvider(CachedSound cachedSound) : ISampleProvider
    {
        private readonly CachedSound _cachedSound = cachedSound;

        private long _position;

        /// <summary>
        ///     Gets the audio format required by the mixer to play this sound.
        /// </summary>
        public WaveFormat WaveFormat => _cachedSound.WaveFormat;

        /// <summary>
        ///     Copies audio samples from the memory cache to the playback output buffer.
        /// </summary>
        /// <param name="buffer">The array to write the audio samples into.</param>
        /// <param name="offset">The index in the buffer to start writing.</param>
        /// <param name="count">The maximum number of samples to read.</param>
        /// <returns>
        ///     The actual number of samples read. Returns 0 if the end of the sound is reached.
        /// </returns>
        public int Read(float[] buffer, int offset, int count)
        {
            long availableSamples = _cachedSound.AudioData.Length - _position;
            long samplesToCopy = Math.Min(availableSamples, count);

            Array.Copy(_cachedSound.AudioData, _position, buffer, offset, samplesToCopy);

            _position += samplesToCopy;

            return
                (int)samplesToCopy;
        }
    }
}
