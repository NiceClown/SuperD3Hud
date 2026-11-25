using System;
using System.Collections.Generic;
using SuperD3Hud.Game;
using Turbo.Plugins;

namespace SuperD3Hud.TurboHost
{
    /// <summary>
    /// Base of every actor the host exposes. The host mirrors what its own
    /// readers captured; it never writes to the game.
    /// </summary>
    public class HostActor : IActor
    {
        protected readonly HostContext Context;
        protected readonly HostSnoController Sno;

        private IWatch _lastSpeak;
        private AvoidableDefinition _avoidableDefinition;

        public HostActor(HostContext context, HostSnoController sno)
        {
            Context = context;
            Sno = sno;
        }

        public virtual ISnoActor SnoActor
        {
            get { return Sno != null ? Sno.SnoActor("actor:0", 0u) : HostStubs.Get<ISnoActor>(); }
        }
        public virtual GizmoType GizmoType { get { return GizmoType.Invalid; } }

        public virtual uint AcdId { get { return 0; } }
        public virtual uint AnnId { get { return 0; } }

        public virtual IWorldCoordinate FloorCoordinate
        {
            get { return new WorldCoordinate(0, 0, 0); }
        }

        public virtual IWorldCoordinate CollisionCoordinate
        {
            get { return FloorCoordinate; }
        }

        public virtual float RadiusScaled { get { return 0f; } }
        public virtual float RadiusBottom { get { return 0f; } }

        public virtual uint WorldId
        {
            get { return Context == null || Context.WorldId < 0 ? 0u : (uint)Context.WorldId; }
        }

        public virtual uint WorldSno { get { return WorldId; } }
        public virtual ISnoWorld SnoWorld { get { return null; } }
        public virtual IScene Scene { get { return Context == null ? null : new HostScene(WorldId); } }

        public virtual double GetAttributeValue(IAttribute attribute, uint modifier, double defaultValue = -1.0f)
        {
            return defaultValue;
        }

        public virtual int GetAttributeValueAsInt(IAttribute attribute, uint modifier, int defaultValue = int.MaxValue)
        {
            return defaultValue;
        }

        public virtual uint GetAttributeValueAsUInt(IAttribute attribute, uint modifier, uint defaultValue = uint.MaxValue)
        {
            return defaultValue;
        }

        public virtual IScreenCoordinate ScreenCoordinate
        {
            get
            {
                IWorldCoordinate coordinate = FloorCoordinate;
                if (coordinate == null) return new ScreenCoordinate(0, 0);
                return coordinate.ToScreenCoordinate();
            }
        }

        public virtual bool IsOnScreen
        {
            get
            {
                IWorldCoordinate coordinate = FloorCoordinate;
                return coordinate != null && coordinate.IsOnScreen();
            }
        }

        public virtual double CentralXyDistanceToMe
        {
            get
            {
                IWorldCoordinate mine = FloorCoordinate;
                return DistanceToMe(mine);
            }
        }

        public virtual double NormalizedXyDistanceToMe { get { return CentralXyDistanceToMe; } }
        public virtual double ZDistanceToMeAbsolute
        {
            get
            {
                IWorldCoordinate coordinate = FloorCoordinate;
                if (coordinate == null || !IsFinite(coordinate.Z)) return double.MaxValue;
                return Math.Abs(coordinate.Z);
            }
        }

        protected double DistanceToMe(IWorldCoordinate coordinate)
        {
            if (Context == null || coordinate == null) return double.MaxValue;
            if (!IsFinite(coordinate.X) || !IsFinite(coordinate.Y)) return double.MaxValue;
            double dx = coordinate.X - Context.PlayerX;
            double dy = coordinate.Y - Context.PlayerY;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            return double.IsNaN(distance) || double.IsInfinity(distance) ? double.MaxValue : distance;
        }

        protected static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public virtual bool IsSelected { get { return false; } }
        public virtual float Hitpoints { get { return 0f; } }
        public virtual uint SummonerId { get { return 0; } }
        public virtual uint SummonerAcdDynamicId { get { return 0; } }
        public virtual bool Untargetable { get { return false; } }
        public virtual bool IsClickable { get { return false; } }
        public virtual bool IsDisabled { get { return false; } }
        public virtual bool IsOperated { get { return false; } }
        public virtual bool DisplayOnOverlay { get { return !IsDisabled && !IsOperated; } }
        public virtual uint Amount { get { return 0; } }
        public virtual int CreatedAtInGameTick { get { return 0; } }

        public virtual IWatch LastSpeak
        {
            get { return _lastSpeak; }
            set { _lastSpeak = value; }
        }

        public virtual AvoidableDefinition AvoidableDefinition
        {
            get { return _avoidableDefinition; }
            set { _avoidableDefinition = value; }
        }
    }

    /// <summary>The local player, built from the live actor snapshot.</summary>
    public class HostPlayer : HostActor, IPlayer
    {
        private uint _actorId;
        private readonly float _x;
        private readonly float _y;

        private ISnoItem _cube1;
        private ISnoItem _cube2;
        private ISnoItem _cube3;
        private ISnoItem _cube4;

        private int _loopingStart;
        private int _loopingEnd;
        private IPlayerPowerInfo _powers;
        private IPlayerStatInfo _stats;
        private IPlayerDefenseInfo _defense;

        public HostPlayer(HostContext context, HostSnoController sno, float x, float y)
            : base(context, sno)
        {
            _x = x;
            _y = y;
        }

        public override uint AcdId { get { return _actorId; } }
        public uint ActorId
        {
            get { return _actorId; }
            set { _actorId = value; }
        }

        public override IWorldCoordinate FloorCoordinate
        {
            get { return new WorldCoordinate(_x, _y, 0f); }
        }

        public override float Hitpoints
        {
            get
            {
                if (Context == null || Context.Skills == null) return 0f;
                double health = Context.Skills.Health;
                if (health < 0d || double.IsNaN(health) || double.IsInfinity(health)) return 0f;
                return health > float.MaxValue ? float.MaxValue : (float)health;
            }
        }

        public bool IsMe { get { return true; } }
        public bool IsInGame { get { return Context != null && Context.GameRunning; } }
        public IHeadStone HeadStone { get { return null; } }
        public bool HasValidActor { get { return Context != null && Context.PlayerKnown; } }
        public override float RadiusScaled { get { return 2.5f; } }
        public override float RadiusBottom { get { return 2.5f; } }

        internal HostContext PlayerContext { get { return Context; } }
        internal HostSnoController PlayerSno { get { return Sno; } }

        public IHero Hero { get { return HostStubs.Get<IHero>(); } }
        public bool HeroIsHardcore { get { return false; } }
        public bool HeroIsMale { get { return true; } }

        public int GetSetItemCount(uint setId) { return 0; }

        public uint HeroId { get { return 0; } }
        public string HeroName { get { return ""; } }
        public IHeroClassDefinition HeroClassDefinition { get { return HostStubs.Get<IHeroClassDefinition>(); } }
        public string BattleTagAbovePortrait { get { return ""; } }
        public bool CoordinateKnown { get { return Context != null && Context.PlayerKnown; } }
        public ISnoArea SnoArea { get { return null; } }
        public int Index { get { return 0; } }
        public int PortraitIndex { get { return 0; } }
        public IUiElement PortraitUiElement { get { return null; } }

        public bool IsCubed(ISnoItem snoItem) { return false; }

        public ISnoItem CubeSnoItem1 { get { return _cube1; } set { _cube1 = value; } }
        public ISnoItem CubeSnoItem2 { get { return _cube2; } set { _cube2 = value; } }
        public ISnoItem CubeSnoItem3 { get { return _cube3; } set { _cube3 = value; } }
        public ISnoItem CubeSnoItem4 { get { return _cube4; } set { _cube4 = value; } }

        public IEnumerable<ISnoItem> CubedItems
        {
            get
            {
                List<ISnoItem> result = new List<ISnoItem>();
                if (_cube1 != null) result.Add(_cube1);
                if (_cube2 != null) result.Add(_cube2);
                if (_cube3 != null) result.Add(_cube3);
                if (_cube4 != null) result.Add(_cube4);
                return result;
            }
        }

        public uint CurrentLevelNormal { get { return 0; } }
        public uint CurrentLevelNormalCap { get { return 70; } }
        public uint CurrentLevelParagon { get { return 0; } }
        public double CurrentLevelParagonDouble { get { return 0; } }

        public IPlayerOffenseInfo Offense { get { return HostStubs.Get<IPlayerOffenseInfo>(); } }
        public IPlayerStatInfo Stats
        {
            get
            {
                if (_stats == null) _stats = new HostPlayerStatInfo(Context);
                return _stats;
            }
        }
        public IPlayerDefenseInfo Defense
        {
            get
            {
                if (_defense == null) _defense = new HostPlayerDefenseInfo(Context);
                return _defense;
            }
        }
        public IPlayerDamageInfo Damage { get { return HostStubs.Get<IPlayerDamageInfo>(); } }
        public IPlayerMaterialInfo Materials { get { return HostStubs.Get<IPlayerMaterialInfo>(); } }
        public IPlayerPowerInfo Powers
        {
            get
            {
                if (_powers == null) _powers = new HostPlayerPowerInfo(this);
                return _powers;
            }
        }

        public bool InCombat { get { return false; } }

        public int LoopingAnimationStartTick
        {
            get { return _loopingStart; }
            set { _loopingStart = value; }
        }

        public int LoopingAnimationEndTick
        {
            get { return _loopingEnd; }
            set { _loopingEnd = value; }
        }

        public bool IsInTown { get { return false; } }
        public bool InGreaterRift { get { return false; } }
        public uint InGreaterRiftRank { get { return 0; } }
        public bool IsDead
        {
            get
            {
                if (Context == null || Context.Skills == null) return false;
                return Context.Skills.HealthMax > 0d && Context.Skills.Health <= 0d;
            }
        }
        public IWatch LastDied { get { return null; } }
        public bool IsDeadSafeCheck { get { return IsDead; } }
        public AnimSnoEnum Animation { get { return default(AnimSnoEnum); } }
        public AcdAnimationState AnimationState { get { return default(AcdAnimationState); } }

        public long ParagonTotalExp { get { return 0; } }
        public long ParagonExpInThisLevel { get { return 0; } }
        public long ParagonExpToNextLevel { get { return 0; } }
        public float BonusPoolPercent { get { return 0; } }
        public long BonusPoolRemaining { get { return 0; } }
        public int HighestSoloRiftLevel { get { return 0; } }
        public int HighestHeroSoloRiftLevel { get { return 0; } }
        public int InventorySpaceTotal { get { return 60; } }
        public int ParagonPointsAvailableTotal { get { return 0; } }
        public int[] ParagonPointsAvailable { get { return new int[0]; } }

        public float GetPowerTagValue(ISnoPower snoPower, uint tagId) { return 0f; }

        public IPlayerArmorySet[] ArmorySets { get { return new IPlayerArmorySet[0]; } }
        public IPlayerDensityInfo Density { get { return HostStubs.Get<IPlayerDensityInfo>(); } }
        public List<IActor> AvoidablesInRange { get { return new List<IActor>(); } }

        internal IEnumerable<IBuff> GetBuffs()
        {
            // The buff objects are cached in the SNO controller, which outlives
            // this per-revision player object, so FirstActive/TimeElapsed keep
            // counting between snapshots. The returned array is shared.
            if (Sno == null || Context == null) return new IBuff[0];
            return Sno.BuffCache.GetBuffs(this, Sno, Context);
        }
    }

    internal sealed class HostPlayerStatInfo : IPlayerStatInfo
    {
        private readonly HostContext _context;
        private float _resourceCurPri;
        private float _resourceCurSec;
        private float _resourceMaxPri;
        private float _resourceMaxSec;

        public HostPlayerStatInfo(HostContext context)
        {
            _context = context;
            _resourceCurPri = -1f;
            _resourceCurSec = -1f;
            _resourceMaxPri = -1f;
            _resourceMaxSec = -1f;
        }

        private float Resource(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d) return -1f;
            return value > float.MaxValue ? float.MaxValue : (float)value;
        }

        public float ResourceMaxArcane { get; set; }
        public float ResourceMaxSpirit { get; set; }
        public float ResourceMaxFury { get; set; }
        public float ResourceMaxMana { get; set; }
        public float ResourceMaxHatred { get; set; }
        public float ResourceMaxDiscipline { get; set; }
        public float ResourceMaxWrath { get; set; }
        public float ResourceMaxEssence { get; set; }
        public float ResourceCurArcane { get; set; }
        public float ResourceCurSpirit { get; set; }
        public float ResourceCurFury { get; set; }
        public float ResourceCurMana { get; set; }
        public float ResourceCurHatred { get; set; }
        public float ResourceCurDiscipline { get; set; }
        public float ResourceCurWrath { get; set; }
        public float ResourceCurEssence { get; set; }
        public float ResourcePctArcane { get; set; }
        public float ResourcePctSpirit { get; set; }
        public float ResourcePctFury { get; set; }
        public float ResourcePctMana { get; set; }
        public float ResourcePctHatred { get; set; }
        public float ResourcePctDiscipline { get; set; }
        public float ResourcePctWrath { get; set; }
        public float ResourcePctEssence { get; set; }
        public float ResourceRegArcane { get; set; }
        public float ResourceRegSpirit { get; set; }
        public float ResourceRegFury { get; set; }
        public float ResourceRegMana { get; set; }
        public float ResourceRegHatred { get; set; }
        public float ResourceRegDiscipline { get; set; }
        public float ResourceRegWrath { get; set; }
        public float ResourceRegEssence { get; set; }
        public float CooldownReduction { get; set; }
        public float ResourceCostReduction { get; set; }
        public float ResourceCurPri { get { return _context == null || _context.Skills == null ? _resourceCurPri : Resource(_context.Skills.PrimaryResource); } set { _resourceCurPri = value; } }
        public float ResourceCurSec { get { return _context == null || _context.Skills == null ? _resourceCurSec : Resource(_context.Skills.SecondaryResource); } set { _resourceCurSec = value; } }
        public float ResourceMaxPri { get { return _context == null || _context.Skills == null ? _resourceMaxPri : Resource(_context.Skills.PrimaryResourceMax); } set { _resourceMaxPri = value; } }
        public float ResourceMaxSec { get { return _context == null || _context.Skills == null ? _resourceMaxSec : Resource(_context.Skills.SecondaryResourceMax); } set { _resourceMaxSec = value; } }
        public float ResourcePctPri { get { return Percent(ResourceCurPri, ResourceMaxPri); } set { } }
        public float ResourcePctSec { get { return Percent(ResourceCurSec, ResourceMaxSec); } set { } }
        public float ResourceRegPri { get; set; }
        public float ResourceRegSec { get; set; }
        public float MainStat { get; set; }
        public float Strength { get; set; }
        public float Dexterity { get; set; }
        public float Intelligence { get; set; }
        public float Vitality { get; set; }
        public float MoveSpeed { get; set; }
        public float MoveSpeedBonus { get; set; }
        public float MagicFind { get; set; }
        public float GoldFind { get; set; }
        public float ExperiencePercentBonus { get; set; }
        public float ExpOnKill { get; set; }
        public float ExpOnKillNoPenalty { get; set; }
        public float ExperienceOnKillBonus { get { return 0f; } }
        public float PickupRange { get; set; }

        private static float Percent(float value, float maximum)
        {
            if (value < 0f || maximum <= 0f) return 0f;
            return Math.Max(0f, Math.Min(100f, value * 100f / maximum));
        }
    }

    internal sealed class HostPlayerDefenseInfo : IPlayerDefenseInfo
    {
        private readonly HostContext _context;
        public HostPlayerDefenseInfo(HostContext context) { _context = context; }
        private float Health(bool maximum)
        {
            if (_context == null || _context.Skills == null) return 0f;
            double value = maximum ? _context.Skills.HealthMax : _context.Skills.Health;
            return value < 0d || double.IsNaN(value) || double.IsInfinity(value) ? 0f : (float)value;
        }
        public float EhpCur { get { return Health(false); } set { } }
        public float EhpMax { get { return Health(true); } set { } }
        public float HealthCur { get { return Health(false); } set { } }
        public float HealthMax { get { return Health(true); } set { } }
        public float HealthPct { get { return HealthMax <= 0f ? 0f : HealthCur * 100f / HealthMax; } set { } }
        public float CurShield { get; set; }
        public float Armor { get; set; }
        public float ResPhysical { get; set; }
        public float ResCold { get; set; }
        public float ResFire { get; set; }
        public float ResLightning { get; set; }
        public float ResPoison { get; set; }
        public float ResArcane { get; set; }
        public float ResLowest { get; set; }
        public float ResAverage { get; set; }
        public float LifeBonus { get; set; }
        public float LifeRegen { get; set; }
        public float LifeOnHit { get; set; }
        public float LifeOnKill { get; set; }
        public float Thorns { get; set; }
        public float GlobeBonusHealth { get; set; }
        public float DRClass { get; set; }
        public float drArmor { get; set; }
        public float drResist { get; set; }
        public float drCombined { get; set; }
        public float DRRanged { get; set; }
        public float DRMelee { get; set; }
        public float DRElite { get; set; }
        public float[] DamageReductionFromType { get { return new float[0]; } }
        public float AverageDamageReductionFromType { get; set; }
        public float CCReduction { get; set; }
        public float DamageReduction { get; set; }
        public double CurrentDamageTakenPerSecond { get; set; }
        public double CurrentHealingPerSecond { get; set; }
        public double CurrentEffectiveHealingPercent { get { return 0d; } }
    }

    /// <summary>Skill slot view backed by the host's skill cooldown snapshot.</summary>
    public sealed class HostPlayerSkill : IPlayerSkill
    {
        private readonly HostPlayer _player;
        private readonly int _slot;
        private readonly ActionKey _key;
        private readonly ISnoPower _snoPower;

        public HostPlayerSkill(HostPlayer player, int slot, ActionKey key)
        {
            _player = player;
            _slot = slot;
            _key = key;
            _snoPower = new HostSnoPower(_player != null ? _player.PlayerSno : null, 0u, "", "Skill" + slot);
        }

        public IPlayer Player { get { return _player; } }
        public ISnoPower SnoPower
        {
            get
            {
                if (_player != null && _player.PlayerContext != null && _player.PlayerContext.Skills != null)
                {
                    int[] powers = _player.PlayerContext.Skills.EquippedPowerSnos;
                    if (powers != null && _slot < powers.Length && powers[_slot] > 0)
                    {
                        uint sno = (uint)powers[_slot];
                        return new HostSnoPower(_player.PlayerSno, sno, "Power" + sno, "Skill" + _slot);
                    }
                }
                return _snoPower;
            }
        }
        public ISnoPower OverrideSnoPower { get { return null; } }
        public ISnoPower CurrentSnoPower { get { return SnoPower; } }

        public byte Rune
        {
            get
            {
                if (_player != null && _player.PlayerContext != null && _player.PlayerContext.Skills != null)
                {
                    int[] runes = _player.PlayerContext.Skills.EquippedRunes;
                    if (runes != null && _slot < runes.Length && runes[_slot] >= 0)
                        return (byte)runes[_slot];
                }
                return 0;
            }
        }
        public string RuneNameLocalized { get { return ""; } }
        public string RuneNameEnglish { get { return ""; } }
        public ActionKey Key { get { return _key; } }

        private double GetRemainingSeconds()
        {
            if (_player == null || _player.PlayerContext == null) return 0.0;
            HostContext ctx = _player.PlayerContext;
            double remaining = 0.0;
            if (ctx.Cooldowns != null && ctx.Cooldowns.TryGetValue(_slot, out remaining) && remaining > 0.0)
                return remaining;
            if (ctx.Skills != null && ctx.Skills.Skills != null && _slot < ctx.Skills.Skills.Count)
            {
                SkillCooldown s = ctx.Skills.Skills[_slot];
                if (s != null && s.RemainingSeconds > 0.0) return s.RemainingSeconds;
            }
            return 0.0;
        }

        private double GetDurationSeconds()
        {
            if (_player == null || _player.PlayerContext == null) return 0.0;
            HostContext ctx = _player.PlayerContext;
            double duration = 0.0;
            if (ctx.CooldownDurations != null && ctx.CooldownDurations.TryGetValue(_slot, out duration) && duration > 0.0)
                return duration;
            if (ctx.Skills != null && ctx.Skills.Skills != null && _slot < ctx.Skills.Skills.Count)
            {
                SkillCooldown s = ctx.Skills.Skills[_slot];
                if (s != null && s.DurationSeconds > 0.0) return s.DurationSeconds;
            }
            double rem = GetRemainingSeconds();
            return rem > 0.0 ? rem : 0.0;
        }

        public bool IsOnCooldown
        {
            get { return GetRemainingSeconds() > 0.05; }
        }

        public int CooldownFinishTick
        {
            get
            {
                if (_player != null && _player.PlayerContext != null && _player.PlayerContext.FinishTicks != null)
                {
                    int tick;
                    if (_player.PlayerContext.FinishTicks.TryGetValue(_slot, out tick) && tick > 0)
                        return tick;
                }
                double remaining = GetRemainingSeconds();
                int currentTick = GetCurrentGameTick();
                return currentTick + (int)Math.Round(remaining * 60.0);
            }
        }

        public int CooldownStartTick
        {
            get
            {
                double duration = GetDurationSeconds();
                return CooldownFinishTick - (int)Math.Round(duration * 60.0);
            }
        }

        private int GetCurrentGameTick()
        {
            if (_player != null && _player.PlayerContext != null)
            {
                if (_player.PlayerContext.CurrentGameTick > 0)
                    return _player.PlayerContext.CurrentGameTick;
                if (_player.PlayerContext.Skills != null && _player.PlayerContext.Skills.GameTick > 0)
                    return _player.PlayerContext.Skills.GameTick;
                if (_player.PlayerContext.CurrentGameTickProvider != null)
                    return _player.PlayerContext.CurrentGameTickProvider();
            }
            return 0;
        }

        public int ElementalType { get { return -1; } }
        public int WeaponDamageMultiplier { get { return -1; } }
        public float DotSeconds { get { return 0f; } }
        public float ResourceCost { get { return 0f; } }
        public float GetResourceRequirement() { return 0f; }
        public float GetResourceRequirement(float baseRequirement) { return baseRequirement; }
        public float CalculateCooldown(float baseCooldown) { return baseCooldown; }
        public float DamageBonus { get { return 0f; } }
        public float ElementalDamageBonus { get { return 0f; } }
        public bool BuffIsActive { get { return false; } }
        public IBuff Buff { get { return null; } }
        public int Charges { get { return 0; } }
        public IWatch LastUsed { get { return null; } }
        public IWatch LastReleased { get { return null; } }
        public double RemainingBuffTime() { return 0.0; }
        public double RemainingBuffTime(int buffIconIndex) { return 0.0; }
    }

    /// <summary>Player power view backed by the host's buff snapshot.</summary>
    internal sealed class HostPlayerPowerInfo : IPlayerPowerInfo
    {
        private readonly HostPlayer _player;
        private readonly IPlayerPowerInfo _fallback;
        private readonly IPlayerSkill[] _skills;

        public HostPlayerPowerInfo(HostPlayer player)
        {
            _player = player;
            _fallback = HostStubs.Get<IPlayerPowerInfo>();
            _skills = new IPlayerSkill[]
            {
                new HostPlayerSkill(_player, 0, ActionKey.LeftSkill),
                new HostPlayerSkill(_player, 1, ActionKey.RightSkill),
                new HostPlayerSkill(_player, 2, ActionKey.Skill1),
                new HostPlayerSkill(_player, 3, ActionKey.Skill2),
                new HostPlayerSkill(_player, 4, ActionKey.Skill3),
                new HostPlayerSkill(_player, 5, ActionKey.Skill4),
            };
        }

        public IPlayerSkill HealthPotionSkill { get { return new HostPlayerSkill(_player, 6, ActionKey.Heal); } }
        public IEnumerable<IPlayerSkill> UsedSkills { get { return _skills; } }
        public IEnumerable<IPlayerSkill> CurrentSkills { get { return _skills; } }
        public IEnumerable<ISnoPower> UsedPassives { get { return _fallback == null ? new ISnoPower[0] : _fallback.UsedPassives; } }
        public IPlayerSkill[] SkillSlots { get { return _skills; } }
        public ISnoPower[] PassiveSlots { get { return _fallback == null ? new ISnoPower[0] : _fallback.PassiveSlots; } }
        public bool SkillOverrideActive { get { return _fallback != null && _fallback.SkillOverrideActive; } }
        public IBarbarianPowerInfo UsedBarbarianPowers { get { return _fallback == null ? null : _fallback.UsedBarbarianPowers; } }
        public ICrusaderPowerInfo UsedCrusaderPowers { get { return _fallback == null ? null : _fallback.UsedCrusaderPowers; } }
        public IDemonHunterPowerInfo UsedDemonHunterPowers { get { return _fallback == null ? null : _fallback.UsedDemonHunterPowers; } }
        public IMonkPowerInfo UsedMonkPowers { get { return _fallback == null ? null : _fallback.UsedMonkPowers; } }
        public INecromancerPowerInfo UsedNecromancerPowers { get { return _fallback == null ? null : _fallback.UsedNecromancerPowers; } }
        public IWitchDoctorPowerInfo UsedWitchDoctorPowers { get { return _fallback == null ? null : _fallback.UsedWitchDoctorPowers; } }
        public IWizardPowerInfo UsedWizardPowers { get { return _fallback == null ? null : _fallback.UsedWizardPowers; } }
        public ILegendaryPowerInfo UsedLegendaryPowers { get { return _fallback == null ? null : _fallback.UsedLegendaryPowers; } }
        public ILegendaryGemInfo UsedLegendaryGems { get { return _fallback == null ? null : _fallback.UsedLegendaryGems; } }
        public IEnumerable<IBuff> AllBuffs { get { return _player == null ? new IBuff[0] : _player.GetBuffs(); } }
        public bool Frozen { get; set; }
        public bool Rooted { get; set; }
        public bool Stunned { get; set; }
        public bool CantMove { get { return Frozen || Rooted || Stunned; } }

        public IPlayerSkill GetUsedSkill(ISnoPower snoPower)
        {
            return _fallback == null ? null : _fallback.GetUsedSkill(snoPower);
        }

        public IBuff GetBuff(string code)
        {
            foreach (IBuff buff in AllBuffs)
                if (buff != null && buff.SnoPower != null && buff.SnoPower.Code == (code ?? "")) return buff;
            return null;
        }

        public IBuff GetBuff(uint sno)
        {
            foreach (IBuff buff in AllBuffs)
                if (buff != null && buff.SnoPower != null && buff.SnoPower.Sno == sno) return buff;
            return null;
        }

        public bool BuffIsActive(uint sno)
        {
            IBuff buff = GetBuff(sno);
            return buff != null && buff.Active;
        }

        public bool BuffIsActive(uint sno, int iconIndex)
        {
            IBuff buff = GetBuff(sno);
            if (buff == null || !buff.Active || iconIndex < 0) return false;
            int[] counts = buff.IconCounts;
            return counts != null && iconIndex < counts.Length && counts[iconIndex] > 0;
        }
    }

    /// <summary>A monster, built from the live actor snapshot.</summary>
    public class HostMonster : HostActor, IMonster
    {
        private readonly MonsterMarker _marker;
        private double _priority;

        public HostMonster(HostContext context, HostSnoController sno, MonsterMarker marker)
            : base(context, sno)
        {
            _marker = marker;
        }

        public MonsterMarker Marker { get { return _marker; } }

        public override ISnoActor SnoActor
        {
            get
            {
                if (Sno == null || _marker == null || _marker.Sno <= 0) return null;
                return Sno.SnoActor("monster:" + _marker.Sno, (uint)_marker.Sno);
            }
        }

        public ISnoMonster SnoMonster
        {
            get
            {
                if (Sno == null || _marker == null || _marker.Sno <= 0) return null;
                MonsterPriority priority = _marker.IsBoss ? MonsterPriority.boss : (_marker.IsElite ? MonsterPriority.high : MonsterPriority.normal);
                float prog = _marker.IsBoss ? 100f : (_marker.Rarity == (int)ActorRarity.Rare ? 4.5f : (_marker.Rarity == (int)ActorRarity.Champion ? 3.5f : 1.15f));
                string name = !string.IsNullOrEmpty(_marker.Label) ? _marker.Label : (_marker.IsBoss ? "Босс" : (_marker.IsElite ? "Элита" : "Монстр"));
                return new HostSnoMonster((uint)_marker.Sno, "monster:" + _marker.Sno,
                                          SnoActor as HostSnoActor, priority, prog, name);
            }
        }

        public override uint AcdId { get { return _marker == null || _marker.Id < 0 ? 0u : (uint)_marker.Id; } }

        public override IWorldCoordinate FloorCoordinate
        {
            get
            {
                if (_marker == null) return new WorldCoordinate();
                float x, y;
                if (Context != null) Context.MonsterPosition(_marker, out x, out y);
                else { x = _marker.X; y = _marker.Y; }
                return new WorldCoordinate(x, y, 0f);
            }
        }

        public override float Hitpoints { get { return _marker == null || !IsFinite(_marker.Health) || _marker.Health < 0f ? 0f : _marker.Health; } }

        public bool IsAlive { get { return _marker != null && IsFinite(_marker.Health) && _marker.Health > 0f; } }
        public bool IsElite { get { return _marker != null && _marker.IsElite; } }
        public override float RadiusBottom
        {
            get
            {
                if (_marker != null && _marker.IsBoss) return 8f;
                if (IsElite) return 5f;
                return 3f;
            }
        }

        public override float RadiusScaled
        {
            get { return RadiusBottom; }
        }

        public double MaxHealth { get { return Math.Max(1.0, (double)Hitpoints); } }
        public double CurHealth { get { return (double)Hitpoints; } }

        public ActorRarity Rarity
        {
            get
            {
                if (_marker == null) return ActorRarity.Normal;
                if (_marker.IsBoss) return ActorRarity.Boss;
                switch (_marker.Rarity)
                {
                    case 0: return ActorRarity.Normal;
                    case 1: return ActorRarity.Champion;
                    case 2: return ActorRarity.Rare;
                    case 3: return ActorRarity.RareMinion;
                    case 4: return ActorRarity.Unique;
                    case 5: return ActorRarity.Hireling;
                    case 7: return ActorRarity.Boss;
                    default: return _marker.IsElite ? ActorRarity.Rare : ActorRarity.Normal;
                }
            }
        }

        public IMonsterPack Pack { get { return null; } }
        public AnimSnoEnum Animation { get { return default(AnimSnoEnum); } }
        public AcdAnimationState AnimationState { get { return default(AcdAnimationState); } }
        public bool IsQuestMonster { get { return _marker != null && _marker.IsBoss; } }
        public float DotDpsApplied { get { return 0f; } }
        public bool Frozen { get { return false; } }
        public bool Chilled { get { return false; } }
        public bool Slow { get { return false; } }
        public bool Stunned { get { return false; } }
        public bool Burrowed { get { return false; } }
        public bool Invulnerable { get { return false; } }
        public bool Hidden { get { return false; } }
        public bool Stealthed { get { return false; } }
        public bool Invisible { get { return false; } }
        public bool Blind { get { return false; } }
        public bool Bleeding { get { return false; } }
        public bool Attackable { get { return IsAlive && IsOnScreen && !Untargetable && !Invisible; } }
        public bool Illusion { get { return false; } }
        public bool Palmed { get { return false; } }
        public bool Haunted { get { return false; } }
        public bool MarkedForDeath { get { return false; } }
        public bool Locust { get { return false; } }
        public bool Strongarmed { get { return false; } }
        public bool Phoenixed { get { return false; } }
        public bool Piranhas { get { return false; } }
        public bool Cursed { get { return false; } }
        public IEnumerable<ISnoMonsterAffix> AffixSnoList
        {
            get
            {
                if (_marker == null || _marker.Affixes == null || _marker.Affixes.Count == 0)
                    return new List<ISnoMonsterAffix>();

                List<ISnoMonsterAffix> list = new List<ISnoMonsterAffix>(_marker.Affixes.Count);
                for (int i = 0; i < _marker.Affixes.Count; i++)
                {
                    uint affixVal = _marker.Affixes[i];
                    MonsterAffix affixEnum = (MonsterAffix)affixVal;
                    string ru = (i < _marker.AffixNames.Count) ? _marker.AffixNames[i] : HostSnoMonsterAffix.GetRussianName(affixEnum);
                    string en = affixEnum.ToString();
                    list.Add(new HostSnoMonsterAffix(affixEnum, ru, en));
                }
                return list;
            }
        }

        public double Priority
        {
            get { return _priority; }
            set { _priority = value; }
        }

        public int GetMonsterDensity(int range)
        {
            if (Context == null || Context.Actors == null || Context.Actors.Monsters == null) return 0;
            int count = 0;
            float rx = FloorCoordinate.X;
            float ry = FloorCoordinate.Y;
            double rangeSq = (double)range * (double)range;
            List<MonsterMarker> list = Context.Actors.Monsters;
            for (int i = 0; i < list.Count; i++)
            {
                MonsterMarker m = list[i];
                if (m.Health <= 0f) continue;
                double dx = m.X - rx;
                double dy = m.Y - ry;
                if (dx * dx + dy * dy <= rangeSq) count++;
            }
            return count;
        }

        public float GetRiftProgressionDensity(int range)
        {
            if (Context == null || Context.Actors == null || Context.Actors.Monsters == null) return 0f;
            float sum = 0f;
            float rx = FloorCoordinate.X;
            float ry = FloorCoordinate.Y;
            double rangeSq = (double)range * (double)range;
            List<MonsterMarker> list = Context.Actors.Monsters;
            for (int i = 0; i < list.Count; i++)
            {
                MonsterMarker m = list[i];
                if (m.Health <= 0f) continue;
                double dx = m.X - rx;
                double dy = m.Y - ry;
                if (dx * dx + dy * dy <= rangeSq)
                {
                    sum += m.IsBoss ? 100f : (m.IsElite ? 4.0f : 1.15f);
                }
            }
            return sum;
        }
    }

    /// <summary>Concrete implementation of ISnoMonsterAffix.</summary>
    public sealed class HostSnoMonsterAffix : ISnoMonsterAffix
    {
        private readonly MonsterAffix _affix;
        private readonly uint _id;
        private readonly string _nameRu;
        private readonly string _nameEn;

        public HostSnoMonsterAffix(MonsterAffix affix, string nameRu, string nameEn)
        {
            _affix = affix;
            _id = (uint)affix;
            _nameRu = string.IsNullOrEmpty(nameRu) ? GetRussianName(affix) : nameRu;
            _nameEn = string.IsNullOrEmpty(nameEn) ? affix.ToString() : nameEn;
        }

        public uint Id { get { return _id; } }
        public MonsterAffix Affix { get { return _affix; } }
        public string NameLocalized { get { return _nameRu; } }
        public string NameEnglish { get { return _nameEn; } }

        public static string GetRussianName(MonsterAffix affix)
        {
            switch (affix)
            {
                case MonsterAffix.Arcane: return "Чародей";
                case MonsterAffix.Avenger: return "Мститель";
                case MonsterAffix.Desecrator: return "Осквернитель";
                case MonsterAffix.Electrified: return "Электрический";
                case MonsterAffix.ExtraHealth: return "Запас здоровья";
                case MonsterAffix.Fast: return "Быстрый";
                case MonsterAffix.FireChains: return "Огненные цепи";
                case MonsterAffix.Frozen: return "Заморозка";
                case MonsterAffix.FrozenPulse: return "Ледяной импульс";
                case MonsterAffix.HealthLink: return "Связующая нить";
                case MonsterAffix.Horde: return "Орда";
                case MonsterAffix.Illusionist: return "Иллюзионист";
                case MonsterAffix.Jailer: return "Тюремщик";
                case MonsterAffix.Juggernaut: return "Джаггернаут";
                case MonsterAffix.Knockback: return "Отбрасывание";
                case MonsterAffix.MissileDampening: return "Замедление снарядов";
                case MonsterAffix.Molten: return "Раскаленный след";
                case MonsterAffix.Mortar: return "Мортира";
                case MonsterAffix.Necrotic: return "Некротик";
                case MonsterAffix.Nightmarish: return "Кошмарный";
                case MonsterAffix.Orbiter: return "Орбитальный";
                case MonsterAffix.Plagued: return "Чумной";
                case MonsterAffix.Poison: return "Яд";
                case MonsterAffix.Reflect: return "Отражение урона";
                case MonsterAffix.Shielding: return "Защитная сфера";
                case MonsterAffix.Teleporter: return "Телепортация";
                case MonsterAffix.Thunderstorm: return "Грозовая буря";
                case MonsterAffix.Vampiric: return "Вампиризм";
                case MonsterAffix.Vortex: return "Вихрь";
                case MonsterAffix.Waller: return "Стена";
                case MonsterAffix.Wormhole: return "Червоточина";
                case MonsterAffix.Enervating: return "Истощение";
                default: return affix.ToString();
            }
        }
    }

    /// <summary>An item on the floor or in the backpack.</summary>
    public class HostItem : HostActor, IItem
    {
        private readonly int _gbid;
        private readonly int _quality;
        private readonly int _rank;
        private readonly string _name;
        private readonly float _x;
        private readonly float _y;
        private readonly int _column;
        private readonly int _row;
        private readonly bool _onFloor;

        public HostItem(HostContext context, HostSnoController sno,
                        int gbid, int quality, int rank, string name,
                        float x, float y, int column, int row, bool onFloor)
            : base(context, sno)
        {
            _gbid = gbid;
            _quality = quality;
            _rank = rank;
            _name = name ?? "";
            _x = x;
            _y = y;
            _column = column;
            _row = row;
            _onFloor = onFloor;
        }

        public override IWorldCoordinate FloorCoordinate
        {
            get
            {
                if (_onFloor) return new WorldCoordinate(_x, _y, 0f);
                if (Context != null) return new WorldCoordinate(Context.PlayerX, Context.PlayerY, 0f);
                return new WorldCoordinate();
            }
        }

        public override uint AcdId { get { return _gbid < 0 ? 0u : (uint)_gbid; } }

        public int AncientRank { get { return Math.Max(0, Math.Min(2, _rank)); } }
        public int CaldesannRank { get { return 0; } }
        public int JewelRank { get { return 0; } }
        public bool AccountBound { get { return true; } }
        public bool BoundToMyAccount { get { return true; } }
        public bool VendorBought { get { return false; } }

        public ISnoItem SnoItem
        {
            get
            {
                if (Sno != null && _gbid > 0) return Sno.SnoItem("item:" + _gbid, (uint)_gbid);
                // A plugin that asks for the item of a loot marker would get
                // null here and fall over, so a safe default is returned.
                return HostStubs.Get<ISnoItem>();
            }
        }

        private string ResolvedName
        {
            get
            {
                if (!string.IsNullOrEmpty(_name)) return _name;
                if (Context == null) return "";
                try { return Context.ItemName(_gbid < 0 ? 0u : (uint)_gbid) ?? ""; }
                catch { return ""; }
            }
        }

        private string RankedName
        {
            get
            {
                string name = ResolvedName;
                if (name.Length == 0) return name;
                if (AncientRank == 2 && name.IndexOf("Primal", StringComparison.OrdinalIgnoreCase) < 0)
                    return "Primal " + name;
                if (AncientRank == 1 && name.IndexOf("Ancient", StringComparison.OrdinalIgnoreCase) < 0)
                    return "Ancient " + name;
                return name;
            }
        }

        public string FullNameLocalized { get { return RankedName; } }
        public string FullNameEnglish { get { return RankedName; } }
        public int InventoryX { get { return _column; } }
        public int InventoryY { get { return _row; } }
        public bool IsNormal { get { return _quality >= 0 && _quality <= 2; } }
        public bool IsMagic { get { return _quality >= 3 && _quality <= 5; } }
        public bool IsRare { get { return _quality >= 6 && _quality <= 8; } }
        public bool IsLegendary { get { return _quality == 9; } }
        public uint SetSno { get { return 0; } }
        public int CountsIntoSet(uint setId) { return 0; }
        public bool IsInventoryLocked { get { return false; } }
        public ItemKeepDecision KeepDecision { get { return ItemKeepDecision.LooksGood; } }
        public ItemLocation Location { get { return _onFloor ? ItemLocation.Floor : ItemLocation.Inventory; } }
        public bool SeenInInventory { get { return !_onFloor; } }
        public double Perfection { get { return 0d; } }
        public ItemQuality Quality { get { return (ItemQuality)Math.Max(0, Math.Min(9, _quality)); } }
        public long Quantity { get { return 1; } }
        public string RareName { get { return ResolvedName; } }
        public int Seed { get { return 0; } }
        public int SocketCount { get { return 0; } }
        public IItem SocketedInto { get { return null; } }
        public ISnoSocketedEffect GetSocketedEffect() { return null; }
        public bool Unidentified { get { return false; } }
        public ISnoItemAffix[] Affixes { get { return new ISnoItemAffix[0]; } }
        public IItemPerfection[] Perfections { get { return new IItemPerfection[0]; } }
        public IItem[] ItemsInSocket { get { return new IItem[0]; } }
        public IEnumerable<IItemStat> StatList { get { return new List<IItemStat>(); } }
        public uint EnchantedAffixOriginal { get { return 0; } }
        public uint EnchantedAffixNew { get { return 0; } }
        public int EnchantedAffixCounter { get { return 0; } }
        public string ItemUniqueId { get { return ""; } }
    }

    /// <summary>A scene of the current world.</summary>
    public class HostScene : IScene
    {
        private readonly uint _worldSno;

        public HostScene(uint worldSno)
        {
            _worldSno = worldSno;
        }

        public ISnoScene SnoScene { get { return null; } }
        public ISnoArea SnoArea { get { return null; } }
        public uint WorldSno { get { return _worldSno; } }
        public uint NavMeshId { get { return 0; } }
        public uint SceneId { get { return 0; } }
        public float PosX { get { return 0f; } }
        public float PosY { get { return 0f; } }
        public float MaxX { get { return 0f; } }
        public float MaxY { get { return 0f; } }
        public float W { get { return 0f; } }
        public float H { get { return 0f; } }
        public float Z { get { return 0f; } }
        public string CalculatedPosId { get { return ""; } }
    }

    /// <summary>
    /// One buff of the local player. One instance per power SNO is kept by
    /// HostBuffCache and updated with every snapshot, so the watches measure
    /// real durations.
    ///
    /// Array contract (the Default BuffLists rely on it): IconCounts,
    /// TimeElapsedSeconds and TimeLeftSeconds are always exactly
    /// HostSnoPower.IconSlots long, the same length as SnoPower.Icons, and
    /// never null.
    /// </summary>
    public class HostBuff : IBuff
    {
        private IPlayer _player;
        private ISnoPower _power;
        private BuffEntry _entry;
        private readonly Watch _firstActive;
        private readonly Watch _lastActive;
        private int[] _iconCounts;
        private readonly double[] _timeElapsed;
        private readonly double[] _timeLeft;

        public HostBuff(IPlayer player, ISnoPower power, BuffEntry entry)
        {
            _firstActive = new Watch();
            _lastActive = new Watch();
            _timeElapsed = new double[HostSnoPower.IconSlots];
            // The snapshot has no expiration timestamp. Zero is the API's
            // safe "unknown/expired" value rather than a fabricated timer.
            _timeLeft = new double[HostSnoPower.IconSlots];
            _iconCounts = new int[HostSnoPower.IconSlots];
            if (entry == null || !entry.Active)
            {
                _firstActive.Stop();
                _lastActive.Stop();
            }
            Apply(player, power, entry);
        }

        /// <summary>Takes the state of a newer snapshot for the same power.</summary>
        internal void Update(IPlayer player, ISnoPower power, BuffEntry entry)
        {
            bool wasActive = Active;
            bool nowActive = entry != null && entry.Active;
            if (nowActive && !wasActive)
            {
                // Newly applied: elapsed time starts from zero.
                _firstActive.Restart();
                _lastActive.Restart();
            }
            else if (nowActive)
            {
                // LastActive measures the time since the buff was last seen
                // active, so it restarts while the buff stays active ...
                _lastActive.Restart();
            }
            else if (wasActive)
            {
                // ... and keeps running after it ends. FirstActive freezes at
                // the total active duration.
                _firstActive.Stop();
                _lastActive.Restart();
            }
            Apply(player, power, entry);
        }

        private void Apply(IPlayer player, ISnoPower power, BuffEntry entry)
        {
            _player = player;
            _power = power;
            _entry = entry;
            // A fresh array per snapshot (not per read): plugins read
            // IconCounts many times per frame, and a reader holding the old
            // array never sees it change under it.
            int[] counts = new int[HostSnoPower.IconSlots];
            if (entry != null && entry.Active)
            {
                // The buff module reports Stacks=0 for a buff that has no
                // stack attribute. TurboHUD counts such an active buff as 1,
                // and the Default rules use MinimumIconCount=1, so 0 would
                // hide every stackless buff (shrines, pylons).
                counts[0] = Math.Max(1, Math.Min(255, entry.Stacks));
            }
            _iconCounts = counts;
        }

        public IPlayer Player { get { return _player; } }
        public ISnoPower SnoPower { get { return _power; } }
        public IWatch FirstActive { get { return _firstActive; } }
        public IWatch LastActive { get { return _lastActive; } }
        public bool Active { get { BuffEntry entry = _entry; return entry != null && entry.Active; } }

        public int[] IconCounts { get { return _iconCounts; } }

        public double[] TimeElapsedSeconds
        {
            get
            {
                // Only slot 0 is known; it is refreshed on every read because
                // the watch keeps running between snapshots.
                _timeElapsed[0] = Active ? _firstActive.ElapsedMilliseconds / 1000.0 : 0d;
                return _timeElapsed;
            }
        }

        public double[] TimeLeftSeconds { get { return _timeLeft; } }

        public string Element { get { BuffEntry entry = _entry; return entry == null || entry.Element == null ? "" : entry.Element; } }
    }

    /// <summary>A player's headstone.</summary>
    public class HostHeadStone : HostActor, IHeadStone
    {
        private readonly IPlayer _player;

        public HostHeadStone(HostContext context, HostSnoController sno, IPlayer player)
            : base(context, sno)
        {
            _player = player;
        }

        public uint PlayerActorAnnId { get { return _player == null ? 0u : _player.AnnId; } }
        public IPlayer Player { get { return _player; } }
    }
}
