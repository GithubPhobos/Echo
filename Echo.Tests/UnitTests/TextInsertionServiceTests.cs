namespace Echo.Tests.UnitTests;

public sealed class TextInsertionServiceTests
{
    [Fact]
    public async Task InsertTextAsync_AutoInsertDisabled_ReturnsImmediately()
    {
        // Arrange
        var settings = new TextInsertionSettings { UseAutoInsert = false };
        var optionsMock = Options.Create(settings);
        var service = new TextInsertionService(optionsMock);

        // Act & Assert
        // We ensure no exception is thrown and it returns immediately.
        // Since we don't mock the static ClipboardService, if it tries to execute, 
        // it might throw an exception in a headless CI environment. 
        // A clean return means our guard clause works.
        var exception = await Record.ExceptionAsync(() => service.InsertTextAsync("Some text"));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task InsertTextAsync_EmptyOrNullText_ReturnsImmediately(string? invalidText)
    {
        // Arrange
        var settings = new TextInsertionSettings { UseAutoInsert = true };
        var optionsMock = Options.Create(settings);
        var service = new TextInsertionService(optionsMock);

        // Act & Assert
        var exception = await Record.ExceptionAsync(() => service.InsertTextAsync(invalidText));

        Assert.Null(exception);
    }
}