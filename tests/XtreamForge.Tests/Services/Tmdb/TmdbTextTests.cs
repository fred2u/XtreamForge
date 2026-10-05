using XtreamForge.ApiService.Services.Tmdb;

namespace XtreamForge.Tests.Services.Tmdb;

public class TmdbTextTests
{
    [Theory]
    [InlineData("|FR| The Lattice (1999) 4K", "The Lattice")]
    [InlineData("[VOSTFR] Somnolence (2010)", "Somnolence")]
    [InlineData("Somnolence 2010", "Somnolence 2010")]
    [InlineData("Iron Courier 2049 (2017)", "Iron Courier 2049")]
    [InlineData("1958 (2019) MULTI", "1958")]
    [InlineData("Les Égarés - HD", "Les Égarés")]
    [InlineData("FR ★ Notre océan a ses secrets L'appel des profondeurs - 2021", "Notre océan a ses secrets L'appel des profondeurs")]
    [InlineData("  ", "")]
    [InlineData(null, "")]
    public void CleanTitle_RemovesTagsAndYear(string? rawTitle, string expectedTitle)
    {
        Assert.Equal(expectedTitle, Cleaners.CleanTitle(rawTitle));
    }

    [Theory]
    [InlineData("|FR| Driftwood 2019 FHD MULTI", "Driftwood")]
    [InlineData("|FR| Maple's Remedy", "Maple's Remedy")]
    [InlineData("|FR| The Long Detour 2016 FHD MULTI", "The Long Detour")]
    [InlineData("|FR| Stories from the Ridge 2020 HD", "Stories from the Ridge")]
    [InlineData("|FR| HARBOR SEVEN SEVEN 2013 FHD MULTI", "HARBOR SEVEN SEVEN")]
    [InlineData("|FR| Kind Surgeon (2017) FHD MULTI", "Kind Surgeon")]
    [InlineData("|FR| Wren 2019 FHD MULTI (H.265)", "Wren")]
    [InlineData("|FR| Port Solana 2003 FHD MULTI (H.265)", "Port Solana")]
    [InlineData("|FR| La Gardienne pourpre (The Keeper s Lantern) 2017 FHD MULTI", "La Gardienne pourpre (The Keeper s Lantern)")]
    [InlineData("|FR| La Rose des Sables (The Rose of Sand) 2021 HD", "La Rose des Sables (The Rose of Sand)")]
    [InlineData("|FR| Esprits rebelles (Rebel Minds) 2005 HD", "Esprits rebelles (Rebel Minds)")]
    [InlineData("|FR| Le retour des Morel (Back to the Morels) 2021 HD", "Le retour des Morel (Back to the Morels)")]
    [InlineData("|FR| Les Guetteurs (The Lookouts) 2013 FHD MULTI (H.265)", "Les Guetteurs (The Lookouts)")]
    [InlineData("|FR| Contes Insolites (Curious Tales) 2020 FHD MULTI", "Contes Insolites (Curious Tales)")]
    [InlineData("|FR| Steel Chapter IV Rising 2022 HD", "Steel Chapter IV Rising")]
    [InlineData("|FR| RallyX Unbound 2022 FHD MULTI", "RallyX Unbound")]
    [InlineData("|FR| Famous Runaway Traque à l'aveugle 2021 FHD", "Famous Runaway Traque à l'aveugle")]
    [InlineData("|FR| OMG Qui baille perd 2021 FHD", "OMG Qui baille perd")]
    [InlineData("|FR| Vita da Pietro 2021 HD", "Vita da Pietro")]
    [InlineData("|FR| 4-0-4: Nevada (4-0-4 Silver State) 2020 FHD MULTI", "4-0-4: Nevada (4-0-4 Silver State)")]
    [InlineData("|FR| On l'appelait le Faucon des Bois FHD (2026)", "On l'appelait le Faucon des Bois")]
    [InlineData("|FR| Évaporés (2025) HEVC", "Évaporés")]
    [InlineData("|FR| Le Chiffre 41 (2007 FHD MULTI", "Le Chiffre 41")]
    [InlineData("|FR|  1974  (2024) SD", "1974")]
    [InlineData("|FR| Les hommes aux valises vertes | 1986", "Les hommes aux valises vertes")]
    [InlineData("|FR| 1958", "1958")]
    [InlineData("|FR| 1958 (2020", "1958")]
    [InlineData("|FR| Iron 2049", "Iron 2049")]
    [InlineData("|FR| Spooky Tale 2", "Spooky Tale 2")]
    public void CleanTitle_Should_Return_Valid_Title_When_Data_Are_Cleanable(string title, string expected)
    {
        var result = Cleaners.CleanTitle(title);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CleanTitle_Should_Return_StringEmpty_When_Data_Is_Not_Cleanable(string? title)
    {
        var result = Cleaners.CleanTitle(title);
        Assert.Empty(result);
    }

    [Fact]
    public void CleanTitle_Should_Remove_Quality_And_Language_Tags()
    {
        var title = "|FR| Test Movie FHD MULTI VOSTFR (2022)";
        var result = Cleaners.CleanTitle(title);
        Assert.Equal("Test Movie", result);
    }

    [Fact]
    public void CleanTitle_Should_Normalize_Spaces()
    {
        var title = "|FR|   Test    Movie   (2023)   ";
        var result = Cleaners.CleanTitle(title);
        Assert.Equal("Test Movie", result);
    }

    [Fact]
    public void CleanTitle_Should_Not_Remove_Valid_Number_In_Title()
    {
        var title = "|FR| 1974 (2024)";
        var result = Cleaners.CleanTitle(title);
        Assert.Equal("1974", result);
    }

    [Fact]
    public void CleanTitle_Should_Handle_Broken_Parentheses()
    {
        var title = "|FR| Movie Title (2021 FHD";
        var result = Cleaners.CleanTitle(title);
        Assert.Equal("Movie Title", result);
    }

    [Fact]
    public void CleanTitle_Should_Not_Confuse_Year_And_Title()
    {
        var title = "|FR| Patrol 2020";
        var result = Cleaners.CleanTitle(title);
        Assert.Equal("Patrol 2020", result);
    }

    [Theory]
    [InlineData("Les Égarés", "les egares")]
    [InlineData("Swift & Daring", "swift daring")]
    [InlineData("Swift and Daring", "swift daring")]
    [InlineData("Sci-Fi & Fantasy", "sci fi fantasy")]
    [InlineData("  Moth-Girl:  Far From Shore ", "moth girl far from shore")]
    [InlineData(null, "")]
    public void Normalize_MakesTextsComparable(string? value, string expected)
    {
        Assert.Equal(expected, TmdbText.Normalize(value));
    }

    [Theory]
    [InlineData("lattice", "lattice", 1)]
    [InlineData("lattice", "", 0)]
    [InlineData("abcd", "abce", 0.75)]
    public void Similarity_IsBetweenZeroAndOne(string first, string second, double expected)
    {
        Assert.Equal(expected, TmdbText.Similarity(first, second), 3);
    }

    [Theory]
    [InlineData("2011-04-17", 2011, 4, 17)]
    [InlineData("2011-04-17 20:00:00", 2011, 4, 17)]
    public void ParseDate_AcceptsDateAndDateTime(string value, int year, int month, int day)
    {
        Assert.Equal(new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Unspecified), TmdbText.ParseDate(value)?.Date);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a date")]
    public void ParseDate_WhenInvalid_ReturnsNull(string value)
    {
        Assert.Null(TmdbText.ParseDate(value));
    }
}
