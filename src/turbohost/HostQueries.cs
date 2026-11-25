using System;
using System.Collections.Generic;
using Turbo.Plugins;

namespace SuperD3Hud.TurboHost
{
    /// <summary>
    /// Queries over the live actor list. Only the entities the host actually
    /// tracks (monsters, loot) produce results; everything else is null, which
    /// is the API's "not found" value.
    /// </summary>
    public class HostActorQueryController : IActorQueryController
    {
        private readonly HostContext _context;
        private readonly HostGameController _game;

        public HostActorQueryController(HostContext context, HostGameController game)
        {
            _context = context;
            _game = game;
        }

        public IHeadStone NearestHeadStone { get { return null; } }
        public IMonster NearestBoss { get { return NearestBossMonster(); } }
        public IMonster NearestKeywarden { get { return null; } }
        public IMonster NearestElite { get { return Nearest(true, true); } }
        public IMonster NearestEliteButNoMinion { get { return NearestEliteMonster(false); } }

        public bool IsEliteOrBossCloserThan(int range, bool includeMinion = true)
        {
            IMonster monster = NearestEliteMonster(includeMinion);
            return monster != null && monster.CentralXyDistanceToMe <= range;
        }

        public IMonster NearestGoblin { get { return null; } }
        public IShrine NearestShrine { get { return null; } }
        public IClickableActor NearestNormalChest { get { return null; } }
        public IClickableActor NearestResplendentChest { get { return null; } }
        public IClickableActor NearestDoor { get { return null; } }
        public IClickableActor NearestCursedEvent { get { return null; } }
        public IActor Stash { get { return null; } }
        public IActor KanaiCube { get { return null; } }
        public IActor Waypoint { get { return null; } }
        public IActor BookOfCain { get { return null; } }

        public List<IActor> HealthGlobes { get { return ActorsOfKind(ActorKind.HealthGlobe); } }
        public IActor NearestHealthGlobe { get { return null; } }
        public List<IActor> PowerGlobes { get { return ActorsOfKind(ActorKind.PowerGlobe); } }
        public List<IActor> RiftOrbs { get { return ActorsOfKind(ActorKind.RiftOrb); } }

        public int OnScreenMonsterCount
        {
            get
            {
                if (_game == null) return 0;
                int count = 0;
                try
                {
                    foreach (IMonster monster in _game.Monsters)
                        if (monster != null && monster.IsAlive && monster.IsOnScreen) count++;
                }
                catch { return 0; }
                return count;
            }
        }

        public IEnumerable<ISkillEffect> GetSkillEffectActors(SkillEffectType effectType)
        {
            List<ISkillEffect> result = new List<ISkillEffect>();
            if (_game == null) return result;
            try
            {
                IEnumerable<IActor> actors = _game.Actors;
                if (actors == null) return result;
                foreach (IActor actor in actors)
                {
                    ISkillEffect effect = actor as ISkillEffect;
                    if (effect == null || effect.SkillEffectRule == null) continue;
                    if (effect.SkillEffectRule.Type == effectType) result.Add(effect);
                }
            }
            catch { }
            return result;
        }

        internal IMonster FindNearestTarget()
        {
            return NearestTargetMonster();
        }

        internal int CountTargets()
        {
            return OnScreenMonsterCount;
        }

        private IMonster NearestBossMonster()
        {
            if (_game == null) return null;
            IMonster best = null;
            double bestDistance = double.MaxValue;
            try
            {
                foreach (IMonster monster in _game.Monsters)
                {
                    if (monster == null || !monster.IsAlive || monster.Rarity != ActorRarity.Boss) continue;
                    if (!FiniteDistance(monster.CentralXyDistanceToMe)) continue;
                    if (monster.CentralXyDistanceToMe < bestDistance)
                    {
                        bestDistance = monster.CentralXyDistanceToMe;
                        best = monster;
                    }
                }
            }
            catch { }
            return best;
        }

        private IMonster NearestEliteMonster(bool includeMinion)
        {
            return Nearest(true, true, includeMinion);
        }

        private IMonster NearestTargetMonster()
        {
            return Nearest(false, false, true);
        }

        private IMonster Nearest(bool eliteOnly, bool includeBoss)
        {
            return Nearest(eliteOnly, includeBoss, true);
        }

        private IMonster Nearest(bool eliteOnly, bool includeBoss, bool includeMinion)
        {
            if (_game == null) return null;

            IMonster best = null;
            double bestDistance = double.MaxValue;

            try
            {
                foreach (IMonster monster in _game.Monsters)
                {
                    if (monster == null || !monster.IsAlive) continue;
                    if (eliteOnly && !monster.IsElite && !(includeBoss && monster.Rarity == ActorRarity.Boss))
                        continue;
                    if (!includeMinion && monster.Rarity == ActorRarity.RareMinion) continue;

                    double distance = monster.CentralXyDistanceToMe;
                    if (!FiniteDistance(distance)) continue;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = monster;
                    }
                }
            }
            catch { }

            return best;
        }

        private List<IActor> ActorsOfKind(ActorKind kind)
        {
            List<IActor> result = new List<IActor>();
            if (_game == null) return result;
            try
            {
                IEnumerable<IActor> actors = _game.Actors;
                if (actors == null) return result;
                foreach (IActor actor in actors)
                {
                    if (actor == null) continue;
                    ISnoActor sno = actor.SnoActor;
                    if (sno != null && sno.Kind == kind) result.Add(actor);
                }
            }
            catch { }
            return result;
        }

        private static bool FiniteDistance(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;
        }
    }

    /// <summary>Target selection over the live monster list.</summary>
    public class HostTargetController : ITargetController
    {
        private readonly HostContext _context;
        private readonly HostActorQueryController _query;
        private readonly Watch _targetOverrideLastChanged = new Watch();
        private readonly Watch _currentTargetLastDamaged = new Watch();

        private float _maxAttackRange = -1f;
        private bool _allowSkipMonsters = true;
        private int _temporarySkipCountAdjustment;
        private Func<IController, int> _extraSkipCount;
        private Func<IController, bool> _allowSkipElites;
        private IMonster _targetOverride;
        private IMonster _currentTarget;
        private IMonster _currentTargetBeforeSkip;
        private DateTime _noSkipUntilUtc;

        public HostTargetController(HostContext context, HostActorQueryController query)
        {
            _context = context;
            _query = query;
        }

        public float MaxAttackRange
        {
            get { return _maxAttackRange; }
            set { _maxAttackRange = value; }
        }

        public bool AllowSkipMonsters
        {
            get { return _allowSkipMonsters; }
            set { _allowSkipMonsters = value; }
        }

        public int TemporarySkipCountAdjustment
        {
            get { return _temporarySkipCountAdjustment; }
            set { _temporarySkipCountAdjustment = value; }
        }

        public Func<IController, int> ExtraSkipCount
        {
            get { return _extraSkipCount; }
            set { _extraSkipCount = value; }
        }

        public Func<IController, bool> AllowSkipElites
        {
            get { return _allowSkipElites; }
            set { _allowSkipElites = value; }
        }

        public double CurrentAttackRange { get { return _maxAttackRange < 0 ? 0 : _maxAttackRange; } }
        public bool CanSkipMonsters { get { return _allowSkipMonsters; } }
        public int MonsterCountToSkip
        {
            get
            {
                int result = _temporarySkipCountAdjustment;
                if (_extraSkipCount != null)
                {
                    try { result += _extraSkipCount(null); } catch { }
                }
                return result < 0 ? 0 : result;
            }
        }

        public IMonster TargetOverride
        {
            get { return _targetOverride; }
            set
            {
                _targetOverride = value;
                _targetOverrideLastChanged.Restart();
            }
        }

        public IWatch TargetOverrideLastChanged { get { return _targetOverrideLastChanged; } }

        public void NoSkipFor(int milliseconds)
        {
            if (milliseconds <= 0)
            {
                _noSkipUntilUtc = DateTime.UtcNow;
                return;
            }
            try { _noSkipUntilUtc = DateTime.UtcNow.AddMilliseconds(milliseconds); }
            catch { _noSkipUntilUtc = DateTime.MaxValue; }
        }

        public IMonster NearestTarget
        {
            get
            {
                if (_targetOverride != null) return _targetOverride;
                if (_query == null) return null;
                IMonster nearest = _query.FindNearestTarget();
                if (nearest == null) return null;
                if (_maxAttackRange >= 0f && nearest.CentralXyDistanceToMe > _maxAttackRange)
                    return null;
                return nearest;
            }
        }

        public int TargetCount
        {
            get
            {
                if (_query == null) return 0;
                return _query == null ? 0 : _query.CountTargets();
            }
        }

        public IMonster CurrentTarget
        {
            get { return _currentTarget; }
            set
            {
                if (_currentTarget != value) _currentTargetBeforeSkip = _currentTarget;
                _currentTarget = value;
                _currentTargetLastDamaged.Restart();
            }
        }

        public IMonster CurrentTargetBeforeSkip { get { return _currentTargetBeforeSkip; } }
        public IWatch CurrentTargetLastDamaged { get { return _currentTargetLastDamaged; } }

        public void PaintTopDebug(ClipState clipState) { }
    }

    /// <summary>Inventory maintenance. The host does not automate the game.</summary>
    public class HostInventoryManagementController : IInventoryManagementController
    {
        private readonly HostContext _context;
        private readonly HostInventoryController _inventory;
        private readonly Watch _lastCleanup = new Watch();
        private bool _dropLegendaryPotionsEnabled;

        public HostInventoryManagementController(HostContext context, HostInventoryController inventory)
        {
            _context = context;
            _inventory = inventory;
        }

        public bool Working { get { return false; } }
        public IWatch LastInventoryCleanupOn { get { return _lastCleanup; } }

        public ActionResult InventoryCleanup(int timeout, bool close = true)
        {
            if (timeout < 0) return ActionResult.Fail;
            if (_context == null || !_context.GameRunning || _inventory == null) return ActionResult.Disconnected;
            return ActionResult.Fail;
        }

        public bool DropLegendaryPotionsEnabled
        {
            get { return _dropLegendaryPotionsEnabled; }
            set { _dropLegendaryPotionsEnabled = value; }
        }

        public ActionResult DropLegendaryPotions(int timeout = 60000)
        {
            if (timeout < 0) return ActionResult.Fail;
            if (_context == null || !_context.GameRunning || _inventory == null) return ActionResult.Disconnected;
            return ActionResult.Fail;
        }
    }
}
