namespace Echo.Setup.Models;

/// <summary>
///     A Whisper model that can be downloaded.
/// </summary>
/// <param name="Id">The model name, e.g. "small" or "small.en".</param>
/// <param name="SizeBytes">The size of the model file.</param>
/// <param name="Sha256">The checksum of the model file.</param>
/// <param name="Tier">The model family without the language suffix, e.g. "small".</param>
/// <param name="IsEnglishOnly">Whether the model understands English only.</param>
internal sealed record ModelInfo(string Id, long SizeBytes, string Sha256, string Tier, bool IsEnglishOnly)
{
    public string FileName => $"ggml-{Id}.bin";

    public Uri DownloadUrl => new($"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{FileName}");
}