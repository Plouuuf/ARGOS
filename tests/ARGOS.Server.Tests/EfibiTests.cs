using Argos.Server.Services;
using Argos.Shared.Contracts;
using Xunit;

namespace Argos.Server.Tests;

/// <summary>Lecture du formulaire eFiBi (structure du script FiveM), sans base de données.</summary>
public sealed class EfibiTests
{
    [Fact]
    public void Lit_les_rubriques_du_formulaire()
    {
        const string json = """
        {"schemaVersion":1,
         "metadata":{"interventionNumber":"210-593","transmissionStatus":"Transmis","victimName":"Test"},
         "sections":{
           "transmissionForm":{"title":"HORODATAGE / TRANSMISSION","controls":[{"index":0,"label":"Heure :","type":"text","value":"14:05","id":"h"}],"lists":[],"choices":[]},
           "identityForm":{"title":"IDENTITÉ","controls":[
               {"index":1,"label":"Prénom","type":"text","value":""},
               {"index":0,"label":"Nom","type":"text","value":"DUPONT"},
               {"index":2,"label":"Homme","type":"radio","value":true},
               {"index":3,"label":"Femme","type":"radio","value":false}],
             "lists":{"allergieList":[{"text":"Pénicilline"}],"maladieList":[]},"choices":[]}
         }}
        """;
        var record = new EfibiRecord();
        EfibiService.ParsePayload(json, record);
        Assert.Null(record.Error);
        Assert.Equal("IDENTITÉ", record.Sections[0].Title);            // ordre d'affichage : identité d'abord
        Assert.Equal("Nom", record.Sections[0].Fields[0].Label);
        Assert.Equal(EfibiText.NonRenseigne, record.Sections[0].Fields[1].Value);
        Assert.Contains(record.Sections[0].Fields, f => f.Label == "Homme");
        Assert.DoesNotContain(record.Sections[0].Fields, f => f.Label == "Femme");
        Assert.Equal(new[] { "Pénicilline" }, record.Sections[0].Lists.Single(l => l.Name == "Allergies").Items);
        Assert.Equal("Heure", record.Sections[1].Fields[0].Label);
        Assert.Equal("210-593", record.Metadata["interventionNumber"]);
    }
}
