using XtreamForge.ApiService.Services.Tmdb;

namespace XtreamForge.Tests.Services.Tmdb;

public class TmdbTextTests
{
    [Theory]
    [InlineData("|FR| The Matrix (1999) 4K", "The Matrix")]
    [InlineData("[VOSTFR] Inception (2010)", "Inception")]
    [InlineData("Inception 2010", "Inception 2010")]
    [InlineData("Blade Runner 2049 (2017)", "Blade Runner 2049")]
    [InlineData("1917 (2019) MULTI", "1917")]
    [InlineData("Les Évadés - HD", "Les Évadés")]
    [InlineData("FR ★ Notre planète a ses limites L'alerte de la science - 2021", "Notre planète a ses limites L'alerte de la science")]
    [InlineData("  ", "")]
    [InlineData(null, "")]
    public void CleanTitle_RemovesTagsAndYear(string? rawTitle, string expectedTitle)
    {
        Assert.Equal(expectedTitle, Cleaners.CleanTitle(rawTitle));
    }

    [Theory]
    [InlineData("|FR| Undone 2019 FHD MULTI", "Undone")]
    [InlineData("|FR| Grey's Anatomy", "Grey's Anatomy")]
    [InlineData("|FR| The Grand Tour 2016 FHD MULTI", "The Grand Tour")]
    [InlineData("|FR| Tales from the Loop 2020 HD", "Tales from the Loop")]
    [InlineData("|FR| BROOKLYN NINE NINE 2013 FHD MULTI", "BROOKLYN NINE NINE")]
    [InlineData("|FR| Good Doctor (2017) FHD MULTI", "Good Doctor")]
    [InlineData("|FR| Hanna 2019 FHD MULTI (H.265)", "Hanna")]
    [InlineData("|FR| Las Vegas 2003 FHD MULTI (H.265)", "Las Vegas")]
    [InlineData("|FR| La Servante ecarlate (The Handmaid s Tale) 2017 FHD MULTI", "La Servante ecarlate (The Handmaid s Tale)")]
    [InlineData("|FR| La Roue du Temps (The Wheel of Time) 2021 HD", "La Roue du Temps (The Wheel of Time)")]
    [InlineData("|FR| Esprits criminels (Criminal Minds) 2005 HD", "Esprits criminels (Criminal Minds)")]
    [InlineData("|FR| Le retour des Rafter (Back to the Rafters) 2021 HD", "Le retour des Rafter (Back to the Rafters)")]
    [InlineData("|FR| Les Disciples (The Following) 2013 FHD MULTI (H.265)", "Les Disciples (The Following)")]
    [InlineData("|FR| Histoires Fantastiques (Amazing Stories) 2020 FHD MULTI", "Histoires Fantastiques (Amazing Stories)")]
    [InlineData("|FR| Power Book IV Force 2022 HD", "Power Book IV Force")]
    [InlineData("|FR| MotoGP Unlimited 2022 FHD MULTI", "MotoGP Unlimited")]
    [InlineData("|FR| Celebrity Hunted Chasse à l'homme 2021 FHD", "Celebrity Hunted Chasse à l'homme")]
    [InlineData("|FR| LOL Qui rit sort 2021 FHD", "LOL Qui rit sort")]
    [InlineData("|FR| Vita da Carlo 2021 HD", "Vita da Carlo")]
    [InlineData("|FR| 9-1-1: Texas (9-1-1 Lone Star) 2020 FHD MULTI", "9-1-1: Texas (9-1-1 Lone Star)")]
    [InlineData("|FR| On l'appelait Robin des Bois FHD (2026)", "On l'appelait Robin des Bois")]
    [InlineData("|FR| Évanouis (2025) HEVC", "Évanouis")]
    [InlineData("|FR| Le Nombre 23 (2007 FHD MULTI", "Le Nombre 23")]
    [InlineData("|FR|  1992  (2024) SD", "1992")]
    [InlineData("|FR| Les hommes en chaussures bleues | 1986", "Les hommes en chaussures bleues")]
    [InlineData("|FR| 1917", "1917")]
    [InlineData("|FR| 1917 (2020", "1917")]
    [InlineData("|FR| Blade 2049", "Blade 2049")]
    [InlineData("|FR| Scary Movie 2", "Scary Movie 2")]
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
        var title = "|FR| 1992 (2024)";
        var result = Cleaners.CleanTitle(title);
        Assert.Equal("1992", result);
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
        var title = "|FR| Police 2020";
        var result = Cleaners.CleanTitle(title);
        Assert.Equal("Police 2020", result);
    }

    [Theory]
    [InlineData("Les Évadés", "les evades")]
    [InlineData("Fast & Furious", "fast furious")]
    [InlineData("Fast and Furious", "fast furious")]
    [InlineData("Sci-Fi & Fantasy", "sci fi fantasy")]
    [InlineData("  Spider-Man:  Far From Home ", "spider man far from home")]
    [InlineData(null, "")]
    public void Normalize_MakesTextsComparable(string? value, string expected)
    {
        Assert.Equal(expected, TmdbText.Normalize(value));
    }

    [Theory]
    [InlineData("matrix", "matrix", 1)]
    [InlineData("matrix", "", 0)]
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
