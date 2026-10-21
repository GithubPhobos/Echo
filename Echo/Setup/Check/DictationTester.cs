using System.Collections.Concurrent;
using System.Globalization;
using Echo.Extensions;
using Echo.Setup.Hardware;
using Microsoft.Extensions.DependencyInjection;
using Whisper.net.LibraryLoader;

namespace Echo.Setup.Check;

/// <inheritdoc cref="IDictationTester"/>
internal sealed class DictationTester : IDictationTester
{
    /// <summary>
    ///     The largest model on a slow disk loads well within this; anything longer means a device is stuck.
    /// </summary>
    private static readonly TimeSpan _defaultStartTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    ///     How long a microphone may take to open. One held by another program may never answer.
    /// </summary>
    private static readonly TimeSpan _defaultDeviceTimeout = TimeSpan.FromSeconds(8);

    private readonly Func<SetupContext, ServiceProvider> _buildServices;
    private readonly TimeSpan _startTimeout;
    private readonly TimeSpan _deviceTimeout;

    /// <summary>
    ///     Done once every attempt nobody waited for any more has let go of the microphone and the key.
    ///     The next attempt starts only then, so the devices never have two owners.
    /// </summary>
    private Task _released = Task.CompletedTask;

    /// <param name="paths"><see cref="IAppPaths"/></param>
    public DictationTester(IAppPaths paths)
        : this(draft => BuildServices(paths, draft), _defaultStartTimeout, _defaultDeviceTimeout)
    {
    }

    /// <param name="buildServices">Makes the dictation services for the draft.</param>
    /// <param name="startTimeout">
    ///     How long an attempt is waited for: for the attempts before it to let go, and then for the model
    ///     once its microphone has answered.
    /// </param>
    /// <param name="deviceTimeout">How long the microphone of an attempt is waited for.</param>
    internal DictationTester(Func<SetupContext, ServiceProvider> buildServices, TimeSpan startTimeout, TimeSpan deviceTimeout)
    {
        _buildServices = buildServices;
        _startTimeout = startTimeout;
        _deviceTimeout = deviceTimeout;
    }

    /// <inheritdoc/>
    public async Task<IDictationTestSession> StartAsync(SetupContext draft, CancellationToken cancellationToken)
    {
        var abandoned = new CancellationTokenSource();
        var device = new DeviceProgress();

        Task<IDictationTestSession> creating = CreateAfterAsync(_released, draft, device, abandoned.Token);

        try
        {
            await Task.WhenAny(device.Asked.Task, creating).WaitAsync(_startTimeout, cancellationToken);

            // A microphone that does not answer must not look like a slow model
            await Task.WhenAny(device.Answered.Task, creating).WaitAsync(_deviceTimeout, cancellationToken);

            return await creating.WaitAsync(_startTimeout, cancellationToken);
        }
        catch (Exception exc) when (exc is TimeoutException or OperationCanceledException)
        {
            // Nobody waits for this attempt any more. One that has not begun never will; one that has lets go
            // of the microphone and the key once it is done, and the next attempt starts only after that
            abandoned.Cancel();
            _released = ReleaseLateAsync(creating);

            if (exc is TimeoutException)
                throw new DictationTestException(Strings.MicrophoneNoResponse, exc);

            throw;
        }
    }

    /// <summary>
    ///     Makes a session once everything the attempts before this one took has been let go of.
    /// </summary>
    private async Task<IDictationTestSession> CreateAfterAsync(Task released,
                                                              SetupContext draft,
                                                              DeviceProgress device,
                                                              CancellationToken abandoned)
    {
        await released;

        // Given up on while it waited: it never takes anything
        abandoned.ThrowIfCancellationRequested();

        // Loading a model takes seconds; the wizard keeps drawing meanwhile. Once begun, the making runs
        // to its end, so that whatever it took can be let go of
        return await Task.Run(() => CreateSession(draft, device, abandoned));
    }

    private IDictationTestSession CreateSession(SetupContext draft, DeviceProgress device, CancellationToken abandoned)
    {
        ServiceProvider? provider = null;

        try
        {
            provider = _buildServices(draft);

            // However long the microphone takes, the services stay until it has answered: nothing is let go of
            // while it is still being opened
            device.Asked.TrySetResult();
            provider.GetRequiredService<IAudioRecordingService>().InititalizeAudioRecordingCapabilities();
            device.Answered.TrySetResult();

            // Given up on while the microphone opened: the model is not loaded and the key is not taken
            abandoned.ThrowIfCancellationRequested();

            return new Session(provider);
        }
        catch (Exception exc)
        {
            provider?.Dispose();

            if (exc is OperationCanceledException)
                throw;

            throw new DictationTestException(exc.GetBaseException().Message, exc);
        }
    }

    /// <summary>
    ///     Waits for an attempt nobody waits for any more, and closes the session it made, if it made one.
    /// </summary>
    private static async Task ReleaseLateAsync(Task<IDictationTestSession> creating)
    {
        try
        {
            IDictationTestSession late = await creating;

            await late.DisposeAsync();
        }
        catch (Exception)
        {
            // What went wrong concerns an attempt nobody waits for; one that failed has let go on its way out
        }
    }

    private static ServiceProvider BuildServices(IAppPaths paths, SetupContext draft)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(paths);
        services.AddPlatform(paths);
        services.SetupConfiguration(ServiceCollectionExtensions.BuildConfiguration(paths, ToOverrides(draft)));
        services.RegisterDictationServices();

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     The draft choices in the shape of the settings file, laid over the defaults. Every additional
    ///     setting is given, including the ones left at the shipped value, so the test runs with exactly
    ///     what the saved settings will hold.
    /// </summary>
    internal static Dictionary<string, string?> ToOverrides(SetupContext draft)
    {
        Dictionary<string, string?> overrides = AdditionalSettings.All.ToDictionary(
            setting => $"{setting.Section}:{setting.Key}",
            draft.Additional.Get);

        foreach ((string key, string? value) in ChosenInWizard(draft))
            overrides[key] = value;

        return overrides;
    }

    /// <summary>
    ///     What the steps of the wizard chose, and what a test must never do.
    /// </summary>
    private static Dictionary<string, string?> ChosenInWizard(SetupContext draft) => new()
    {
        [$"{nameof(WhisperSettings)}:{nameof(WhisperSettings.ModelName)}"] = draft.ModelFileName,
        [$"{nameof(WhisperSettings)}:{nameof(WhisperSettings.Language)}"] = draft.DictationLanguage,
        [$"{nameof(WhisperSettings)}:{nameof(WhisperSettings.HardwareBackend)}"] = Acceleration.ToSetting(draft.RequestedBackend),
        [$"{nameof(WhisperSettings)}:{nameof(WhisperSettings.GpuDeviceIndex)}"] =
            (draft.GraphicsCardIndex ?? 0).ToString(CultureInfo.InvariantCulture),
        [$"{nameof(RecordingDeviceSettings)}:{nameof(RecordingDeviceSettings.DeviceIndex)}"] =
            draft.MicrophoneIndex.ToString(CultureInfo.InvariantCulture),

        // Empty rather than missing: the name saved before must not lead to another microphone than the one just chosen
        [$"{nameof(RecordingDeviceSettings)}:{nameof(RecordingDeviceSettings.DeviceName)}"] = draft.MicrophoneName ?? string.Empty,
        [$"{nameof(PushToTalkSettings)}:{nameof(PushToTalkSettings.Key)}"] = draft.Hotkey.Key,
        [$"{nameof(PushToTalkSettings)}:{nameof(PushToTalkSettings.Modifier)}"] = draft.Hotkey.Modifier,

        // A test must neither beep nor type into whatever window happens to be active
        [$"{nameof(PushToTalkSettings)}:{nameof(PushToTalkSettings.PlaySound)}"] = bool.FalseString,
        [$"{nameof(TextInsertionSettings)}:{nameof(TextInsertionSettings.UseAutoInsert)}"] = bool.FalseString,
    };

    /// <summary>
    ///     How far the opening of the microphone of an attempt has got.
    /// </summary>
    private sealed class DeviceProgress
    {
        public TaskCompletionSource Asked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Answered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Session : IDictationTestSession
    {
        private readonly ServiceProvider _provider;
        private readonly IAudioRecordingService _audio;
        private readonly IWhisperInferenceService _whisper;
        private readonly IPushToTalkMonitorService _pushToTalk;
        private readonly ConcurrentQueue<DictationTestEvent> _events = new();

        /// <summary>
        ///     Guards the key handlers against closing, so closing knows every phrase it has to wait for.
        /// </summary>
        private readonly object _lock = new();

        private Task _recognition = Task.CompletedTask;
        private bool _isClosing;

        public Session(ServiceProvider provider)
        {
            _provider = provider;
            _audio = provider.GetRequiredService<IAudioRecordingService>();
            _whisper = provider.GetRequiredService<IWhisperInferenceService>();
            _pushToTalk = provider.GetRequiredService<IPushToTalkMonitorService>();

            _whisper.TryInitializeWhisperModel();
            _pushToTalk.InitializePushToTalkHotkeysOrThrow();

            _pushToTalk.OnRecordingTriggered += OnPressed;
            _pushToTalk.OnRecordingReleased += OnReleased;

            _pushToTalk.StartListeningAsync(CancellationToken.None).GetAwaiter().GetResult();

            Runtime = RuntimeOptions.LoadedLibrary;
        }

        public RuntimeLibrary? Runtime { get; }

        public bool TryRead(out DictationTestEvent testEvent) =>
            _events.TryDequeue(out testEvent!);

        public async ValueTask DisposeAsync()
        {
            Task recognition;

            // A key handler that is already inside has either started its phrase or will find the session closing
            lock (_lock)
            {
                _isClosing = true;
                recognition = _recognition;
            }

            _pushToTalk.OnRecordingTriggered -= OnPressed;
            _pushToTalk.OnRecordingReleased -= OnReleased;

            // The model cannot be unloaded in the middle of a recognition, and a recognition always ends
            await recognition;

            await _provider.DisposeAsync();
        }

        private void OnPressed(object? sender, EventArgs e)
        {
            lock (_lock)
            {
                if (_isClosing)
                    return;

                try
                {
                    _audio.StartRecording();
                    _events.Enqueue(new DictationTestEvent(DictationTestEventKind.RecordingStarted));
                }
                catch (Exception exc)
                {
                    _events.Enqueue(new DictationTestEvent(DictationTestEventKind.Failed, exc.Message));
                }
            }
        }

        private void OnReleased(object? sender, EventArgs e)
        {
            lock (_lock)
            {
                if (_isClosing)
                    return;

                _events.Enqueue(new DictationTestEvent(DictationTestEventKind.Transcribing));

                _recognition = Recognize(_recognition);
            }
        }

        /// <summary>
        ///     Recognizes the phrase just said once the phrase before it is done.
        /// </summary>
        private Task Recognize(Task previous) =>
            Task.Run(async () =>
            {
                try
                {
                    MemoryStream? audio = _audio.StopRecording();

                    // One phrase at a time, in the order they were said: the model mixes up the text
                    // of phrases it is given together
                    await previous;

                    var stopwatch = Stopwatch.StartNew();

                    string? text = await _whisper.ProcessAudioAsync(audio);

                    _events.Enqueue(string.IsNullOrWhiteSpace(text)
                        ? new DictationTestEvent(DictationTestEventKind.NothingHeard)
                        : new DictationTestEvent(DictationTestEventKind.Recognized, text, stopwatch.Elapsed.TotalMilliseconds));
                }
                catch (Exception exc)
                {
                    _events.Enqueue(new DictationTestEvent(DictationTestEventKind.Failed, exc.Message));
                }
            });
    }
}