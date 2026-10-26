namespace PetShopWeb;

// Uses a type that only the private NuGet feed serves, so the project cannot
// build unless that package restored.
public static class PrivateFeedCheck
{
    public static string Source => Vutk.LiveCheck.Private.PrivateFeedMarker.Source;
}
