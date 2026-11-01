namespace Echo.Setup.Models;

/// <summary>
///     The models offered by the setup wizard.
/// </summary>
internal static class ModelCatalog
{
    public const string Tiny = "tiny";
    public const string Base = "base";
    public const string Small = "small";
    public const string Medium = "medium";
    public const string LargeTurbo = "large-v3-turbo";
    public const string Large = "large-v3";

    /// <summary>
    ///     Sizes and checksums are those of the files published in the whisper.cpp model repository.
    /// </summary>
    public static readonly IReadOnlyList<ModelInfo> All =
    [
        new("tiny", 77_691_713, "be07e048e1e599ad46341c8d2a135645097a538221678b7acdd1b1919c6e1b21", Tiny, false),
        new("tiny.en", 77_704_715, "921e4cf8686fdd993dcd081a5da5b6c365bfde1162e72b08d75ac75289920b1f", Tiny, true),
        new("base", 147_951_465, "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe", Base, false),
        new("base.en", 147_964_211, "a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002", Base, true),
        new("small", 487_601_967, "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b", Small, false),
        new("small.en", 487_614_201, "c6138d6d58ecc8322097e0f987c32f1be8bb0a18532a3f88f734d1bbf9c41e5d", Small, true),
        new("medium", 1_533_763_059, "6c14d5adee5f86394037b4e4e8b59f1673b6cee10e3cf0b11bbdbee79c156208", Medium, false),
        new("medium.en", 1_533_774_781, "cc37e93478338ec7700281a7ac30a10128929eb8f427dda2e865faa8f6da4356", Medium, true),
        new("large-v3-turbo", 1_624_555_275, "1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69", LargeTurbo, false),
        new("large-v3", 3_095_033_483, "64d182b440b98d5203c4f9bd541544d84c605196c4f7b845dfa11fb23594d1e2", Large, false),
    ];

    /// <summary>
    ///     The models that make sense for the chosen dictation language: one per tier, smallest first.
    ///     English-only models are faster and more accurate, but only when nothing else is ever dictated.
    /// </summary>
    public static IReadOnlyList<ModelInfo> For(bool englishOnly) =>
        All.GroupBy(model => model.Tier)
            .Select(tier => tier.FirstOrDefault(model => model.IsEnglishOnly == englishOnly) ?? tier.First())
            .ToList();

    /// <summary>
    ///     Whether the model is offered but advised against for any machine: tiny and base make far more mistakes
    ///     than small outside English, and a modern computer runs small fast enough. Echo is not tuned for weak ones.
    /// </summary>
    public static bool IsNotRecommended(ModelInfo model) =>
        model.Tier is Tiny or Base;
}