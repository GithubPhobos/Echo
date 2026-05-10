namespace Echo.Settings;

public class WhisperSettings
{
    /// <summary>
    ///     Name of Whisper model/file.
    /// </summary>
    /// <remarks>
    ///     E.g., "ggml-base.bin".
    /// </remarks>
    public string ModelName { get; init; } = null!;

    /// <summary>
    ///     Global hardware backend to use. Options: "Auto", "Cuda", "Vulkan", "Cpu".
    ///     Applied before processor creation.
    /// </summary>
    public string HardwareBackend { get; init; } = null!;

    /// <summary>
    ///     Index of the GPU to use (e.g., 0 for the primary graphics card).
    /// </summary>
    public int GpuDeviceIndex { get; init; }

    /// <summary>
    ///     Number of threads to use when falling back to CPU inference. 
    ///     Default is usually 4. Too many threads can degrade performance.
    /// </summary>
    public int? Threads { get; init; }

    /// <summary>
    ///     Target language for transcription. Use "auto" for automatic detection,
    ///     or standard ISO codes like "en", "ru".
    /// </summary>
    public string Language { get; init; } = null!;

    /// <summary>
    ///     Translate the transcribed text to English automatically.
    /// </summary>
    public bool Translate { get; init; }

    /// <summary>
    ///     Do not use past transcription context to guide the current decoding.
    /// </summary>
    public bool NoContext { get; init; }

    /// <summary>
    ///     Force single segment output rather than splitting across multiple segments.
    /// </summary>
    public bool SingleSegment { get; init; }

    /// <summary>
    ///     Print special tokens (e.g., <|startoftranscript|>, <|en|>) in the output.
    /// </summary>
    public bool PrintSpecialTokens { get; init; }

    /// <summary>
    ///     Print progress to the console during processing.
    /// </summary>
    public bool PrintProgress { get; init; }

    /// <summary>
    ///     Print results to the console as they are generated.
    /// </summary>
    public bool PrintResults { get; init; }

    /// <summary>
    ///     Include timestamps in the printed output.
    /// </summary>
    public bool PrintTimestamps { get; init; }

    /// <summary>
    ///     Enable token-level timestamps.
    /// </summary>
    public bool UseTokenTimestamps { get; init; }

    /// <summary>
    ///     Split text on words rather than tokens (requires UseTokenTimestamps = true).
    /// </summary>
    public bool SplitOnWord { get; init; }

    /// <summary>
    ///     Compute the probability for each token and expose it in the results.
    /// </summary>
    public bool ComputeProbabilities { get; init; }

    /// <summary>
    ///     Suppress outputting blank/empty segments. True by default in library.
    ///     Set to false to allow blank outputs.
    /// </summary>
    public bool SuppressBlank { get; init; }

    /// <summary>
    ///     Context prompt to guide the model (e.g., a dictionary of terms to improve accuracy).
    /// </summary>
    public string? Prompt { get; init; }

    /// <summary>
    ///     If true, carries the initial prompt forward into subsequent segments.
    /// </summary>
    public bool CarryInitialPrompt { get; init; }

    /// <summary>
    ///     Regex pattern to suppress specific outputs.
    /// </summary>
    public string? SuppressRegex { get; init; }

    /// <summary>
    ///     Audio processing offset. Starts transcription from this point in time.
    /// </summary>
    public TimeSpan? Offset { get; init; }

    /// <summary>
    ///     Maximum duration of audio to process.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    ///     Maximum number of text context tokens to store.
    /// </summary>
    public int? MaxLastTextTokens { get; init; }

    /// <summary>
    ///     Threshold for token-level timestamps.
    /// </summary>
    public float? TokenTimestampsThreshold { get; init; }

    /// <summary>
    ///     Sum threshold for token-level timestamps.
    /// </summary>
    public float? TokenTimestampsSumThreshold { get; init; }

    /// <summary>
    ///     Maximum character length of a single segment before splitting.
    /// </summary>
    public int? MaxSegmentLength { get; init; }

    /// <summary>
    ///     Maximum number of tokens per segment.
    /// </summary>
    public int? MaxTokensPerSegment { get; init; }

    /// <summary>
    ///     Audio context size limit.
    /// </summary>
    public int? AudioContextSize { get; init; }

    /// <summary>
    ///     Initial decoding temperature. 0.0 = greedy (most accurate), > 0.0 = more creative.
    /// </summary>
    public float? Temperature { get; init; }

    /// <summary>
    ///     Increment step for temperature fallback if decoding fails.
    /// </summary>
    public float? TemperatureInc { get; init; }

    /// <summary>
    ///     Maximum initial timestamp threshold.
    /// </summary>
    public float? MaxInitialTs { get; init; }

    /// <summary>
    ///     Penalty applied to length in beam search.
    /// </summary>
    public float? LengthPenalty { get; init; }

    /// <summary>
    ///     Threshold to fall back to a higher temperature if entropy is above this value.
    /// </summary>
    public float? EntropyThreshold { get; init; }

    /// <summary>
    ///     Threshold to fall back if the average log probability is below this value.
    /// </summary>
    public float? LogProbThreshold { get; init; }

    /// <summary>
    ///     If the no-speech probability is higher than this, decoding is skipped.
    /// </summary>
    public float? NoSpeechThreshold { get; init; }

    public string? OpenVinoEncoderPath { get; init; }

    public string? OpenVinoDevice { get; init; }

    public string? OpenVinoCacheDir { get; init; }
}