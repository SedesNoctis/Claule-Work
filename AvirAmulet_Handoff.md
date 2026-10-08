# Авийский традиционный амулет — состояние на 4 октября 2026

## Где что лежит
- `Source/Avir/Artifactorics/AvirAmulet.cs` — дефы каст и подклассов, компонент амулета, графика (маска), рецепты, счёт сердечника, запрет ношения для не-Авир.
- `AvirAmuletDevelopment.cs` — развитие: настройки (`Avir_AmuletSettings`), ветки, цены, сила, опыт, бонусы статов.
- `AvirAmuletElder.cs` / `AvirAmuletSteward.cs` / `AvirAmuletMaker.cs` — майлстоуны и слоты Воинов / Управленцев / Артефакторов (числа в начале каждого файла).
- `Dialog_AmuletDevelopment.cs` — окно развития (круговой лабиринт), `Dialog_AvirAmulets.cs` — вкладка Amulets на столе артефактора.
- XML: `Defs/Artifactorics/AvirAmulet_*.xml` (связки смежных путей — `AvirAmulet_Combos.xml`), металлы — `ArtifactMetals.xml` (поля `amulet*`).
- Текстуры: `Things/Item/Artifactorics/AvirAmulet(_m)`, `AvirAmuletNecklace(_m)`, `UI/Artifactorics/Amulet_*`, `UI/Artifactorics/Amulet/*` (слоты), `UI/Artifactorics/Paths/*` (пути артефактов).

## Как работает
- Две работы на столе артефактора: ожерелье (золото — Аскхиалис, серебро — А'Шай), затем сердечник с кастой.
- Касты: warrior (hunter, guard, fighter), governor (priest, guild trader, guild master, inquisitor), artificer (craftsman, artist, architect).
- Подкласс выбирается при первом открытии развития. 18 камней на путь, майлстоуны 3/9/12/15/18, чужие ветки касты ×2 цены.
- Майлстоун работает, как только открыт его камень на любом пути амулета. Слот (активка/пассивка) — только у основного пути.
- А'Шай: серебро + только эссенций/тиссерий, носит только Авир, касты нет — все пути, основной выбирается; опыт только от навыков основного пути и его касты.
- Смежное развитие: пути стоят по кругу (у каст — свои подклассы; у А'Шай — все 10 путей в порядке воины → управленцы → артефакторы, замкнуто). Общий коэффициент: каждый камень, открытый на обоих соседних путях (min из двух), даёт +0.5% силы обоим (`adjacencyPerStone`). Связки (`AmuletComboDef`): просыпаются, когда оба пути дошли до 9, удваиваются на 18; масштабируются силой более слабого пути. 10 связок внутри каст + 3 межкастовые только у А'Шай (боец–жрец, инквизитор–ремесленник, архитектор–охотник «Телекинетический залп»).
- А'Шай: боевой телекинез (любой воинский путь на 9), БЕЗ урона (игра с Combat Extended): шанс остановить летящий в носителя снаряд, вырвать оружие у цели, к которой бежит Авир, обезоружить стрелка, целящегося в него, сбить врага с ног на 2 с (лежит визуально). Шанс = база × психочувствительность носителя × сила пути (телекинез физический — психочувствительность цели не важна; сбить с ног труднее крупных: ×1/размер тела, не ниже 0.25; оружие механоидов встроено и не вырывается), потолок 35%. У носителя окна (один бросок на окно), у цели иммунитет после срабатывания; оружие падает на землю. «Телекинетический залп» — шансы ×1.5. Код — `AvirAmuletTelekinesis.cs`, числа — `tk*` в `Avir_AmuletSettings`. Перехват снарядов CE — по имени через рефлексию, не проверен.
- Базовая подпространственная сумка амулета — 10 кг. «Гильдейская книга» даёт +8 кг сумки за ранг.
- Описания связок ждут переписывания (согласовать с автором).
- На листе развития связки — мостики между соседними путями на кольцах 9 и 18 (подсказка при наведении); слева — коэффициенты и активные связки.
- Узор: круговой лабиринт от сердечника, кольцо каждые `rowStones` (3) камня; майлстоуны каст в своих кружках; всё закрыто общим кругом.

## Именные амулеты
- Silversong, Shadowstrider (серебро), Shadowguard (золото) — спираль А'Шай (все пути); Flameguard (золото) — всегда каста воинов; Goldensight (золото) — свои пути: инквизитор (основной, навсегда) + охотник, страж, боец. Свои способности (хедиффы, абилки, телекинез) сохранены.
- Делаются из готового традиционного амулета (нужное ожерелье; для Flameguard — каста воинов или ещё не выбранная) и забирают его металлы, качество, путь, камни и опыт (`RecipeWorker_NamedAmulet`, `CompProperties_AvirAmulet`: multiclass / fixedCaste / branches / fixedMain / requiredNecklace / requiredCaste).
- Качество: сердечник — сила всех путей (×0.8…1.2): статы клеток, майлстоуны, связки, шансы и мощь механик, боевой телекинез. Ожерелье — рост телекинеза амулета (радиус и сумка от камней, ×0.8…1.2).

## Лунное пламя (энергия «Жгучего копья»)
- `SpearEnergy_MoonflameEssentium` / `_MoonflameTisserium`: серебряный луч, ожог 12 / 15; цель «Moonstruck» на 6 / 9 с × мощь (зрение −30%, сознание −10%), сбивает прицел и забывает, кто ударил (отпускает носителя как цель). Поля `mindFogHediff`, `mindFogSeconds`, `forgetStriker` в SpearEnergyDef.

## Кристалл-батареи (ArtifactBattery.cs, Artifactorics_Battery.xml)
- Два предмета: `Avir_CrystalBatteryEmpty` (пустая, её делает стол) и `Avir_CrystalBattery` (заряженная, до 150). Зарядка: кто может отдать чувствительность — тот и берёт работу (без привязки к пешке); 150% = полная, меньше — частично (пешка со 100% отдаёт 90% → 60% батареи), другой дозарядит. Дев-кнопки: раздел «Avir artifactorics» в debug actions (батареи, заряд артефактов, очки амулета/артефактов, амулеты A'Shaign/Askhialis).
- `Avir_CrystalBattery`: 150 заряда (единицы те же, что у артефакта: 100 × качество оправы × чувствительность носителя), 0.25 кг, стак 5 (стакаются только полные с полными и пустые с пустыми). Делается пустой на столе артефактора (30 серебра + 15 пластали, Apex alloycraft).
- Использование: сама, когда самый пустой артефакт ниже порога автоперезарядки (раньше собственной перезарядки пешки), или кнопкой «Use crystal battery». Сначала заполняет самый пустой артефакт, потом остальные; остаток остаётся в батарее. Пустые пешка выкладывает.
- Зарядка: рецепт «charge crystal battery» (600 работы) с выбранной пешкой; она отдаёт 150% психочувствительности на день (как перезарядка, не ниже 10% своей). Вкладка «Items» стола показывает зарядчиков по убыванию чувствительности.
- Стол артефактора: 6 вкладок (Artifact, Inlay, Stones, Amulets, Items, Buildings — заглушка). Эссенций, тиссерий и облачная ткань перенесены на стол (вкладка Items, `ArtificerItemRecipe`).

## Интерфейс амулета (после проверки)
- Названия в текстах — A'Shaign (не A'Shai) и A'Schialis (не Askhialis).
- Узор развития: как спираль артефакторики — в единицах узора, зум колёсиком, перетаскивание, клик без сдвига выбирает; тяжёлая отрисовка только на Repaint (раньше каждый камень рисовался кругом из 72 линий — игра зависала). Подтверждение выбора — на пергаменте внутри окна.
- Сердечник на предмете — отдельный слой `AvirAmulet_Gem` (Cutout, цвет металла сердечника) поверх амулета.
- Слот (активка/пассивка) спит, пока в него не вложено 3 очка (`slotCost`): клик по сердцевине в центре узора → «Awaken slot». Без этого способность не выдаётся и пассивка не работает. Пешкам фракций слот открыт, если на основном пути ≥3 камней.
- Центр узора — сердце как в артефакторике (оправа цвета ожерелья, камень цвета сердечника, эмблема слота). Камни — кольца (заполнены золотом, когда открыты), майлстоуны — эмблема пути в круге. Подписи путей — только у пути под курсором.
- Развитие амулета — во вкладке пешки «Artifacts» (амулет — первая строка, кнопка «Open development»); гизмо развития убрано. Подпространственная сумка — своя вкладка «Satchel» вместо гизмо.

## Окна и сумка (после второй проверки)
- Листы (стол, спираль, развитие амулета) разворачиваются на весь экран кнопкой-рамкой слева от крестика (`SheetWindow`), пропорции сохраняются.
- Камни узора амулета крупнее и тёмные (закрытые — чёрные, доступные — с золотым кольцом, открытые — золотые). В окне развития при девмоде: +1 / +10 очков, +5k опыта.
- Сумка: проверка 2 раза/с до 3 вещей, радиус 3, хранилище ищется без пути (телекинез); запрещённые вещи сама не берёт. ПКМ по вещи — «Pick up into satchel» (в пределах телекинетического радиуса, снимает запрет). Во вкладке Satchel — «Keep» (не выкладывать). Эффективность 9 — 45 кг × сила.

## Третья проверка
- Узор шире (A'Shaign: внешнее кольцо 1500 ед., ядро 300; касты 900/210), камни 34/78 ед., ядро 330; зум 0.12–1.6.
- Связки — отдельный пояс за замыкающим кругом: каждая между двумя путями, «9» внутри, «18» снаружи, к ней тянутся линии от концов путей.
- Поле телекинеза амулета (`tkLevel`): круг как у артефакта на вкладке «Amulet itself» левой страницы (узор его больше не содержит), гнёзда показывают те же три камня; 5 гнёзд за очки 1/2/3/4/5, гнёзда дают +5/+6/+7/+8/+10 радиуса и столько же кг (× качество ожерелья) поверх роста от камней. Гнёзда текстуры стоят не через 72°: углы в `ArtifactoricsTex.TkSocketAngles` (и для артефактов).
- Обычные камни узора берут картинки `UI/Artifactorics/Amulet/Stone_Locked|_Available|_Opened`, если они есть (иначе диски).
- Связка — один узел на поясе за кругом: «общие камни / 9», потом «/ 18», потом «x2». Без чёрных обводок; кнопки окна — на мягком свечении. Вещь, взятая в сумку по приказу, сразу «Keep».
- Крестик и «на весь экран» — на тёмных печатях; иконку разворота можно положить в `UI/Artifactorics/Button_Maximize` / `Button_Restore` (иначе рисуется рамка).
- Инквизитор у A'Shaign называется Justiciar (`ashaignLabel`). Shadowguard — серебро (A'Shaign); именные A'Shaign — серебро, A'Schialis — золото.

## Огонь копья (глефы)
- Глефы `KWC_WarpPolearm*` получили дальнобойный verb `Verb_SpearFire`: доступен, только если в артефакте глефы камень «Жгучее копьё», режим включён и есть заряд. Стреляет как обычное оружие (навык стрельбы, дистанция, укрытия; точность глефы 0.75/0.75/0.65/0.5, дальность 24, прицеливание 1.1 с), снаряд и энергия — из камня, мощь выстрела ×0.5 от сфокусированного луча (способности), 0.75% заряда за выстрел (`spearShotCharge`, `spearShotPower` в Artifactorics_Settings). Без режима/заряда — ближний бой, как раньше.
- Переключатель «Spear fire» у колониста; по умолчанию включён, у драчунов — выключен (и драчун недоволен глефой только при включённом режиме). Артефакт глефы в режиме копья учится и от стрельбы.
- Способность «Жгучее копьё» осталась как сфокусированный одиночный луч.
- Гнёзда для артефактов и эффект Essence Reaver по-прежнему считают глефу оружием ближнего боя.

## Фракции (AvirFactionArtifactorics.cs)
- Вызывается из гардероба фракций (`Patch_AvirElvesGear`: Dress + Arm, барбарский запасной вариант, переодевание Hospitality) — после одежды и оружия. Старая таблица `AvirAmuletAssignment` (Telekinetic I–IV и т. п.) снята.
- Артефакторы и архитекторы носят только традиционные амулеты: их телекинез растёт с камнями (каста артефакторов ×2), старые Telekinetic I–IV пешкам не выдаются.
- Пешки Ордена А'Шайн (`Defs/Artifactorics/AvirFaction_Gear.xml`) и Легиона А'Схиалис (`Compat/VMemes/Defs/Artifactorics/AvirFaction_Gear_AShialis.xml`) рождаются с амулетом на шее (именной по `namedChance` или традиционный: Орден — серебро + эссенций/тиссерий, все пути; Легион — золото + эссенций, каста по пути) и всегда с артефактом: в одежде на торс → в другой одежде → в оружии → в инвентаре (голый Авир без оружия).
- Развитие 0..1 = (статус × 0.65 + возраст × 0.35) × скорость обучения (0.5–1.75), ±15%. Камни амулета = развитие × 50 (основной путь, затем соседи), ячейки артефакта = развитие × 18 по рукавам вида (группы исключения соблюдаются). Качество растёт с развитием.
- Новые караваны «artificers» (`Caravan_Avir_Artificers`, `Caravan_AShialis_Artificers`, commonality 0.5): металлы, оправы (без сердцевин), базовые амулеты своей фракции. Всё в подпространственном хранилище торговца — вьючных животных нет (`AvirSubspaceCaravan`).
- Оружие пешек фракций всегда несёт свой артефакт (рукава оружия по роли); глефы (KWC_WarpPolearm*) в 75% — с камнем «Жгучее копьё» (Орден — лунное пламя эссенция/тиссерия и поглощающее копьё; Легион — золотой свет и исцеляющее пламя), автоприменение в бою. Автоприменение камней теперь работает у пешек любых фракций.
- Амулеты с сердечником из эссенция теперь только для Авир (золото-эссенций Аскхиалис), как и серебряные.
- Амулет и артефакт остаются на трупе со всем развитием — трофей.

## Открыто
- Отдельная вкладка развития для именных амулетов (Flameguard и др.) — не начата, ждёт ответов (выбор навсегда или параллельно; остаются ли именные амулеты предметами).
- Все числа предварительные, в игре механики не проверены.
- Telekinetic I–IV ещё в Named amulets (рецепты можно скрыть, ассеты сохранены).
- Push в GitHub недоступен (403) — актуальное состояние в архиве `AvirApexPredator.zip`.

## Concept (not coded): artifact buildings, Oct 4 2026
- One host building class with 1-3 artifact-module sockets, inlaid AFTER construction (pawn job), with a small parchment inlay menu (stones as in the telekinesis circle). No module development yet. Buildings drain charge much slower.
- Apogee (Avir): Subspace Controller (pulls items in radius to storage; radius grows with stones); Subspace Storage (1 cell, 250 kg -> 1000 kg with 3 stones, no rot, not counted in raid wealth); Observers (statues, 2 eyes: fewer social fights, slave suppression; both eyes reveal invisible pawns, e.g. sightstealers); Essentium projector (3 gold-rim or 3 silver-rim stones, essentium hearts, Excellent+; gold = day charging, golden burning beam; silver = moonflame, third stone special; rotates, 2 operators, arcs like a mortar); Temperature attuner (aura with any chosen temperature, works outdoors/fields; stones 2-3 widen radius; 3rd adds auto sunlight aura).
- Basic artifactorics (everyone): one-stone totems such as an explosive totem and an animal-scare totem.
- Charge upkeep: "Attuner" work driven by psychic sensitivity; later a whole Ritualistics system.
- Answers (Oct 4): Storage gives items out via controllers; wealth counted at half value. Controller = quartermaster of storages. Observer: slowly raises slave suppression in aura and +25% terror. Projector: silver charges at night; stone layout = arch of 2 essentium sides + tisserium centre (silver) ; fires in an arc but needs sight (like the tesseron); 2nd operator = attuner. Temperature attuner: outdoors already handled by the user's patch - indoor only for now. Explosive totem fires an explosion at range, does not blow itself up. Attuner work stays invisible until the player has artifact buildings. Ritualistics is its own system, NOT Ideology rituals.
- New item: Subspace Capacitor - utility, +100 kg subspace satchel (adds/extends), weighs 7 kg, no telekinesis, anyone can wear, not drawn on the pawn, no inlay; items go into it first, then the backpack. Long spear-sized back/side case. Recipe: essentium + any metal except violite/essentium/tisserium.
- Oct 6: SatchelInventory.cs - the satchel's free kg add to MassUtility.Capacity (inventory extension) and to Pawn_CarryTracker.MaxStackSpaceEver (hand loads, up to a stack); no rot in a bearer's inventory; food/drug policy/medicine stock served from the satchel into the inventory. FreeKg subtracts the inventory overflow. A'Shaign amulets learn from every other skill: ashaignOffPathXpShare 0.25 of the artifacts' cut.
- Oct 8: AmuletTelekineticWork.cs - helping hands (A'Shaign + castes with telekineticHelpers: Steward, Maker): while the wearer cleans / works plants / mines, telekinesis works extra targets (AmuletSettingsDef tkHelper* lists by tk level). No reservations; soft hand-off (filth thickness, rock HP, plant progress via WorkDonePerTick postfix). Mod settings (AvirMod.cs): artifact/amulet xp factors, A'Shaign off-path share (default 0.5), point price curve, off-branch cost factor (default 1). Amulets: useHitPoints false.
- Oct 8: artifact buildings frame (ArtifactBuildings.cs: CompArtifactHost, Dialog_ArtifactHost, attuner WorkType hidden via PawnColumnWorker_WorkPriority.VisibleCurrently, inlay/extract/recharge jobs, drop on destroy) + pilot totems (ArtifactTotems.cs: blast, scare). Defs: Artifactorics_Buildings.xml. Buildings tab = reference. Placeholder textures in Things/Building/Artifactorics.
- Oct 8 (2): ArtifactBuildingEffects.cs + Artifactorics_Buildings2.xml - hearth stone, growth totem, calm bell, tk crane, drag warden, mending stone, healing stone, work pylon (StatPart on WorkTableWorkSpeedFactor), observer, temperature attuner. Placeholder textures. Remaining: subspace storage/controller, essentium projector.
- Oct 8 (3): building quality (CompQuality + CompArt from Excellent; buildingQualityFactors), crane radius up, renames Architect's/Healer's Charm, no stacking, research tiers Basic/Standard/Avir artifactorics, locked recipes hidden at the table.
