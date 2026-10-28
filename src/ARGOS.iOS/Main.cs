using UIKit;

namespace ArgosIos;

public static class Application
{
    // Point d'entrée iOS.
    private static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
