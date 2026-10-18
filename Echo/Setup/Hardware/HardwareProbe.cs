using System.Globalization;

namespace Echo.Setup.Hardware;

/// <inheritdoc cref="IHardwareProbe"/>
/// <param name="processRunner"><see cref="IProcessRunner"/></param>
/// <param name="vulkan">Whether Vulkan can be used on this system.</param>
internal sealed class HardwareProbe(IProcessRunner processRunner, IVulkanSupport vulkan) : IHardwareProbe
{
    private const string NvidiaTool = "nvidia-smi";
    private const string QueryWithCapability = "--query-gpu=index,name,memory.total,driver_version,compute_cap --format=csv,noheader,nounits";
    private const string QueryWithoutCapability = "--query-gpu=index,name,memory.total,driver_version --format=csv,noheader,nounits";

    private static readonly TimeSpan _toolTimeout = TimeSpan.FromSeconds(5);

    private readonly IProcessRunner _processRunner = processRunner;
    private readonly IVulkanSupport _vulkan = vulkan;

    /// <inheritdoc/>
    public async Task<HardwareInfo> DetectAsync(CancellationToken cancellationToken)
    {
        // Older drivers do not know the compute capability field and reject the whole query
        IReadOnlyList<NvidiaGpu> cards =
            ParseNvidiaGpus(await _processRunner.RunAsync(NvidiaTool, QueryWithCapability, _toolTimeout, cancellationToken), hasCapability: true)
            is { Count: > 0 } modern
                ? modern
                : ParseNvidiaGpus(await _processRunner.RunAsync(NvidiaTool, QueryWithoutCapability, _toolTimeout, cancellationToken), hasCapability: false);

        long memoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        VulkanStatus vulkan = _vulkan.Check();

        // Until the user says otherwise, the card with the most memory is the one to use
        return new HardwareInfo(
            cards.OrderByDescending(card => card.MemoryMb).ThenBy(card => card.Index).FirstOrDefault(),
            vulkan == VulkanStatus.Available,
            (int)Math.Round(memoryBytes / (1024d * 1024 * 1024)),
            Environment.ProcessorCount)
        {
            NvidiaCards = cards,
            IsVulkanChosenAutomatically = _vulkan.IsChosenAutomatically,
            Vulkan = vulkan
        };
    }

    /// <summary>
    ///     Parses the output of the NVIDIA tool, one card per line:
    ///     "index, name, memory in MB, driver version[, compute capability]".
    /// </summary>
    internal static IReadOnlyList<NvidiaGpu> ParseNvidiaGpus(string? output, bool hasCapability)
    {
        var cards = new List<NvidiaGpu>();

        foreach (string line in (output ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] fields = line.Split(',', StringSplitOptions.TrimEntries);
            int tail = hasCapability ? 3 : 2;

            // The name is everything between the index and the numbers at the end, commas included
            if (fields.Length < tail + 2
                ||
                !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
                ||
                !int.TryParse(fields[^tail], NumberStyles.Integer, CultureInfo.InvariantCulture, out int memoryMb)
                ||
                !Version.TryParse(fields[^(tail - 1)], out Version? driverVersion))
            {
                continue;
            }

            Version? computeCapability = hasCapability && Version.TryParse(fields[^1], out Version? parsed) ? parsed : null;

            cards.Add(new NvidiaGpu(string.Join(", ", fields[1..^tail]), memoryMb, driverVersion, computeCapability, index));
        }

        return cards;
    }
}