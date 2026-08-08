internal static class DungeonGenerationPolicy
{
    internal const int DefaultRequestedDanger = 1;
    internal const int VanillaRandomDanger = 0;
    internal const int SearchRadius = 8;

    internal static int ResolveCreationDanger(int requestedDanger)
    {
        return requestedDanger >= 1 ? requestedDanger : VanillaRandomDanger;
    }

    internal static bool CanGenerateAtCurrentArea(
        bool isRegion,
        bool isInsideMoongateWorld,
        bool topZoneExists,
        bool currentZoneHasInstance,
        bool topZoneHasInstance,
        bool currentZoneIsExternal,
        bool topZoneIsExternal)
    {
        if (isInsideMoongateWorld)
            return false;
        if (isRegion)
            return true;
        return topZoneExists &&
               !currentZoneHasInstance &&
               !topZoneHasInstance &&
               !currentZoneIsExternal &&
               !topZoneIsExternal;
    }
}
