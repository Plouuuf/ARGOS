using Argos.Shared.Contracts;
using Xunit;

namespace Argos.Server.Tests;

public sealed class VdmsMapTests
{
    [Fact]
    public void PlanCharge()
    {
        Assert.Equal(188, VdmsMap.Streets.Count);
        Assert.NotEmpty(VdmsMap.Districts);
        Assert.All(VdmsMap.Streets, s => Assert.NotEmpty(s.Segments));
    }

    [Theory]
    [InlineData("Avenue Bosquet")]
    [InlineData("avenue bosquet")]
    [InlineData("AVENUE BOSQUET - L'ÉCHAT")]
    [InlineData("AVENUE BOSQUET - L’ECHAT")]
    public void RetrouveUneVoie(string saisie)
    {
        var street = VdmsMap.FindStreet(saisie);
        Assert.NotNull(street);
        Assert.Equal("Avenue Bosquet", street!.Name);
        Assert.Equal("L'Échat", street.District);
    }

    [Fact]
    public void RechercheSansAccents()
        => Assert.Contains(VdmsMap.Search("bosq echat"), s => s.Name == "Avenue Bosquet");

    [Fact]
    public void LocaliseUnePositionGta()
    {
        var location = VdmsMap.Locate(new MapPoint(2880.66, 5565.09));
        Assert.Equal("Avenue Bosquet", location.Street?.Name);
        Assert.True(location.DistanceMeters < 1);
    }
}
