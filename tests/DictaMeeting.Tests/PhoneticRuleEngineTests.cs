using System.Linq;
using DictaMeeting.Meetings.Vocabulary;
using Xunit;

namespace DictaMeeting.Tests;

public class PhoneticRuleEngineTests
{
    [Theory]
    [InlineData("Aritz", new[] { "Arich", "Ariz" })]
    public void GenerateSuggestedAliases_EuskeraNames_GeneratesExpectedPhonetics(string word, string[] expectedVariants)
    {
        var suggestions = PhoneticRuleEngine.GenerateSuggestedAliases(word);

        Assert.NotEmpty(suggestions);
        foreach (var expected in expectedVariants)
        {
            Assert.Contains(suggestions, s => s.Equals(expected, System.StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void GenerateSuggestedAliases_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(PhoneticRuleEngine.GenerateSuggestedAliases(null));
        Assert.Empty(PhoneticRuleEngine.GenerateSuggestedAliases(""));
        Assert.Empty(PhoneticRuleEngine.GenerateSuggestedAliases("   "));
    }

    [Fact]
    public void GenerateSuggestedAliases_DoesNotIncludeOriginalWord()
    {
        var suggestions = PhoneticRuleEngine.GenerateSuggestedAliases("Aritz");
        Assert.DoesNotContain("Aritz", suggestions, System.StringComparer.OrdinalIgnoreCase);
    }
}
