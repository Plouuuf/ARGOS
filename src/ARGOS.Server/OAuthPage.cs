using System.Net;

namespace Argos.Server;

/// <summary>Page affichée dans le navigateur à la fin de la connexion Discord.</summary>
internal static class OAuthPage
{
    public static string Build(string title, string message, bool success, string? displayName)
    {
        var safeTitle = WebUtility.HtmlEncode(title);
        var safeMessage = WebUtility.HtmlEncode(message);
        var identity = string.IsNullOrWhiteSpace(displayName)
            ? string.Empty
            : $"<p class=\"who\">Compte Discord : <strong>{WebUtility.HtmlEncode(displayName)}</strong></p>";
        var stateClass = success ? "ok" : "ko";
        var note = success
            ? "Tu peux fermer cette page et revenir dans ARGOS : la session s'ouvre automatiquement."
            : "Tu peux fermer cette page et revenir dans ARGOS.";
        var closeScript = success
            ? "<script>setTimeout(function(){try{window.open('','_self');window.close();}catch(e){}},1500);</script>"
            : string.Empty;

        return $$"""
<!doctype html>
<html lang="fr">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>ARGOS · {{safeTitle}}</title>
<style>
  :root { color-scheme: dark; }
  * { box-sizing: border-box; }
  body { margin: 0; min-height: 100vh; display: grid; place-items: center; padding: 16px;
         background: #0b0f14; color: #f2f4f7; font: 15px/1.5 "Segoe UI", -apple-system, Helvetica, Arial, sans-serif; }
  .card { width: 100%; max-width: 440px; background: #151b23; border: 1px solid #263140; border-radius: 6px; padding: 28px; }
  .brand { font-weight: 700; letter-spacing: .18em; font-size: 13px; color: #c9d1db; }
  .brand span { color: #e1251b; }
  .state { margin: 18px 0 6px; font-size: 12px; font-weight: 700; letter-spacing: .08em; }
  .ok .state { color: #3fb950; }
  .ko .state { color: #f85149; }
  h1 { margin: 0 0 8px; font-size: 22px; font-weight: 600; }
  p { margin: 0 0 8px; color: #c9d1db; }
  .who { color: #f2f4f7; }
  .note { margin-top: 16px; font-size: 13px; color: #8b97a6; }
</style>
</head>
<body>
  <main class="card {{stateClass}}">
    <div class="brand"><span>■</span> ARGOS · BSPP · VDMS</div>
    <div class="state">{{(success ? "CONNEXION AUTORISÉE" : "CONNEXION REFUSÉE")}}</div>
    <h1>{{safeTitle}}</h1>
    <p>{{safeMessage}}</p>
    {{identity}}
    <p class="note">{{note}}</p>
  </main>
  {{closeScript}}
</body>
</html>
""";
    }
}
