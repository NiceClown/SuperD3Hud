using System;
using Turbo.Plugins;
using Turbo.Plugins.Default;

namespace TurboPlugins
{
    /// <summary>Проверка этапа 2: монстры, предметы, добыча и SNO-каталоги.</summary>
    public class Stage2Demo : BasePlugin, IInGameTopPainter
    {
        /// <summary>Краткое описание для списка плагинов.</summary>
        public string Description
        {
            get { return "Демонстрация этапа 2: монстры, предметы, добыча и SNO-каталоги."; }
        }

        private IFont _font;

        public override void Load(IController hud)
        {
            base.Load(hud);
            if (Hud == null || Hud.Render == null) return;
            _font = Hud.Render.CreateFont("Segoe UI", 11f, 230, 220, 240, 255,
                                         false, false, true);
        }

        public void PaintTopInGame(ClipState clipState)
        {
            if (clipState != ClipState.BeforeClip || Hud == null || Hud.Game == null
                || Hud.Sno == null || Hud.Render == null || _font == null) return;

            int monsterCount = 0;
            IMonster nearest = null;
            double nearestDistance = double.MaxValue;
            if (Hud.Game.Monsters != null)
            {
                foreach (IMonster monster in Hud.Game.Monsters)
                {
                    if (monster == null) continue;
                    monsterCount++;
                    if (monster.CentralXyDistanceToMe < nearestDistance)
                    {
                        nearest = monster;
                        nearestDistance = monster.CentralXyDistanceToMe;
                    }
                }
            }

            int itemCount = 0;
            IItem firstItem = null;
            if (Hud.Game.Items != null)
            {
                foreach (IItem item in Hud.Game.Items)
                {
                    if (item == null) continue;
                    itemCount++;
                    if (firstItem == null) firstItem = item;
                }
            }

            IPlayer me = Hud.Game.Me;
            string player = me == null ? "no player" : "player";
            string rarity = nearest == null ? "none" : nearest.Rarity.ToString();
            string itemText = firstItem == null ? "none" : ((firstItem.FullNameEnglish ?? "")
                + "/" + firstItem.Quality.ToString());

            string itemNames = "";
            if (Hud.Sno.SnoItems != null)
            {
                ISnoItem one = Hud.Sno.SnoItems.Amethyst_03;
                ISnoItem two = Hud.Sno.SnoItems.Amulet01;
                ISnoItem three = Hud.Sno.SnoItems.P4_Unique_Ring_082;
                itemNames = NameOf(one) + "," + NameOf(two) + "," + NameOf(three);
            }

            string powerNames = "";
            if (Hud.Sno.SnoPowers != null)
            {
                ISnoPower one = Hud.Sno.SnoPowers.Barbarian_Bash;
                ISnoPower two = Hud.Sno.SnoPowers.DemonHunter_HungeringArrow;
                powerNames = NameOf(one) + "," + NameOf(two);
            }

            ISnoPower lookedUp = Hud.Sno.GetSnoPower(79242u);
            string lookedUpName = lookedUp == null ? "none" : (lookedUp.NameEnglish ?? "");
            _font.DrawText("Stage2 " + player + " monsters=" + monsterCount
                + " nearest=" + rarity + " items=" + itemCount + " item=" + itemText
                + " sno=" + itemNames + " powers=" + powerNames
                + " lookup=" + lookedUpName, 18f, 92f);
        }

        private static string NameOf(ISnoItem item)
        {
            if (item == null) return "none";
            return item.NameEnglish ?? item.Code ?? "";
        }

        private static string NameOf(ISnoPower power)
        {
            if (power == null) return "none";
            return power.NameEnglish ?? power.Code ?? "";
        }
    }
}
