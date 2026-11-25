using System;
using Turbo.Plugins;
using Turbo.Plugins.Default;

namespace TurboPlugins
{
    /// <summary>Проверка этапа 1: контроллер игры, игрок, окно и рисование.</summary>
    public class Stage1Demo : BasePlugin, IInGameTopPainter
    {
        /// <summary>Краткое описание для списка плагинов.</summary>
        public string Description
        {
            get { return "Демонстрация этапа 1: контроллер игры, игрок, окно и рисование."; }
        }

        private IFont _font;
        private IBrush _brush;

        public override void Load(IController hud)
        {
            base.Load(hud);
            if (Hud == null || Hud.Render == null) return;
            _font = Hud.Render.CreateFont("Segoe UI", 12f, 255, 220, 240, 255,
                                         false, false, true);
            _brush = Hud.Render.CreateBrush(220, 80, 190, 255, 2f);
        }

        public void PaintTopInGame(ClipState clipState)
        {
            if (clipState != ClipState.BeforeClip || Hud == null) return;
            if (Hud.Render == null || Hud.Window == null || Hud.Time == null
                || Hud.Game == null) return;
            if (!Hud.Game.IsInGame) return;

            int monsterCount = 0;
            if (Hud.Game.Monsters != null)
            {
                foreach (IMonster monster in Hud.Game.Monsters)
                    if (monster != null) monsterCount++;
            }

            IPlayer me = Hud.Game.Me;
            string playerText = me == null ? "no player" : "player";
            int width = Hud.Window.Size.Width;
            float x = 16f;
            float y = 16f;
            float lineEnd = width > 96 ? x + 64f : x + 24f;

            if (_brush != null) _brush.DrawLine(x, y, lineEnd, y);
            if (_font != null)
            {
                _font.DrawText("Stage1 " + playerText + " " + monsterCount +
                               " " + Hud.Time.Now.ToString("HH:mm:ss"), x, y + 4f);
            }
        }
    }
}
