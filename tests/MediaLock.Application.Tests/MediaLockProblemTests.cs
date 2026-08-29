using MediaLock.Application;
using Xunit;

namespace MediaLock.Application.Tests;

public sealed class MediaLockProblemTests
{
    [Fact]
    public void EveryKnownProblemHasAUniqueStablePublicCode()
    {
        var definitions = MediaLockProblemCatalog.Definitions;

        Assert.Equal(Enum.GetValues<MediaLockProblemId>().Length, definitions.Length);
        Assert.Equal(
            definitions.Length,
            definitions.Select(definition => definition.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.All(definitions, definition =>
            Assert.Matches("^ML-[A-Z]+-[0-9]{3}$", definition.Code));
        Assert.Contains(definitions, definition =>
            definition.Id == MediaLockProblemId.Unknown && definition.Code == "ML-APP-000");
        Assert.All(
            definitions.Where(definition => definition.Code.StartsWith("ML-BR-", StringComparison.Ordinal)),
            definition => Assert.True(
                int.Parse(definition.Code[^3..], System.Globalization.CultureInfo.InvariantCulture) >= 12,
                $"Desktop Browser code {definition.Code} collides with the Extension range."));
    }

    [Fact]
    public void RepeatedOccurrencesRemainDistinctWithoutChangingTheirPublicCode()
    {
        var first = MediaLockProblem.Error(MediaLockProblemId.CommandFailed);
        var second = MediaLockProblem.Error(MediaLockProblemId.CommandFailed);

        Assert.Equal(first.Code, second.Code);
        Assert.NotEqual(first.OccurrenceId, second.OccurrenceId);
    }

    [Fact]
    public void TechnicalContextRetainsOnlyTheExceptionType()
    {
        var problem = MediaLockProblem.Error(
            MediaLockProblemId.RuntimeStateSaveFailed,
            new IOException("C:\\Users\\private\\state.json could not be saved"));

        Assert.Equal(typeof(IOException).FullName, problem.ExceptionType);
        Assert.DoesNotContain("private", problem.ExceptionType, StringComparison.OrdinalIgnoreCase);
    }
}
