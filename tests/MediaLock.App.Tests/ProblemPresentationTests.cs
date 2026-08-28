using System.Globalization;
using MediaLock.App.Localization;
using MediaLock.App.Presentation;
using MediaLock.Application;
using MediaLock.Core.Configuration;
using Xunit;

namespace MediaLock.App.Tests;

[Collection("Localization")]
public sealed class ProblemPresentationTests
{
    [Fact]
    public void EveryKnownProblemHasEnglishAndTraditionalChineseCopy()
    {
        var englishCulture = CultureInfo.GetCultureInfo(UiLanguagePreference.EnglishUnitedStates);
        var traditionalChineseCulture = CultureInfo.GetCultureInfo(
            UiLanguagePreference.TraditionalChinese);
        var englishKeys = UiText.GetExactResourceKeys(englishCulture);
        var traditionalChineseKeys = UiText.GetExactResourceKeys(traditionalChineseCulture);
        var expectedKeys = MediaLockProblemCatalog.Definitions
            .Select(definition => $"Problem_{definition.Id}")
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(expectedKeys.SetEquals(
            englishKeys.Where(key => key.StartsWith("Problem_", StringComparison.Ordinal))));
        Assert.True(expectedKeys.SetEquals(
            traditionalChineseKeys.Where(key => key.StartsWith("Problem_", StringComparison.Ordinal))));
        foreach (var definition in MediaLockProblemCatalog.Definitions)
        {
            var key = $"Problem_{definition.Id}";
            Assert.Contains(key, englishKeys);
            Assert.Contains(key, traditionalChineseKeys);
            var problem = MediaLockProblem.Create(definition.Id, definition.DefaultSeverity);

            var english = ProblemPresentation.Describe(
                problem,
                englishCulture);
            var traditionalChinese = ProblemPresentation.Describe(
                problem,
                traditionalChineseCulture);

            Assert.Contains($"({problem.Code})", english, StringComparison.Ordinal);
            Assert.Contains($"（{problem.Code}）", traditionalChinese, StringComparison.Ordinal);
            Assert.NotEqual(english, traditionalChinese);
        }
    }

    [Fact]
    public void MissingLocaleFallsBackToEnglishWithoutLosingTheStableCode()
    {
        var problem = MediaLockProblem.Error(MediaLockProblemId.RuntimeStateSaveFailed);

        var result = ProblemPresentation.Describe(
            problem,
            CultureInfo.GetCultureInfo("fr-FR"));

        Assert.Contains("runtime state", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ML-CFG-009", result, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownProblemUsesAnIdentifiableLocalizedFallback()
    {
        var problem = MediaLockProblem.Create((MediaLockProblemId)int.MaxValue);

        var english = ProblemPresentation.Describe(
            problem,
            CultureInfo.GetCultureInfo("en-US"));
        var traditionalChinese = ProblemPresentation.Describe(
            problem,
            CultureInfo.GetCultureInfo("zh-TW"));

        Assert.Equal("An unexpected Media Lock error occurred. Try again. (ML-APP-000)", english);
        Assert.Equal("Media Lock 發生未預期的錯誤，請再試一次。（ML-APP-000）", traditionalChinese);
    }

    [Fact]
    public void AppFailureSurfacesUseTheExpectedCodesWithoutRawExceptionMessages()
    {
        const string privateMessage = "private path C:\\Users\\example\\secret.txt";
        var exception = new InvalidOperationException(privateMessage);
        var problems = new[]
        {
            AppProblemFactory.Startup(exception),
            AppProblemFactory.Shutdown(exception),
            AppProblemFactory.MediaInputStartup(exception),
            AppProblemFactory.MediaInputStopped(exception),
        };

        Assert.Equal(
            ["ML-APP-001", "ML-APP-002", "ML-INPUT-001", "ML-INPUT-002"],
            problems.Select(problem => problem.Code));
        foreach (var problem in problems)
        {
            var message = ProblemPresentation.Describe(
                problem,
                CultureInfo.GetCultureInfo("en-US"));

            Assert.Contains(problem.Code, message, StringComparison.Ordinal);
            Assert.DoesNotContain(privateMessage, message, StringComparison.Ordinal);
            Assert.Equal(typeof(InvalidOperationException).FullName, problem.ExceptionType);
        }
    }

    [Fact]
    public void ActiveProblemFollowsAnImmediateLanguageChange()
    {
        var problem = MediaLockProblem.Error(MediaLockProblemId.RuntimeStateSaveFailed);
        UiText.Apply(UiLanguagePreference.EnglishUnitedStates);

        var english = ProblemPresentation.Describe(problem);
        UiText.Apply(UiLanguagePreference.TraditionalChinese);
        var traditionalChinese = ProblemPresentation.Describe(problem);

        Assert.Contains("runtime state", english, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("執行期狀態", traditionalChinese, StringComparison.Ordinal);
        Assert.Contains(problem.Code, english, StringComparison.Ordinal);
        Assert.Contains(problem.Code, traditionalChinese, StringComparison.Ordinal);

        UiText.Apply(UiLanguagePreference.EnglishUnitedStates);
    }
}
