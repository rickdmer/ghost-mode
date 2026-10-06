namespace GhostMode
{
    /// <summary>
    /// Every app Ghost Mode knows about, in display order. To support another app, write an IPresenceProvider
    /// (see Providers/) and add it here; the app list, switches and toggling all pick it up automatically.
    /// </summary>
    public static class ProviderRegistry
    {
        public static IPresenceProvider[] All()
        {
            return new IPresenceProvider[]
            {
                new DiscordProvider(),
                new SteamProvider(),
                new XboxProvider(),
                new BattleNetProvider(),
                new GogGalaxyProvider(),
            };
        }
    }
}
