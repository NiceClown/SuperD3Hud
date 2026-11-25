using System;
using System.Collections.Generic;
using System.Reflection;
using Turbo.Plugins;
using Turbo.Plugins.Default;

namespace TurboPlugins
{
    /// <summary>Проверка этапа 4: сцена, избегание, цели и запросы.</summary>
    public class Stage4Demo : BasePlugin, IInGameTopPainter
    {
        /// <summary>Краткое описание для списка плагинов.</summary>
        public string Description
        {
            get { return "Демонстрация этапа 4: сцена, избегание, цели и запросы."; }
        }

        private IFont _font;

        public override void Load(IController hud)
        {
            base.Load(hud);
            if (Hud == null || Hud.Render == null) return;
            _font = Hud.Render.CreateFont("Segoe UI", 11f, 220, 240, 220, 255,
                                          false, false, true);
        }

        public void PaintTopInGame(ClipState clipState)
        {
            if (clipState != ClipState.BeforeClip || Hud == null || _font == null) return;

            bool map = false;
            float opacity = 0f;
            if (Hud.SceneReveal != null)
            {
                map = Hud.SceneReveal.MinimapEnabled;
                opacity = Hud.SceneReveal.MapOpacity;
                Hud.SceneReveal.MapOpacity = opacity;
            }

            bool avoidance = Hud.Avoidance != null && Hud.Avoidance.CurrentValue;
            IActorQueryController query = Hud.Game == null ? null : Hud.Game.ActorQuery;
            IMonster elite = query == null ? null : query.NearestElite;
            IMonster boss = query == null ? null : query.NearestBoss;
            int onScreen = query == null ? 0 : query.OnScreenMonsterCount;
            int globes = query == null || query.HealthGlobes == null ? 0 : query.HealthGlobes.Count;

            object target = GetProperty(Hud, "Target");
            IMonster nearestTarget = GetProperty(target, "NearestTarget") as IMonster;
            IMonster currentTarget = GetProperty(target, "CurrentTarget") as IMonster;
            int targetCount = GetIntProperty(target, "TargetCount");

            object inventory = GetProperty(Hud, "InventoryManagement");
            bool working = GetBoolProperty(inventory, "Working");
            object cleanup = GetProperty(inventory, "LastInventoryCleanupOn");
            long cleanupMs = GetLongProperty(cleanup, "ElapsedMilliseconds");

            bool inventoryHotKey = Hud.Interaction != null
                && Hud.Interaction.IsHotKeySet(ActionKey.Inventory);

            _font.DrawText("Stage4 map=" + map + " opacity=" + opacity.ToString("0.00")
                + " avoid=" + avoidance + " elite=" + NameOf(elite)
                + " boss=" + NameOf(boss) + " screen=" + onScreen
                + " globes=" + globes + " target=" + NameOf(nearestTarget)
                + " targets=" + targetCount + " current=" + NameOf(currentTarget)
                + " inv=" + working + "/" + cleanupMs
                + " hotkey=" + inventoryHotKey, 18f, 148f);
        }

        private static object GetProperty(object instance, string name)
        {
            if (instance == null || string.IsNullOrEmpty(name)) return null;
            try
            {
                PropertyInfo property = instance.GetType().GetProperty(name,
                    BindingFlags.Public | BindingFlags.Instance);
                return property == null ? null : property.GetValue(instance, null);
            }
            catch { return null; }
        }

        private static bool GetBoolProperty(object instance, string name)
        {
            object value = GetProperty(instance, name);
            return value is bool && (bool)value;
        }

        private static int GetIntProperty(object instance, string name)
        {
            object value = GetProperty(instance, name);
            try { return value == null ? 0 : Convert.ToInt32(value); }
            catch { return 0; }
        }

        private static long GetLongProperty(object instance, string name)
        {
            object value = GetProperty(instance, name);
            try { return value == null ? 0L : Convert.ToInt64(value); }
            catch { return 0L; }
        }

        private static string NameOf(IMonster monster)
        {
            if (monster == null) return "none";
            if (monster.SnoMonster != null && !string.IsNullOrEmpty(monster.SnoMonster.NameEnglish))
                return monster.SnoMonster.NameEnglish;
            return monster.Rarity.ToString();
        }
    }
}
