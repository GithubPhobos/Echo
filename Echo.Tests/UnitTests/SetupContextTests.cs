using Echo.Setup;

namespace Echo.Tests.UnitTests;

public sealed class SetupContextTests
{
    [Fact]
    public void RoundTripThroughSettings_KeepsEveryChoice()
    {
        // Arrange
        var settings = new UserSettings
        {
            SetupVersion = 1,
            UiLanguage = "de",
            DictationLanguage = "auto",
            Additional = new AdditionalValues()
                .With(AdditionalSettings.Translate, AdditionalSettings.On)
                .With(AdditionalSettings.Prompt, "Kubernetes, gRPC")
                .With(AdditionalSettings.Threads, "8")
                .With(AdditionalSettings.Volume, "0.35")
                .With(AdditionalSettings.PlaySound, AdditionalSettings.Off),
            HardwareBackend = "Cpu",
            GraphicsCardIndex = 2,
            ModelName = "ggml-tiny.bin",
            HotkeyKey = "VcRightAlt",
            HotkeyModifier = string.Empty,
            MicrophoneIndex = 1,
            MicrophoneName = "Headset",
            SilenceThreshold = 0.03f,
        };

        // Act & Assert
        Assert.Equal(settings, SetupContext.FromSettings(settings).ToSettings(setupVersion: 1));
    }
}