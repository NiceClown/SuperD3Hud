using System.Collections.Generic;
using Turbo.Plugins;

namespace SuperD3Hud.TurboHost
{
    /// <summary>
    /// Hand-written members of the generated catalogue classes. The generator
    /// only emits properties; the few methods of the catalogue interfaces are
    /// added here.
    /// </summary>
    public partial class HostSnoPowerList
    {
        public IEnumerable<ISnoPower> GetClassSpecificPowers(HeroClass heroClass)
        {
            return new List<ISnoPower>();
        }

        public IEnumerable<ISnoPower> GetLegendaryGemPowers()
        {
            return new List<ISnoPower>();
        }

        public IEnumerable<ISnoPower> GetLegendaryItemPowers()
        {
            return new List<ISnoPower>();
        }
    }
}
