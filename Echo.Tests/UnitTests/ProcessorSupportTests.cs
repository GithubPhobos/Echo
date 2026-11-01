namespace Echo.Tests.UnitTests;

public sealed class ProcessorSupportTests
{
    [Fact]
    public void Missing_EveryInstructionThere_IsEmpty()
    {
        // Act & Assert
        Assert.Empty(ProcessorSupport.Missing(hasAvx: true, hasAvx2: true, hasFma: true, hasF16c: true));
    }

    [Fact]
    public void Missing_OlderProcessor_NamesWhatItLacks()
    {
        // Act: a processor of 2011, with AVX but none of what came later
        IReadOnlyList<string> missing = ProcessorSupport.Missing(hasAvx: true, hasAvx2: false, hasFma: false, hasF16c: false);

        // Assert
        Assert.Equal(["AVX2", "FMA", "F16C"], missing);
    }

    [Fact]
    public void MissingInstructions_TheMachineTheTestsRunOn_CanRunRecognition()
    {
        // Act & Assert: every machine Echo is built and tested on runs recognition
        Assert.Empty(ProcessorSupport.MissingInstructions());
    }
}