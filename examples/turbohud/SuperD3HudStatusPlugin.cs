using System;
using System.Drawing;
using Turbo.Plugins;
using Turbo.Plugins.Default;

namespace TurboPlugins
{
    /// <summary>
    /// Показывает состояние совместимости TurboHUD: позицию игрока,
    /// число монстров и подсказку о версии API.
    /// </summary>
    public class SuperD3HudStatusPlugin : BasePlugin, IAfterCollectHandler, IInGameTopPainter
    {
        /// <summary>Краткое описание для списка плагинов.</summary>
        public string Description
        {
            get { return "Показывает состояние хоста TurboHUD: позицию игрока и число монстров."; }
        }

        private IFont _font;
        private IFont _small;
        private IBrush _shadow;

        public override void Load(IController hud)
        {
            base.Load(hud);
            _font = Hud.Render.CreateFont("Segoe UI", 16f, 255, 232, 163, 61, true, false, true);
            _small = Hud.Render.CreateFont("Segoe UI", 11f, 220, 220, 220, 220, false, false, true);
            _shadow = Hud.Render.CreateBrush(180, 0, 0, 0, 2f);
        }

        public void AfterCollect()
        {
            // Данные уже собраны хостом; здесь плагин может подготовить кэш.
        }

        public void PaintTopInGame(ClipState clipState)
        {
            if (clipState != ClipState.BeforeClip) return;
            if (Hud == null || Hud.Game == null || !Hud.Game.IsInGame) return;

            float y = 40f;
            _shadow.DrawRectangle(20f, y - 4f, 420f, 66f);
            _font.DrawText("SuperD3Hud — TurboHUD API", 28f, y);

            IPlayer me = Hud.Game.Me;
            string position = me == null
                ? "нет данных"
                : string.Format("X {0:0}  Y {1:0}", me.FloorCoordinate.X, me.FloorCoordinate.Y);

            int monsters = 0;
            foreach (IMonster m in Hud.Game.AliveMonsters) monsters++;

            _small.DrawText("Игрок: " + position + "   Монстров: " + monsters, 28f, y + 24f);
        }
    }
}
