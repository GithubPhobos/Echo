using Echo.Setup.Audio;

namespace Echo.Tests.UnitTests;

public sealed class ThresholdCalculatorTests
{
    [Fact]
    public void Calculate_QuietRoomAndClearVoice_SetsThresholdJustAboveTheNoise()
    {
        // Arrange
        float[] quiet = [.. Enumerable.Repeat(0.004f, 30)];
        float[] speech = [.. Enumerable.Repeat(0.01f, 10), .. Enumerable.Repeat(0.25f, 30)];

        // Act
        Calibration calibration = ThresholdCalculator.Calculate(quiet, speech);

        // Assert
        Assert.True(calibration.IsVoiceHeard);
        Assert.Equal(0.012f, calibration.Threshold, precision: 4);
    }

    [Fact]
    public void Calculate_NoisyRoomAndQuietVoice_KeepsThresholdBelowTheVoice()
    {
        // Arrange
        float[] quiet = [.. Enumerable.Repeat(0.02f, 30)];
        float[] speech = [.. Enumerable.Repeat(0.08f, 30)];

        // Act
        Calibration calibration = ThresholdCalculator.Calculate(quiet, speech);

        // Assert
        Assert.True(calibration.IsVoiceHeard);
        Assert.InRange(calibration.Threshold, 0.02f, 0.04f);
    }

    [Fact]
    public void Calculate_VoiceNoLouderThanTheRoom_ReportsItWasNotHeard()
    {
        // Arrange
        float[] quiet = [.. Enumerable.Repeat(0.01f, 30)];
        float[] speech = [.. Enumerable.Repeat(0.012f, 30)];

        // Act & Assert
        Assert.False(ThresholdCalculator.Calculate(quiet, speech).IsVoiceHeard);
    }
}