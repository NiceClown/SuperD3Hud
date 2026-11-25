using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SuperD3Hud
{
    /// <summary>
    /// Builds a human readable description for every plugin file.
    ///
    /// Most plugin sources in the wild carry no documentation at all, so the
    /// description is derived from three sources, in order of priority:
    ///
    /// 1. an explicit override in config\plugin_descriptions.json, which the
    ///    user can edit;
    /// 2. the &lt;summary&gt; comment of the class, when present;
    /// 3. the class name, its base class, the interfaces it implements and the
    ///    game data it touches.
    ///
    /// The result is cached in config\plugin_descriptions.json on first scan,
    /// so every plugin has an editable description.
    /// </summary>
    public static class PluginCatalog
    {
        private static readonly Regex InterfaceRegex =
            new Regex(@"\bI[A-Z][A-Za-z0-9]*(?:Handler|Painter|Decorator|Plugin|Test|Transformator)\b");

        private static readonly Dictionary<string, string> Curated =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // --- Общее -------------------------------------------------
                { "BasePlugin", "Базовый класс всех плагинов. Сам ничего не делает: от него наследуются остальные." },
                { "BaseTool", "Вспомогательный инструмент плагинов. Не является самостоятельным плагином." },
                { "PublicClassPlugin", "Служебный класс с общими методами для остальных плагинов." },
                { "LinqExtensions", "Набор вспомогательных методов для работы с коллекциями. Не плагин." },
                { "DebugPlugin", "Отладочный вывод: печатает служебные данные в журнал HUD. Обычно держат выключенным." },
                { "TestContext", "Служебный контекст для тестовых условий автоприменения навыков." },
                { "SkillBuild", "Описание сборки навыков: набор навыков и рун для одной специализации." },
                { "SkillBuildSetter", "Переключает сборку навыков по горячей клавише или событию." },
                { "ValueFormat", "Форматирование чисел для подписей: сокращения «k», «M» и проценты." },
                { "Timer", "Простой таймер для отслеживания интервалов внутри других плагинов." },

                // --- Отрисовка и декораторы --------------------------------
                { "BuffPainter", "Рисует иконки баффов с таймером и подписями." },
                { "BuffPaintInfo", "Данные одного баффа для отрисовки: иконка, стек, оставшееся время." },
                { "BuffRule", "Правило, по которому бафф попадает в список отображаемых." },
                { "BuffRuleCalculator", "Считает, какие баффы сейчас активны и сколько им осталось." },
                { "SkillPainter", "Рисует иконки навыков с кулдауном и зарядом." },
                { "IItemRenderer", "Интерфейс отрисовки предмета в списках и подсказках." },
                { "StandardItemRenderer", "Стандартная отрисовка предмета: рамка редкости, иконка, название." },
                { "IFader", "Интерфейс плавного появления и исчезновения элементов." },
                { "StandardFader", "Стандартное плавное затухание элементов интерфейса." },
                { "IMovable", "Интерфейс перетаскиваемого элемента интерфейса." },
                { "MovableArea", "Область, которую пользователь может перетащить мышью." },
                { "MovableController", "Управляет перетаскиванием и запоминает позицию элементов." },
                { "IMovableKeyEventHandler", "Обработка клавиш для перетаскиваемых элементов." },
                { "ResizeMode", "Режим изменения размера элемента интерфейса." },
                { "IRotationTransformator", "Интерфейс поворота фигуры на карте." },
                { "CircularRotationTransformator", "Вращает фигуру по кругу — для индикаторов направления." },
                { "IRadiusTransformator", "Интерфейс изменения радиуса фигуры." },
                { "StandardPingRadiusTransformator", "Пульсирующий радиус: фигура плавно расширяется и сжимается." },
                { "IShapePainter", "Интерфейс рисования фигуры на земле." },
                { "IWorldShapePainter", "Интерфейс рисования фигуры в мировых координатах." },
                { "IWorldDecorator", "Интерфейс декоратора, который рисует объекты в мире." },
                { "IWorldDecoratorWithRadius", "Декоратор мира с настраиваемым радиусом." },
                { "WorldDecoratorCollection", "Набор декораторов мира, которые рисуются вместе." },
                { "CircleShapePainter", "Рисует круг на земле — радиус способности или опасной зоны." },
                { "CrossShapePainter", "Рисует крест на земле — метка точки или направления." },
                { "DoorShapePainter", "Рисует контур двери или прохода." },
                { "LineFromMeShapePainter", "Рисует линию от персонажа к цели." },
                { "PlusShapePainter", "Рисует знак «плюс» на земле." },
                { "RectangleShapePainter", "Рисует прямоугольник на земле — зона способности." },
                { "RotatingTriangleShapePainter", "Рисует вращающийся треугольник — указатель направления." },
                { "TriangleShapePainter", "Рисует треугольник на земле." },
                { "WellShapePainter", "Рисует окружность колодца или похожего объекта." },
                { "WorldStarShapePainter", "Рисует звезду в мире — метка важной точки." },
                { "GroundCircleDecorator", "Круг на земле с подписью: радиус и текст." },
                { "GroundLabelDecorator", "Подпись на земле под объектом." },
                { "GroundShapeDecorator", "Фигура на земле вокруг объекта." },
                { "GroundTimerDecorator", "Круговой таймер на земле — оставшееся время эффекта." },
                { "TopLabelDecorator", "Подпись в верхней части экрана со значением и цветом." },
                { "TopLabelWithTitleDecorator", "Подпись с заголовком в верхней части экрана." },
                { "MapLabelDecorator", "Подпись на миникарте." },
                { "MapShapeDecorator", "Фигура на миникарте." },
                { "MapTextureDecorator", "Текстура или иконка на миникарте." },
                { "AbstractMapDecoratorWithRadius", "Базовый декоратор миникарты с радиусом. Сам по себе не используется." },
                { "AbstractTopLabelList", "Базовый список подписей в верхней части экрана." },
                { "HorizontalTopLabelList", "Список подписей, выстроенный по горизонтали." },
                { "VerticalTopLabelList", "Список подписей, выстроенный по вертикали." },
                { "GroundLabelDecoratorPainterPlugin", "Рисует подписи на земле для всех декораторов." },
                { "PandemoniumIconA", "Иконка для события Пандемониума." },

                // --- Списки баффов -----------------------------------------
                { "MiniMapLeftBuffListPlugin", "Список баффов слева от миникарты." },
                { "MiniMapRightBuffListPlugin", "Список баффов справа от миникарты." },
                { "PlayerBottomBuffListPlugin", "Список баффов под портретом персонажа." },
                { "PlayerLeftBuffListPlugin", "Список баффов слева от портрета персонажа." },
                { "PlayerRightBuffListPlugin", "Список баффов справа от портрета персонажа." },
                { "PlayerTopBuffListPlugin", "Список баффов над портретом персонажа." },
                { "TopLeftBuffListPlugin", "Список баффов в левом верхнем углу экрана." },
                { "TopRightBuffListPlugin", "Список баффов в правом верхнем углу экрана." },
                { "ConventionOfElementsBuffListPlugin", "Показывает текущую стихию Convention of Elements и время до смены." },
                { "CheatDeathBuffFeederPlugin", "Передаёт срабатывание «второго шанса» в список баффов." },

                // --- Инвентарь и город -------------------------------------
                { "InventoryAndStashPlugin", "Показывает содержимое инвентаря и сундука с редкостью и статами." },
                { "InventoryFreeSpacePlugin", "Счётчик свободных ячеек инвентаря." },
                { "InventoryKanaiCubedItemsPlugin", "Показывает предметы, извлечённые в Кубе Канаи." },
                { "InventoryMaterialCountPlugin", "Счётчик материалов: души, пыль, кристаллы и прочее." },
                { "StashPreviewPlugin", "Предпросмотр содержимого сундука без его открытия." },
                { "StashUsedSpacePlugin", "Показывает, сколько места занято в сундуке." },
                { "SalvagePlugin", "Подсвечивает предметы, которые можно разобрать у кузнеца." },
                { "KadalaPlugin", "Подсказки при азарте у Кадалы: какие предметы стоит покупать." },
                { "EnchantPlugin", "Подсказки при перековке предметов у ювелира." },
                { "ConvertMaterialPlugin", "Помогает при конвертации материалов." },
                { "UpgradeLegendaryGemPlugin", "Подсказки при прокачке легендарных камней у Урши." },
                { "UpgradeRareToLengendaryPlugin", "Помогает превращать редкие предметы в легендарные в Кубе." },
                { "UpgradeToAncientPlugin", "Подсказки при перековке предмета в древний." },
                { "ThrowItemPlugin", "Выбрасывает ненужные предметы по заданным правилам." },
                { "PickUpPlugin", "Подсвечивает предметы, которые стоит поднять." },
                { "PickupRangePlugin", "Показывает радиус подбора предметов." },
                { "HoveredItemInfoPlugin", "Показывает подробную информацию о предмете под курсором." },
                { "ItemsPlugin", "Помечает на земле предметы, которые подходят под фильтр." },
                { "CosmeticItemsPlugin", "Помечает косметические предметы и питомцев." },
                { "ChestPlugin", "Помечает сундуки на земле." },
                { "ClickableChestGizmoPlugin", "Помечает интерактивные сундуки и механизмы." },
                { "OpenChestPlugin", "Автоматически открывает сундук под курсором." },
                { "RackPlugin", "Помечает стойки с оружием и доспехами." },
                { "DeadBodyPlugin", "Помечает трупы, с которых можно получить добычу." },
                { "GlobePlugin", "Помечает сферы здоровья." },
                { "ShrinePlugin", "Помечает алтари и их тип." },
                { "PortalPlugin", "Помечает порталы и указывает направление." },
                { "OculusPlugin", "Помечает круги Окулюса, которые усиливают урон." },
                { "GoblinPlugin", "Помечает гоблинов и подсказывает, где они находятся." },
                { "BannerPlugin", "Помечает знамёна игроков в мире." },
                { "HeadStonePlugin", "Помечает надгробия павших игроков." },
                { "TriunesWillPlugin", "Отслеживает эффект «Воля Триуна»." },
                { "CursedEventPlugin", "Показывает состояние проклятого события и оставшееся время." },

                // --- Монстры и бой -----------------------------------------
                { "EliteMonsterAffixPlugin", "Показывает аффиксы элитных монстров и их опасность." },
                { "EliteMonsterSkillPlugin", "Предупреждает об опасных способностях элитных монстров." },
                { "EliteNearbyTest", "Условие «рядом есть элитный монстр» для автоприменения навыков." },
                { "EliteOrBossNearbyTest", "Условие «рядом элита или босс»." },
                { "EliteOrBossNearbyCursorTest", "Условие «элита или босс под курсором»." },
                { "DangerousMonsterPlugin", "Подсвечивает монстров, которые опасны в ближнем бою." },
                { "ExplosiveMonsterPlugin", "Предупреждает о монстрах, которые взрываются после смерти." },
                { "MonsterCirclePlugin", "Рисует круг вокруг монстров, чтобы их было лучше видно." },
                { "MonsterPackPlugin", "Показывает группы монстров и их количество." },
                { "MonsterRiftProgressionColoringPlugin", "Красит полоску прогресса Великого портала по типу монстров." },
                { "StandardMonsterPlugin", "Базовая отрисовка монстров: полоса здоровья и метки." },
                { "11111EliteHpBarPlugin", "Увеличенная полоса здоровья элитных монстров с их аффиксами." },
                { "TopMonsterHealthBarPlugin", "Полоса здоровья цели в верхней части экрана." },
                { "AvoidanceDisplayPlugin", "Показывает слово «DANGER», когда персонаж стоит в опасной зоне." },
                { "BaseAvoidanceCalculator", "Базовый расчёт опасных зон. От него наследуются остальные калькуляторы." },
                { "CloseToDPSPlugin", "Подсказывает, что персонаж слишком близко к цели." },
                { "SkillRangeHelperPlugin", "Показывает радиус действия навыков." },
                { "TempestTracker", "Отслеживает стаки «Бури» и время до сброса." },
                { "RiftPlugin", "Общая информация о Великом портале: прогресс, время, уровень." },
                { "NotifyAtRiftPercentagePlugin", "Уведомляет при заданном проценте прогресса портала." },
                { "OpenGreatRiftPlugin", "Помогает открыть Великий портал у обелиска." },
                { "BountyTablePlugin", "Таблица заданий- bounty с прогрессом." },
                { "WaypointQuestsPlugin", "Показывает задания и точки перемещения на карте." },
                { "SceneHintPlugin", "Подсказки по текущей сцене: выходы, важные объекты." },
                { "MarkerPlugin", "Ставит пользовательские метки на карте." },
                { "CloseDialogPlugin", "Автоматически закрывает диалоги NPC." },
                { "CloseQuestDialogPlugin", "Автоматически закрывает окно задания." },

                // --- Интерфейс и информация --------------------------------
                { "GameInfoPlugin", "Сводка о текущем состоянии игры: сложность, уровень, время." },
                { "NetworkLatencyPlugin", "Показывает задержку сети." },
                { "BloodShardPlugin", "Показывает количество кровавых осколков." },
                { "DamageBonusPlugin", "Показывает бонус к урону от активных эффектов." },
                { "ExperienceOverBarPlugin", "Показывает получаемый опыт над полосой." },
                { "TopExperienceStatistics", "Статистика опыта: сколько получено за сессию и в час." },
                { "PortraitBottomStatsPlugin", "Статистика под портретом персонажа." },
                { "ResourceOverGlobePlugin", "Показывает ресурс поверх сферы." },
                { "OtherPlayersPlugin", "Информация о других игроках в группе." },
                { "MultiplayerExperienceRangePlugin", "Показывает, кто из группы получает опыт." },
                { "PlayerSkillPlugin", "Панель навыков персонажа с кулдаунами." },
                { "OriginalSkillBarPlugin", "Возвращает панель навыков к стандартному виду." },
                { "OriginalHealthPotionSkillPlugin", "Показывает зелье здоровья как обычный навык." },
                { "UiHiddenPlayerSkillBarPlugin", "Панель навыков, которая остаётся видимой при скрытом интерфейсе." },
                { "ParagonCapturePlugin", "Делает снимок экрана при получении нового уровня парагона." },
                { "CooldownSoundPlayerPlugin", "Звуковой сигнал, когда навык готов." },
                { "DrinkHealthPotionPlugin", "Автоматически пьёт зелье здоровья при низком здоровье." },
                { "DrinkHealthPotionArchonMeteorBuildPlugin", "Пьёт зелье здоровья по правилам сборки Archon/Meteor." },
                { "AttributeLabelListPlugin", "Список атрибутов персонажа с числовыми значениями." },

                // --- Автоприменение навыков: служебное ---------------------
                { "AbstractSkillHandler", "Базовый класс автоматизации одного навыка. От него наследуются все навыки." },
                { "AbstractSkillTest", "Базовый класс условия, при котором навык применяется." },
                { "AutoSkillPlugin", "Ядро автоматизации навыков: проверяет условия и нажимает клавиши." },
                { "AttackingTest", "Условие «персонаж атакует»." },
                { "BossNearbyTest", "Условие «рядом босс»." },
                { "BuffIsAboutToExpireTest", "Условие «бафф скоро закончится»." },
                { "CanCastBuffTest", "Условие «можно применить бафф»." },
                { "CanCastSimpleTest", "Условие «можно применить навык»." },
                { "CanCastSkillTest", "Условие «навык доступен и не на кулдауне»." },
                { "CastingIdentifyTest", "Условие «персонаж опознаёт предмет»." },
                { "CastingPortalTest", "Условие «персонаж открывает портал»." },
                { "ChannelingSkillTest", "Условие «персонаж поддерживает канал»." },
                { "CoolDownRule", "Правило проверки кулдауна навыка." },
                { "CustomFalseTest", "Всегда ложное условие. Используется для отключения ветки." },
                { "CustomTrueTest", "Всегда истинное условие." },
                { "EnoughMonstersInSectorTest", "Условие «в секторе достаточно монстров»." },
                { "EnoughMonstersNearbyTest", "Условие «рядом достаточно монстров»." },
                { "EnoughMonstersNearbyCursorTest", "Условие «рядом с курсором достаточно монстров»." },
                { "ForceCastTest", "Условие «навык нужно применить принудительно»." },
                { "HealthBelowPercentageTest", "Условие «здоровье ниже заданного процента»." },
                { "HealthWarningTest", "Условие «здоровье на опасно низком уровне»." },
                { "IdleTest", "Условие «персонаж простаивает»." },
                { "InTownTest", "Условие «персонаж в городе»." },
                { "LevelingNearbyPartyMemberTest", "Условие «рядом участник группы, который качается»." },
                { "NearbyPartyMemberIsInDangerTest", "Условие «участник группы в опасности»." },
                { "OnCooldownTest", "Условие «навык на кулдауне»." },
                { "PrimaryResourceAmountIsAboveTest", "Условие «основного ресурса больше N»." },
                { "PrimaryResourceAmountIsBelowTest", "Условие «основного ресурса меньше N»." },
                { "PrimaryResourcePercentageIsAboveTest", "Условие «основного ресурса больше N%»." },
                { "PrimaryResourcePercentageIsBelowTest", "Условие «основного ресурса меньше N%»." },
                { "PrimaryResourceRequirementTest", "Условие «хватает основного ресурса на навык»." },
                { "SecondaryResourceAmountIsAboveTest", "Условие «вторичного ресурса больше N»." },
                { "SecondaryResourceAmountIsBelowTest", "Условие «вторичного ресурса меньше N»." },
                { "SecondaryResourcePercentageIsAboveTest", "Условие «вторичного ресурса больше N%»." },
                { "SecondaryResourcePercentageIsBelowTest", "Условие «вторичного ресурса меньше N%»." },
                { "SecondaryResourceRequirementTest", "Условие «хватает вторичного ресурса на навык»." },
                { "RunningTest", "Условие «персонаж бежит»." },
                { "SpecificBuffIsAboutToExpireTest", "Условие «конкретный бафф скоро закончится»." },
                { "SpecificBuffIsAboutToExpireOnPartyTest", "Условие «у участника группы бафф скоро закончится»." },
                { "SpecificBuffIsActiveTest", "Условие «конкретный бафф активен»." },
                { "SpecificBuffIsActiveOnPartyTest", "Условие «у участника группы конкретный бафф активен»." },
                { "SpecificSkillOnCooldownTest", "Условие «конкретный навык на кулдауне»." },
                { "BaseInventoryManagementPlugin", "Базовый класс плагинов управления инвентарём." },
                { "BaseKanaiPlugin", "Базовый класс плагинов Куба Канаи." },
                { "ILeftClickHandler", "Интерфейс обработки левого клика." },
                { "IRightClickHandler", "Интерфейс обработки правого клика." },
                { "ClickEventHandler", "Обрабатывает клики по элементам интерфейса HUD." },
            };

        // ------------------------------------------------------------------
        // Class and skill names
        // ------------------------------------------------------------------

        private static readonly Dictionary<string, string> Classes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Barbarian", "Варвар" },
                { "Crusader", "Крестоносец" },
                { "DemonHunter", "Охотник на демонов" },
                { "Monk", "Монах" },
                { "Nec", "Некромант" },
                { "Necromancer", "Некромант" },
                { "WitchDoctor", "Колдун" },
                { "Wizard", "Чародей" }
            };

        private static readonly Dictionary<string, string> Skills =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Варвар
                { "AncientSpear", "Древнее копьё" },
                { "BandofMight", "Повязка силы" },
                { "BattleRage", "Боевой клич" },
                { "CallOfTheAncients", "Зов древних" },
                { "IgnorePain", "Игнорирование боли" },
                { "Leap", "Прыжок" },
                { "OverPower", "Сокрушение" },
                { "Rend", "Разрыв" },
                { "SeismicSlam", "Сейсмический удар" },
                { "Sprint", "Рывок" },
                { "ThreateningShout", "Устрашающий крик" },
                { "WarCry", "Боевой клич" },
                { "Whirlwind", "Вихрь" },
                { "WrathOfTheBerserker", "Гнев берсерка" },
                // Крестоносец
                { "AkaratsChampion", "Чемпион Акарата" },
                { "BlessedShield", "Благословенный щит" },
                { "Bombardment", "Бомбардировка" },
                { "Condemn", "Осуждение" },
                { "FallingSword", "Падающий меч" },
                { "FistoftheHeavens", "Длань небес" },
                { "IronSkin", "Железная кожа" },
                { "Judgment", "Приговор" },
                { "Justice", "Правосудие" },
                { "LawsOfHope", "Закон надежды" },
                { "LawsOfJustice", "Закон справедливости" },
                { "LawsOfValor", "Закон доблести" },
                { "Phalanx", "Фаланга" },
                { "Provoke", "Провокация" },
                { "ShieldGlare", "Сияние щита" },
                { "SteedCharge", "Натиск скакуна" },
                // Охотник на демонов
                { "Bolas", "Болас" },
                { "Caltrops", "Шипы" },
                { "ChakramShurikenCloud", "Чакрам: облако сюрикенов" },
                { "Companion", "Спутник" },
                { "EntanglingShot", "Опутывающий выстрел" },
                { "EvasiveFire", "Уклончивый выстрел" },
                { "FanofKnives", "Веер клинков" },
                { "HungeringArrow", "Голодная стрела" },
                { "Impale", "Пронзающий выстрел" },
                { "MarkedforDeath", "Метка смерти" },
                { "Multishot", "Залп" },
                { "Preparation", "Подготовка" },
                { "PreparationPunishment", "Подготовка: кара" },
                { "RainofVengeance", "Дождь возмездия" },
                { "Sentry", "Часовой" },
                { "ShadowPower", "Сила тени" },
                { "SmokeScreen", "Дымовая завеса" },
                { "StrafeHungeringArrow", "Стрельба на бегу + голодная стрела" },
                { "Vengeance", "Возмездие" },
                // Монах
                { "BlindingFlash", "Ослепляющая вспышка" },
                { "BreathOfHeaven", "Дыхание небес" },
                { "CripplingWave", "Калечащая волна" },
                { "CycloneStrike", "Удар циклона" },
                { "DashingStrike", "Стремительный удар" },
                { "DeadlyReach", "Смертельная досягаемость" },
                { "Epiphany", "Озарение" },
                { "FistsOfThunder", "Кулаки грома" },
                { "InnerSanctuary", "Внутреннее святилище" },
                { "MantraOfConviction", "Мантра убеждения" },
                { "MantraOfHealing", "Мантра исцеления" },
                { "MantraOfSalvation", "Мантра спасения" },
                { "MysticAlly", "Мистический союзник" },
                { "Serenity", "Безмятежность" },
                { "SweepingWind", "Порыв ветра" },
                { "TempestRush", "Натиск бури" },
                { "WayOfTheHundredFists", "Путь ста кулаков" },
                // Некромант
                { "ArmyoftheDead", "Армия мёртвых" },
                { "BloodRush", "Кровавый рывок" },
                { "BoneArmor", "Костяная броня" },
                { "BoneSpear", "Костяное копьё" },
                { "BoneSpikes", "Костяные шипы" },
                { "CommandSkeletons", "Приказ скелетам" },
                { "Decrepify", "Дряхление" },
                { "Devour", "Пожирание" },
                { "Frailty", "Хрупкость" },
                { "LandOfTheDead", "Земля мёртвых" },
                { "Leech", "Высасывание" },
                { "NayrsBlackDeath", "Чёрная смерть Найра" },
                { "DeathNova", "Взрыв смерти" },
                { "Revive", "Оживление" },
                { "SiphonBlood", "Вытягивание крови" },
                { "SkeletalMage", "Скелет-маг" },
                { "Simulacrum", "Симулякр" },
                // Колдун
                { "BigBadVoodoo", "Большое вуду" },
                { "CorpseSpiders", "Трупные пауки" },
                { "FetishArmy", "Армия фетишей" },
                { "Gargantuan", "Гаргантюан" },
                { "Hex", "Порча" },
                { "Horrify", "Ужас" },
                { "LocustSwarm", "Рой саранчи" },
                { "MassConfusion", "Массовое замешательство" },
                { "Piranhas", "Пираньи" },
                { "Sacrifice", "Жертва" },
                { "SoulHarvest", "Жатва душ" },
                { "JadeHarvester", "Изумрудная жница" },
                { "SpiritBarrage", "Залп духов" },
                { "Phantasm", "Фантазм" },
                { "SpiritWalk", "Прогулка духов" },
                { "WallOfDeath", "Стена смерти" },
                { "ZombieDogs", "Зомби-псы" },
                { "Team", "команда" },
                // Чародей
                { "ArcaneOrb", "Тайная сфера" },
                { "Archon", "Архонт" },
                { "ArchonArcaneBlast", "Архонт: тайный взрыв" },
                { "ArchonArcaneBlastCold", "Архонт: тайный взрыв (холод)" },
                { "ArchonArcaneBlastFire", "Архонт: тайный взрыв (огонь)" },
                { "ArchonArcaneBlastLightning", "Архонт: тайный взрыв (молния)" },
                { "ArchonSlowTime", "Архонт: замедление времени" },
                { "BlackHole", "Чёрная дыра" },
                { "Blizzard", "Метель" },
                { "DiamondSkin", "Алмазная кожа" },
                { "Disintegrate", "Дезинтеграция" },
                { "Electrocute", "Электрошок" },
                { "EnergyArmor", "Энергетическая броня" },
                { "ExplosiveBlast", "Взрывной удар" },
                { "Familiar", "Фамильяр" },
                { "FrostNova", "Кольцо холода" },
                { "Hydra", "Гидра" },
                { "IceArmor", "Ледяная броня" },
                { "MagicWeapon", "Магическое оружие" },
                { "Meteor", "Метеор" },
                { "MirrorImage", "Зеркальное отражение" },
                { "StormArmor", "Штормовая броня" },
                { "Triumvirate", "Триумвират" },
                { "MagicMissile", "Магическая стрела" },
                { "ShockPulse", "Ударный импульс" },
                { "SpectralBlade", "Призрачный клинок" },
                { "WaveofForce", "Волна силы" }
            };

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        private static Dictionary<string, string> _overrides;
        private static readonly object _sync = new object();

        private static string OverridesPath
        {
            get { return Path.Combine(AppPaths.ConfigDir, "plugin_descriptions.json"); }
        }

        /// <summary>
        /// Returns a description for the plugin, using the override file, the
        /// summary comment or the analysis of the class name.
        /// </summary>
        public static string Describe(PluginInfo info)
        {
            if (info == null) return "";

            Dictionary<string, string> overrides = Overrides();
            string custom;
            if (overrides.TryGetValue(info.Name, out custom) && !string.IsNullOrEmpty(custom))
                return custom;

            if (!string.IsNullOrEmpty(info.Description) && !IsFallback(info.Description))
                return info.Description;

            string text = CuratedDescription(info.Name);
            if (!string.IsNullOrEmpty(text)) return text;

            return DerivedDescription(info);
        }

        private static bool IsFallback(string description)
        {
            return description.StartsWith("Описание в файле не найдено", StringComparison.OrdinalIgnoreCase);
        }

        private static string CuratedDescription(string className)
        {
            string text;
            if (Curated.TryGetValue(className, out text)) return text;

            // A plugin named after a class and a skill, e.g. MonkSweepingWindPlugin.
            foreach (KeyValuePair<string, string> pair in Classes)
            {
                if (!className.StartsWith(pair.Key, StringComparison.OrdinalIgnoreCase)) continue;
                string rest = className.Substring(pair.Key.Length);
                if (!rest.EndsWith("Plugin", StringComparison.OrdinalIgnoreCase)) continue;
                rest = rest.Substring(0, rest.Length - "Plugin".Length);
                if (rest.Length == 0) continue;

                string skill;
                if (Skills.TryGetValue(rest, out skill))
                    return pair.Value + ": автоматизация навыка «" + skill + "».";

                // Names with a set suffix, e.g. NayrsBlackDeath_BoneArmor.
                int underscore = rest.IndexOf('_');
                if (underscore > 0)
                {
                    string set = rest.Substring(0, underscore);
                    string part = rest.Substring(underscore + 1);
                    string setText;
                    string partText;
                    if (Skills.TryGetValue(set, out setText) && Skills.TryGetValue(part, out partText))
                        return pair.Value + ": автоматизация связки «" + setText + "» + «" + partText + "».";
                }

                return pair.Value + ": автоматизация навыка «" + Humanize(rest) + "».";
            }
            return "";
        }

        /// <summary>
        /// Fallback: describe the plugin from its name, base class and the
        /// interfaces it implements.
        /// </summary>
        private static string DerivedDescription(PluginInfo info)
        {
            string name = info.Name ?? "";
            string body = StripSuffix(name, "Plugin");

            // Pure infrastructure classes.
            if (name.StartsWith("I", StringComparison.Ordinal) && name.Length > 2 &&
                char.IsUpper(name[1]))
                return "Интерфейс для других плагинов. Сам по себе не выполняется.";

            if (name.EndsWith("Test", StringComparison.OrdinalIgnoreCase))
                return "Условие для системы автоприменения навыков: «" + Humanize(StripSuffix(body, "Test")) + "».";

            if (name.EndsWith("Decorator", StringComparison.OrdinalIgnoreCase))
                return "Декоратор: рисует элемент интерфейса «" + Humanize(StripSuffix(body, "Decorator")) + "».";

            if (name.EndsWith("Painter", StringComparison.OrdinalIgnoreCase))
                return "Рисует фигуру «" + Humanize(StripSuffix(body, "Painter")) + "» в мире или на миникарте.";

            if (name.EndsWith("Transformator", StringComparison.OrdinalIgnoreCase))
                return "Преобразование фигуры «" + Humanize(StripSuffix(body, "Transformator")) + "» при отрисовке.";

            if (name.EndsWith("Handler", StringComparison.OrdinalIgnoreCase))
                return "Обработчик «" + Humanize(StripSuffix(body, "Handler")) + "» для других плагинов.";

            string category = info.Category ?? "";
            string where = "";
            if (category.IndexOf("SkillHandlers", StringComparison.OrdinalIgnoreCase) >= 0)
                where = " Автоматизация навыка для боевой сборки.";
            else if (category.IndexOf("Avoidance", StringComparison.OrdinalIgnoreCase) >= 0)
                where = " Относится к системе избегания опасных зон.";

            return "Плагин «" + Humanize(body) + "»." + where;
        }

        private static string StripSuffix(string text, string suffix)
        {
            if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return text.Substring(0, text.Length - suffix.Length);
            return text;
        }

        /// <summary>
        /// Splits a CamelCase identifier into readable words and keeps the
        /// original Latin name, which is easier to match with a guide.
        /// </summary>
        private static string Humanize(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return "";

            StringBuilder text = new StringBuilder(identifier.Length + 8);
            for (int i = 0; i < identifier.Length; i++)
            {
                char c = identifier[i];
                if (i > 0)
                {
                    char previous = identifier[i - 1];
                    bool boundary =
                        (char.IsUpper(c) && !char.IsUpper(previous)) ||
                        (char.IsUpper(c) && i + 1 < identifier.Length && char.IsLower(identifier[i + 1]) && char.IsUpper(previous)) ||
                        (char.IsDigit(c) && !char.IsDigit(previous));
                    if (boundary) text.Append(' ');
                }
                if (c == '_') text.Append(' ');
                else text.Append(c);
            }
            return text.ToString().Trim();
        }

        // ------------------------------------------------------------------
        // Overrides file
        // ------------------------------------------------------------------

        private static Dictionary<string, string> Overrides()
        {
            lock (_sync)
            {
                if (_overrides != null) return _overrides;
                _overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (File.Exists(OverridesPath))
                    {
                        string json = File.ReadAllText(OverridesPath, Encoding.UTF8);
                        Dictionary<string, string> loaded =
                            JsonStore.Deserialize<Dictionary<string, string>>(json);
                        if (loaded != null)
                        {
                            foreach (KeyValuePair<string, string> pair in loaded)
                                _overrides[pair.Key] = pair.Value;
                        }
                    }
                }
                catch { }
                return _overrides;
            }
        }

        /// <summary>Forgets the cached override file, e.g. after the user edited it.</summary>
        public static void Invalidate()
        {
            lock (_sync) _overrides = null;
        }

        /// <summary>
        /// Writes a description for every plugin that has none, so the whole
        /// catalogue is documented and editable in one place.
        /// </summary>
        public static int FillMissingDescriptions(List<PluginInfo> plugins)
        {
            if (plugins == null) return 0;

            Dictionary<string, string> overrides = Overrides();
            int added = 0;

            foreach (PluginInfo info in plugins)
            {
                if (info == null || string.IsNullOrEmpty(info.Name)) continue;
                if (overrides.ContainsKey(info.Name)) continue;

                string text = Describe(info);
                if (string.IsNullOrEmpty(text)) continue;
                if (IsFallback(text)) continue;
                overrides[info.Name] = text;
                added++;
            }

            if (added > 0) SaveOverrides(overrides);
            return added;
        }

        private static void SaveOverrides(Dictionary<string, string> overrides)
        {
            try
            {
                // Sorted output makes the file easy to review and diff.
                SortedDictionary<string, string> sorted =
                    new SortedDictionary<string, string>(overrides, StringComparer.OrdinalIgnoreCase);
                JsonStore.Save(OverridesPath, sorted);
            }
            catch { }
        }
    }
}
