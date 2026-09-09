namespace HypeSwarm.Shared.Net
{
    /// <summary>What this process should do once the scene loads.</summary>
    public enum NetworkRole
    {
        /// <summary>Decide from the environment: a headless process serves, anything else hosts.</summary>
        Auto = 0,

        /// <summary>Server plus a local player. The normal way a group plays.</summary>
        Host = 1,

        /// <summary>Connect to someone else.</summary>
        Client = 2,

        /// <summary>Server with no local player — a dedicated server (spec §23).</summary>
        Server = 3,

        /// <summary>Start nothing. For opening the scene and looking at it.</summary>
        Offline = 4
    }
}
