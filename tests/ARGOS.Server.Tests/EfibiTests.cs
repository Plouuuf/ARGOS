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

    [Fact]
    public void Comme_CORTEX_auteur_circonstances_et_cases_cochees()
    {
        const string json = """
        {"sections":{
           "circulatoryForm":{"title":"BILAN CIRCULATOIRE","controls":[
               {"index":0,"label":"FC","type":"number","value":"80","id":"fc"},
               {"index":1,"label":"Pâleurs","type":"checkbox","value":true,"checked":true},
               {"index":2,"label":"Sueurs","type":"checkbox","value":false,"checked":false},
               {"index":3,"label":"< 2 s","type":"radio","value":"inf2","checked":true},
               {"index":4,"label":"≥ 2 s","type":"radio","value":"sup2","checked":false}],"lists":[],"choices":[]},
           "circonstancielForm":{"title":"BILAN CIRCONSTANCIEL","controls":[
               {"index":0,"label":"Chef d'agrès","type":"text","value":"SGT TEST"},
               {"index":1,"label":"Observations","type":"textarea","value":"Chute de sa hauteur"}],"lists":[],"choices":[]},
           "regulationForm":{"title":"CONTACT RÉGULATION","controls":[{"index":0,"label":"","type":"text","value":"","id":"regTime"}],
             "lists":{"regTimeline":[]},"choices":[]}
         }}
        """;
        Assert.Equal("SGT TEST", EfibiPayload.ExtractControl(json, "Chef d'agrès"));
        Assert.Equal("Chute de sa hauteur", EfibiPayload.ExtractControl(json, "Observations"));
        var record = new EfibiRecord();
        EfibiPayload.Parse(json, record);
        Assert.Equal("BILAN CIRCONSTANCIEL", record.Sections[0].Title);    // même ordre que CORTEX
        var circ = record.Sections.Single(x => x.Key == "circulatoryForm");
        Assert.Equal("Oui", circ.Fields.Single(f => f.Label == "Pâleurs").Value);
        Assert.Equal("Oui", circ.Fields.Single(f => f.Label == "< 2 s").Value);   // radio cochée (champ « checked »)
        Assert.DoesNotContain(circ.Fields, f => f.Label is "Sueurs" or "≥ 2 s");
        var reg = record.Sections.Single(x => x.Key == "regulationForm");
        Assert.Empty(reg.Fields);                                          // champ technique vide masqué
        Assert.Empty(reg.Lists);                                           // liste vide masquée
        Assert.Equal("#178447", EfibiText.StatusColor("Transmis"));
    }
}
