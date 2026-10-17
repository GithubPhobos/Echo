using Echo.Setup;

namespace Echo.Tests.UnitTests;

public sealed class AdditionalValuesTests
{
    [Fact]
    public void ValueSetBackToTheShippedOne_IsNoLongerKept()
    {
        // Arrange
        var values = new AdditionalValues();

        // Act
        values.Switch(AdditionalSettings.PlaySound);
        bool isOffAfterFirstSwitch = !values.IsOn(AdditionalSettings.PlaySound);

        values.Switch(AdditionalSettings.PlaySound);
        values.Set(AdditionalSettings.Threads, "8");
        values.Set(AdditionalSettings.Threads, null);

        // Assert
        Assert.True(isOffAfterFirstSwitch);
        Assert.True(values.IsOn(AdditionalSettings.PlaySound));
        Assert.Empty(values.Changed);
        Assert.Equal(new AdditionalValues(), values);
    }

    [Fact]
    public void Step_GoesThroughTheValuesWorthTryingAndRoundAgain()
    {
        // Arrange
        var values = new AdditionalValues();

        // Act & Assert: a setting that ships without a value passes through "no value" on the way round
        var temperatures = new List<string?>();

        for (int press = 0; press < 8; press++)
        {
            values.Step(AdditionalSettings.Temperature);
            temperatures.Add(values.Get(AdditionalSettings.Temperature));
        }

        Assert.Equal(["0", "0.2", "0.4", "0.6", "0.8", "1", null, "0"], temperatures);

        // One that ships with a value comes round to it, and is then no longer kept
        var buffers = new List<string?>();

        for (int press = 0; press < 5; press++)
        {
            values.Step(AdditionalSettings.BufferMs);
            buffers.Add(values.Get(AdditionalSettings.BufferMs));
        }

        Assert.Equal(["100", "200", "20", "30", "50"], buffers);
        Assert.Null(values.Stored(AdditionalSettings.BufferMs));

        // A number typed between two steps moves on to the higher one
        values.Set(AdditionalSettings.Threads, "7");
        values.Step(AdditionalSettings.Threads);

        Assert.Equal("8", values.Get(AdditionalSettings.Threads));

        // A fixed set of values goes round without a gap
        var levels = new List<string?>();

        for (int press = 0; press < 6; press++)
        {
            values.Step(AdditionalSettings.LogLevel);
            levels.Add(values.Get(AdditionalSettings.LogLevel));
        }

        Assert.Equal(["Information", "Warning", "Error", "Fatal", "Verbose", "Debug"], levels);

        // A switch is flipped, and a text has nothing to step through
        values.Step(AdditionalSettings.AutoInsert);
        values.Step(AdditionalSettings.Prompt);

        Assert.False(values.IsOn(AdditionalSettings.AutoInsert));
        Assert.Null(values.Get(AdditionalSettings.Prompt));
    }
}