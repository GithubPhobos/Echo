using Echo.Setup;
using Echo.Setup.Audio;
using Echo.Setup.Steps;
using Echo.UI;
using Spectre.Console.Testing;

namespace Echo.Tests.UnitTests;

/// <summary>
///     The sound step page by page, where the order in which one device is opened and closed matters: the wizard as a
///     whole waits for every device to answer before it moves on, as a user rarely does. The keys of a page are given
///     before it is shown, since the page reads them on a thread of its own.
/// </summary>
public sealed class CheckStepTests
{
    /// <summary>
    ///     How long a test waits for what it holds up.
    /// </summary>
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(10);

    private readonly TestConsole _console = new TestConsole().Width(100).Height(32).Interactive();
    private readonly SetupContext _context = new();
    private readonly SetupScreen _screen = new(0, ["Sound"], CanGoBack: true);

    [Fact]
    public async Task MeasureSilence_DeviceLeftOnTheListWhileStillOpening_IsOpenedAgainOnlyOnceThatOneIsClosed()
    {
        // Arrange: the first opening of the device takes its time
        using var holding = new ManualResetEventSlim();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int openings = 0;

        var firstSession = new Mock<IMicrophoneSession>();
        firstSession.Setup(session => session.Dispose()).Callback(() => firstClosed.TrySetResult());

        Mock<IMicrophoneProbe> probe = Probe();
        probe.Setup(p => p.Open(0)).Returns(() =>
        {
            if (Interlocked.Increment(ref openings) == 1)
            {
                firstEntered.TrySetResult();
                holding.Wait(_patience);

                return firstSession.Object;
            }

            // Whether the first opening is closed by the time the device is opened again
            secondEntered.TrySetResult(firstClosed.Task.IsCompleted);

            return Mock.Of<IMicrophoneSession>();
        });

        CheckStep step = Step(probe, TimeProvider.System);

        await LeaveTheListAtOnceAsync(step);
        await firstEntered.Task.WaitAsync(_patience);

        // Act: the silence level is measured at once
        using var leaving = new CancellationTokenSource();

        _console.Input.PushKey(ConsoleKey.Enter);
        Task measuring = step.Pages[1].ShowAsync(_context, _screen, leaving.Token);

        bool isOpenedTwiceAtOnce = await Task.WhenAny(secondEntered.Task, Task.Delay(TimeSpan.FromMilliseconds(200))) == secondEntered.Task;
        holding.Set();

        // Assert: two openings of one device at once may find it busy
        Assert.False(isOpenedTwiceAtOnce);
        Assert.True(await secondEntered.Task.WaitAsync(_patience));

        await LeaveAsync(measuring, leaving);
    }

    [Fact]
    public async Task MeasureSilence_DeviceDidNotAnswerAndIsMeasuredAgain_OpensItAnew()
    {
        // Arrange: the first opening never ends, as with a device another program holds
        using var holding = new ManualResetEventSlim();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int openings = 0;

        Mock<IMicrophoneProbe> probe = Probe();
        probe.Setup(p => p.Open(0)).Returns(() =>
        {
            if (Interlocked.Increment(ref openings) == 1)
            {
                firstEntered.TrySetResult();
                holding.Wait(_patience);
            }
            else
            {
                secondEntered.TrySetResult();
            }

            return Mock.Of<IMicrophoneSession>();
        });

        var clock = new StoppedClock();
        CheckStep step = Step(probe, clock);

        using (var first = new CancellationTokenSource())
        {
            _console.Input.PushKey(ConsoleKey.Enter);
            Task measuring = step.Pages[1].ShowAsync(_context, _screen, first.Token);

            await firstEntered.Task.WaitAsync(_patience);

            // The time a device may take runs out, and the page says the device did not answer
            clock.Expire();
            await clock.LookedAtAfterExpiring.Task.WaitAsync(_patience);

            await LeaveAsync(measuring, first);
        }

        try
        {
            // Act: the user measures again, as after closing the program that held the device
            using var second = new CancellationTokenSource();

            _console.Input.PushKey(ConsoleKey.Enter);
            Task measuring = step.Pages[1].ShowAsync(_context, _screen, second.Token);

            // Assert: the opening that never ended is not waited for again
            await secondEntered.Task.WaitAsync(_patience);

            await LeaveAsync(measuring, second);
        }
        finally
        {
            holding.Set();
        }
    }

    [Fact]
    public async Task TestDictation_DeviceLeftOnTheListWhileStillOpening_StartsOnlyOnceItIsClosed()
    {
        // Arrange: the opening of the device on the list takes its time
        using var holding = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var session = new Mock<IMicrophoneSession>();
        session.Setup(s => s.Dispose()).Callback(() => closed.TrySetResult());

        Mock<IMicrophoneProbe> probe = Probe();
        probe.Setup(p => p.Open(0)).Returns(() =>
        {
            entered.TrySetResult();
            holding.Wait(_patience);

            return session.Object;
        });

        // The dictation opens the device on its own; whether the one on the list is closed by then
        var tester = new Mock<IDictationTester>();
        tester.Setup(t => t.StartAsync(It.IsAny<SetupContext>(), It.IsAny<CancellationToken>())).Returns(() =>
        {
            started.TrySetResult(closed.Task.IsCompleted);

            return new TaskCompletionSource<IDictationTestSession>().Task;
        });

        CheckStep step = Step(probe, TimeProvider.System, tester.Object);

        await LeaveTheListAtOnceAsync(step);
        await entered.Task.WaitAsync(_patience);

        // Act: the silence level is skipped, so the dictation comes right after the list
        using var leaving = new CancellationTokenSource();
        Task testing = step.Pages[2].ShowAsync(_context, _screen, leaving.Token);

        bool isStartedTooEarly = await Task.WhenAny(started.Task, Task.Delay(TimeSpan.FromMilliseconds(200))) == started.Task;
        holding.Set();

        // Assert
        Assert.False(isStartedTooEarly);
        Assert.True(await started.Task.WaitAsync(_patience));

        await LeaveAsync(testing, leaving);
    }

    private static Mock<IMicrophoneProbe> Probe()
    {
        var probe = new Mock<IMicrophoneProbe>();
        probe.Setup(p => p.GetDevices()).Returns([new MicrophoneDevice(0, "Desk microphone")]);

        return probe;
    }

    private CheckStep Step(Mock<IMicrophoneProbe> probe, TimeProvider time, IDictationTester? tester = null) =>
        new(new SetupUi(_console, Theme.Ascii, TimeSpan.FromMilliseconds(1)),
            probe.Object,
            tester ?? Mock.Of<IDictationTester>(),
            SetupTimings.Default,
            time);

    /// <summary>
    ///     Takes the device from the list before it has even opened.
    /// </summary>
    private async Task LeaveTheListAtOnceAsync(CheckStep step)
    {
        _console.Input.PushKey(ConsoleKey.Enter);

        await step.Pages[0].ShowAsync(_context, _screen, CancellationToken.None).WaitAsync(_patience);
    }

    /// <summary>
    ///     Leaves a page that would listen on, as the wizard does when it is closed.
    /// </summary>
    private static async Task LeaveAsync(Task page, CancellationTokenSource leaving)
    {
        leaving.Cancel();

        await page.WaitAsync(_patience).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    /// <summary>
    ///     A clock that stands still until every wait on it runs out at once, and says when it is looked at after that.
    /// </summary>
    private sealed class StoppedClock : TimeProvider
    {
        private long _ticks;

        public TaskCompletionSource LookedAtAfterExpiring { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            long ticks = Interlocked.Read(ref _ticks);

            if (ticks > 0)
                LookedAtAfterExpiring.TrySetResult();

            return ticks;
        }

        public void Expire() =>
            Interlocked.Exchange(ref _ticks, TimeSpan.FromHours(1).Ticks);
    }
}