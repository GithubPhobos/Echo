using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Echo.Platform.Windows;

/// <inheritdoc cref="IAudioDeviceEvents"/>
/// <remarks>
///     Windows tells about its sound devices through the notices of its audio engine. A device that comes back after
///     sleep goes away and comes back several times within a few seconds, so the changes are told only once the
///     devices have been quiet for a moment, and once for the whole series.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class WindowsAudioDeviceEvents : IAudioDeviceEvents, IMMNotificationClient, IDisposable
{
    /// <summary>
    ///     How long the devices stay quiet before their changes are told. After sleep, the loudspeakers of a USB sound
    ///     card went away and came back twice within six seconds, a second at most apart.
    /// </summary>
    internal static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(2);

    private readonly ILogger<WindowsAudioDeviceEvents> _logger;
    private readonly MMDeviceEnumerator? _devices;
    private readonly Func<string, DataFlow?> _flowOf;
    private readonly TimeProvider _time;

    /// <summary>
    ///     One telling at a time: a slow look at the devices must not let the telling of a later change overtake it.
    /// </summary>
    private readonly object _telling = new();

    /// <summary>
    ///     Guards everything below: the notices come on threads of the system, and the changes are told on another.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    ///     The devices that changed since the changes were last told. Whether each records or plays is asked only once
    ///     they have settled: a notice of the system is not to be kept waiting.
    /// </summary>
    private readonly HashSet<string> _changedDevices = [];

    /// <summary>
    ///     The quiet period after the latest change, which tells the whole series.
    /// </summary>
    private ITimer? _settling;

    /// <summary>
    ///     How many changes have come: a telling that finds more than when it began leaves them to the quiet period of the
    ///     latest one.
    /// </summary>
    private long _changes;

    private bool _isMicrophonesChanged;
    private bool _isLoudspeakersChanged;
    private bool _isDisposed;

    /// <inheritdoc/>
    public event EventHandler? MicrophonesChanged;

    /// <inheritdoc/>
    public event EventHandler? LoudspeakersChanged;

    /// <summary>
    ///     Initializes a new instance of the <see cref="WindowsAudioDeviceEvents"/>, listening to the audio engine.
    /// </summary>
    /// <param name="logger"><see cref="ILogger{WindowsAudioDeviceEvents}"/></param>
    public WindowsAudioDeviceEvents(ILogger<WindowsAudioDeviceEvents> logger)
        : this(logger, TimeProvider.System, flowOf: null)
    {
        try
        {
            _devices = new MMDeviceEnumerator();

            int result = _devices.RegisterEndpointNotificationCallback(this);
            Marshal.ThrowExceptionForHR(result);
        }
        catch (Exception exc)
        {
            _devices?.Dispose();
            _devices = null;

            // Echo works without the notices: a microphone it lost is looked for again at every press of the key
            _logger.LogDebug("{NoteEmoji} Sound devices: Windows does not tell about their changes: {ErrorMessage}",
                LoggerConstants.NoteEmoji, exc.Message);
        }
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="WindowsAudioDeviceEvents"/> that only takes the notices it is
    ///     given.
    /// </summary>
    /// <param name="logger"><see cref="ILogger{WindowsAudioDeviceEvents}"/></param>
    /// <param name="time">The clock the devices settle by.</param>
    /// <param name="flowOf">
    ///     Whether a device records or plays, null for one the system does not know; by default the system is asked.
    /// </param>
    internal WindowsAudioDeviceEvents(ILogger<WindowsAudioDeviceEvents> logger,
                                      TimeProvider time,
                                      Func<string, DataFlow?>? flowOf)
    {
        _logger = logger;
        _time = time;
        _flowOf = flowOf ?? FlowOf;
    }

    /// <inheritdoc/>
    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) =>
        Note(deviceId);

    /// <inheritdoc/>
    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) =>
        Note(pwstrDeviceId);

    /// <inheritdoc/>
    void IMMNotificationClient.OnDeviceRemoved(string deviceId) =>
        Note(deviceId);

    /// <summary>
    ///     Echo plays where the system plays by default, so a new default for playing counts. The default microphone
    ///     does not: Echo records from the one chosen in setup.
    /// </summary>
    /// <inheritdoc/>
    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow != DataFlow.Render || role == Role.Communications)
            return;

        lock (_lock)
        {
            _isLoudspeakersChanged = true;
            Settle();
        }
    }

    /// <summary>
    ///     A changed volume or name leaves the sound of the device as it was.
    /// </summary>
    /// <inheritdoc/>
    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _settling?.Dispose();
        }

        if (_devices is not null)
        {
            _devices.UnregisterEndpointNotificationCallback(this);
            _devices.Dispose();
        }
    }

    private void Note(string deviceId)
    {
        lock (_lock)
        {
            _changedDevices.Add(deviceId);
            Settle();
        }
    }

    /// <summary>
    ///     Counts a change and waits for the devices to be quiet again from now on. Called under <see cref="_lock"/>.
    /// </summary>
    private void Settle()
    {
        if (_isDisposed)
            return;

        // A quiet period of its own for every change: one that has already run out finds that a later change came
        long changes = ++_changes;

        _settling?.Dispose();
        _settling = _time.CreateTimer(_ => Tell(changes), null, SettleTime, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    ///     Tells the changes once the devices have settled: what each changed device does decides what changed.
    /// </summary>
    /// <param name="changes">How many changes had come when this quiet period began.</param>
    private void Tell(long changes)
    {
        lock (_telling)
        {
            string[] changedDevices;

            lock (_lock)
            {
                // A later change, even one that came just as this period ran out, has a quiet period of its own
                if (_isDisposed || _changes != changes)
                    return;

                changedDevices = [.. _changedDevices];
                _changedDevices.Clear();
            }

            bool isMicrophonesChanged = false;
            bool isLoudspeakersChanged = false;

            foreach (string deviceId in changedDevices)
            {
                // A device the system does not know any more may have been either
                DataFlow? flow = _flowOf(deviceId);

                isMicrophonesChanged |= flow is null or DataFlow.Capture;
                isLoudspeakersChanged |= flow is null or DataFlow.Render;
            }

            lock (_lock)
            {
                _isMicrophonesChanged |= isMicrophonesChanged;
                _isLoudspeakersChanged |= isLoudspeakersChanged;

                // A device changed while these were looked at: they are told with it, once the devices are quiet again
                if (_isDisposed || _changes != changes)
                    return;

                isMicrophonesChanged = _isMicrophonesChanged;
                isLoudspeakersChanged = _isLoudspeakersChanged;

                _isMicrophonesChanged = false;
                _isLoudspeakersChanged = false;
            }

            if (isMicrophonesChanged)
                MicrophonesChanged?.Invoke(this, EventArgs.Empty);

            if (isLoudspeakersChanged)
                LoudspeakersChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    ///     Whether the device records or plays, as the system says; null when it does not know the device.
    /// </summary>
    private DataFlow? FlowOf(string deviceId)
    {
        if (_devices is null)
            return null;

        try
        {
            using MMDevice device = _devices.GetDevice(deviceId);

            return device.DataFlow;
        }
        catch (Exception)
        {
            // A device removed from the system is not found any more
            return null;
        }
    }
}