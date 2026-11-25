using System;
using System.Collections.Generic;
using System.Threading;
using Turbo.Plugins;

namespace SuperD3Hud.TurboHost
{
    /// <summary>
    /// The controller a TurboHUD plugin receives in Load(). It owns every
    /// sub-controller and the plugin list, and it is the only object that
    /// wires the plugin API to the host's data.
    /// </summary>
    public class HostController : IController
    {
        private readonly HostContext _context;
        private readonly HostSnoController _sno;
        private readonly List<IPlugin> _plugins = new List<IPlugin>();
        private readonly HostRenderController _render;
        private readonly HostWindow _window;
        private readonly HostGameController _game;
        private readonly HostTimeController _time;
        private readonly HostTextureController _texture;
        private readonly HostStatController _stat;
        private readonly HostInventoryController _inventory;
        private readonly HostQueueController _queue;
        private readonly HostInputController _input;
        private readonly HostSoundController _sound;
        private readonly HostTrackerController _tracker;
        private readonly HostTextLogController _textLog;
        private readonly HostSceneRevealController _sceneReveal;
        private readonly HostAvoidanceController _avoidance;
        private readonly HostInteractionController _interaction;
        private readonly HostActorQueryController _actorQuery;
        private readonly HostTargetController _target;
        private readonly HostInventoryManagementController _inventoryManagement;

        public HostController(HostContext context, HostCanvas canvas)
        {
            _context = context;
            _sno = new HostSnoController(context);
            _window = new HostWindow(context);
            _render = new HostRenderController(context, canvas, _window);
            _game = new HostGameController(context, _sno);
            _time = new HostTimeController();
            _texture = new HostTextureController(canvas);
            _stat = new HostStatController(context);
            _inventory = new HostInventoryController(context, _sno);
            _queue = new HostQueueController();
            _input = new HostInputController();
            _sound = new HostSoundController();
            _tracker = new HostTrackerController();
            _textLog = new HostTextLogController();
            _sceneReveal = new HostSceneRevealController();
            _avoidance = new HostAvoidanceController();
            _interaction = new HostInteractionController(context, _window, _input);
            _actorQuery = new HostActorQueryController(context, _game);
            _target = new HostTargetController(context, _actorQuery);
            _inventoryManagement = new HostInventoryManagementController(context, _inventory);

            _game.ActorQuery = _actorQuery;

            // Let stub properties that ask for a controller get the real one.
            HostStubs.Register<IController>(this);
            HostStubs.Register<IGameController>(_game);
            HostStubs.Register<ISnoController>(_sno);
            HostStubs.Register<IWindow>(_window);
            HostStubs.Register<IRenderController>(_render);
        }

        /// <summary>Raised when a plugin writes a debug line.</summary>
        public event Action<string> DebugLog;

        /// <summary>Raised when the host should draw a hint.</summary>
        public event Action<string> HintChanged;

        public ITextureController Texture { get { return _texture; } }
        public IRenderController Render { get { return _render; } }
        public ISceneRevealController SceneReveal { get { return _sceneReveal; } }
        public IStatController Stat { get { return _stat; } }
        public IInventoryController Inventory { get { return _inventory; } }
        public IQueueController Queue { get { return _queue; } }
        public IInputController Input { get { return _input; } }
        public IWindow Window { get { return _window; } }
        public IGameController Game { get { return _game; } }
        public ISnoController Sno { get { return _sno; } }
        public ISoundController Sound { get { return _sound; } }
        public ITrackerController Tracker { get { return _tracker; } }
        public ITextLogController TextLog { get { return _textLog; } }
        public ITimeController Time { get { return _time; } }

        public string MyBattleTag { get { return ""; } }
        public IEnumerable<IHero> AccountHeroes { get { return new IHero[0]; } }

        public T GetPlugin<T>() where T : class, IPlugin
        {
            for (int i = 0; i < _plugins.Count; i++)
            {
                T typed = _plugins[i] as T;
                if (typed != null) return typed;
            }
            return null;
        }

        public void RunOnPlugin<T>(Action<T> action) where T : class, IPlugin
        {
            if (action == null) return;
            T plugin = GetPlugin<T>();
            if (plugin != null) action(plugin);
        }

        public void TogglePlugin<T>(bool enabled) where T : class, IPlugin
        {
            for (int i = 0; i < _plugins.Count; i++)
            {
                T typed = _plugins[i] as T;
                if (typed != null) typed.Enabled = enabled;
            }
        }

        public IEnumerable<IPlugin> AllPlugins { get { return _plugins; } }

        public void Debug(string text)
        {
            Action<string> handler = DebugLog;
            if (handler != null) handler(text);
        }

        public IInteractionController Interaction { get { return _interaction; } }
        public IAvoidanceController Avoidance { get { return _avoidance; } }

        public void Wait(int interval, int randomOffset = -1)
        {
            int delay = interval;
            if (randomOffset > 0)
                delay += new Random().Next(0, randomOffset + 1);
            if (delay > 0) Thread.Sleep(delay);
        }

        public bool WaitFor(int timeout, int sleepBetweenTests, int stabilityTimeout, Func<bool> func)
        {
            if (func == null) return false;

            DateTime start = DateTime.UtcNow;
            DateTime stableSince = DateTime.MinValue;

            while ((DateTime.UtcNow - start).TotalMilliseconds < timeout)
            {
                if (func())
                {
                    if (stabilityTimeout <= 0) return true;
                    if (stableSince == DateTime.MinValue) stableSince = DateTime.UtcNow;
                    if ((DateTime.UtcNow - stableSince).TotalMilliseconds >= stabilityTimeout)
                        return true;
                }
                else
                {
                    stableSince = DateTime.MinValue;
                }

                if (sleepBetweenTests > 0) Thread.Sleep(sleepBetweenTests);
            }
            return false;
        }

        public void ReCollect()
        {
            // The host reads the game every frame; there is nothing to flush.
        }

        public Language CurrentLanguage { get { return Language.ruRU; } }

        /// <summary>Adds a plugin to the controller's registry.</summary>
        public void RegisterPlugin(IPlugin plugin)
        {
            if (plugin != null) _plugins.Add(plugin);
        }

        /// <summary>Removes every plugin.</summary>
        public void ClearPlugins()
        {
            _plugins.Clear();
        }

        /// <summary>Exposes the target controller to the runtime.</summary>
        public ITargetController Target { get { return _target; } }

        /// <summary>Exposes the inventory manager to the runtime.</summary>
        public IInventoryManagementController InventoryManagement { get { return _inventoryManagement; } }

        /// <summary>Exposes the actor query controller to the runtime.</summary>
        public IActorQueryController ActorQuery { get { return _actorQuery; } }

        internal void RaiseHint(string text)
        {
            Action<string> handler = HintChanged;
            if (handler != null) handler(text);
        }
    }
}
