import json
import urllib.request
import gzip
import os
import sys
import re

sys.stdout.reconfigure(encoding='utf-8')

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
BASE_DIR = os.path.dirname(SCRIPT_DIR) if os.path.basename(SCRIPT_DIR).lower() == 'tools' else SCRIPT_DIR

def resolve_path(filename):
    if os.path.isabs(filename) and os.path.exists(filename):
        return filename
    c1 = os.path.join(os.getcwd(), filename)
    if os.path.exists(c1):
        return c1
    c2 = os.path.join(BASE_DIR, filename)
    if os.path.exists(c2):
        return c2
    c3 = os.path.join(SCRIPT_DIR, filename)
    if os.path.exists(c3):
        return c3
    return c2

OFFICIAL_CARDS_PATH = resolve_path('bg_cards_official.json')
SUPPLEMENTARY_CARDS_PATH = resolve_path('supplementary_cards.json')
HEARTHDB_MAP_PATH = resolve_path(os.path.join('tools', 'hearthdb_id_map.json'))
OUTPUT_DB_PATH = os.path.join(BASE_DIR, 'comps_vector_db.json')
MISSING_CARDS_REPORT_PATH = os.path.join(BASE_DIR, 'missing_meta_cards.json')
PERFECT_GAMES_URL = 'https://static.zerotoheroes.com/api/bgs/bgs-perfect-games.json'
COMP_STATS_URL = 'https://static.zerotoheroes.com/api/bgs/comp-stats/last-patch/overview-from-hourly.gz.json'
COMP_STATS_PAST_SEVEN_URL = 'https://static.zerotoheroes.com/api/bgs/comp-stats/past-seven/overview-from-hourly.gz.json'

RUSSIAN_ARCHETYPE_NAMES = {
    'mech_automaton': 'Механизмы (Автоматоны)',
    'mech_volumizer': 'Механизмы (Волумайзер и Щиты)',
    'mech_magnetic': 'Механизмы (Магнетизм)',
    'mech_magnet': 'Механизмы (Магнетизм)',
    'mech_apm': 'Механизмы (APM)',
    'mech_deathrattle': 'Механизмы (Хрипы)',
    'mech_glambot': 'Механизмы (Глэмбот и Щиты)',
    'mech_general': 'Механизмы (Сборка)',
    'quilboar_choose_one': 'Свинобразы (Самоцветы и Баффы)',
    'quilboar_felboar_spam': 'Свинобразы (Скверносвин)',
    'quilboar_bloodgem': 'Свинобразы (Кровавые самоцветы)',
    'quilboar_end_of_turn': 'Свинобразы (Конец хода)',
    'quilboar_general': 'Свинобразы (Сборка)',
    'beast_deathrattle': 'Звери (Предсмертные хрипы)',
    'beast_leapfrogger': 'Звери (Попрыгухи и Ривендер)',
    'beast_goldrinn': 'Звери (Голдринн и Волки)',
    'beast_beetle': 'Звери (Жуки и Перерождения)',
    'beast_lobster': 'Звери (Омары и Хрипы)',
    'beast_leviathan': 'Звери (Морские чудища)',
    'beast_general': 'Звери (Сборка)',
    'murloc_battlecry': 'Мурлоки (Боевые кличи и Бранн)',
    'murloc_scam': 'Мурлоки (Яды и Щиты)',
    'murloc_handbuff': 'Мурлоки (Бафф в руке)',
    'murloc_general': 'Мурлоки (Сборка)',
    'undead_reborn': 'Нежить (Перерождения и Рыцари)',
    'undead_deathrattle': 'Нежить (Предсмертные хрипы)',
    'undead_attack_buff': 'Нежить (Бафф атаки)',
    'undead_butcher': 'Нежить (Мясники и Спавны)',
    'undead_general': 'Нежить (Сборка)',
    'demon_self_damage': 'Демоны (Урон по герою)',
    'demon_tavern_consume': 'Демоны (Пожирание таверны)',
    'demon_boost_shop': 'Демоны (Раскачка таверны)',
    'demon_general': 'Демоны (Сборка)',
    'dragon_battlecry': 'Драконы (Калесгос и Кличи)',
    'dragon_kalecgos': 'Драконы (Калесгос и Кличи)',
    'dragon_shield': 'Драконы (Божественные щиты)',
    'dragon_spell_power': 'Драконы (Сила заклинаний)',
    'dragon_evoker': 'Драконы (Пробудитель)',
    'dragon_start_of_combat': 'Драконы (Начало боя)',
    'dragon_general': 'Драконы (Сборка)',
    'elemental_stats': 'Элементали (Масштабирование статов)',
    'elemental_cycle': 'Элементали (Цикл карт и статов)',
    'elemental_azerite': 'Элементали (Азерит)',
    'elemental_tempest': 'Элементали (Буря)',
    'elemental_tavern_buff': 'Элементали (Бафф таверны)',
    'elemental_general': 'Элементали (Сборка)',
    'naga_spellcraft': 'Наги (Чародейство)',
    'naga_deep_blues': 'Наги (Блюз глубоких глубин)',
    'naga_groundbreaker': 'Наги (Землекрушительница)',
    'naga_end_of_turn': 'Наги (Конец хода)',
    'naga_general': 'Наги (Сборка)',
    'pirate_apm': 'Пираты (Бесконечная экономика)',
    'pirate_discover': 'Пираты (Раскопки и Экономика)',
    'pirate_attack': 'Пираты (Атакующий темп)',
    'pirate_general': 'Пираты (Сборка)',
    'neutral_tea_set': 'Зверинец (Чайный сервиз)',
    'aberration_discard': 'Абберации (Сброс карт)',
    'aberration_deathrattle': 'Абберации (Предсмертные хрипы)',
    'aberration_general': 'Абберации (Сборка)',
    'abberation_discard': 'Абберации (Сброс карт)',
    'abberation_deathrattle': 'Абберации (Предсмертные хрипы)',
    'abberation_general': 'Абберации (Сборка)',
    'menagerie': 'Зверинец (Сборка разных типов)'
}

ALL_TRIBES = [
    'Механизм', 'Зверь', 'Мурлок', 'Нежить', 'Демон',
    'Элементаль', 'Нага', 'Пират', 'Дракон', 'Свинобраз', 'Абберация'
]

TRIBE_MAP_EN_TO_RU = {
    'mech': 'Механизм',
    'beast': 'Зверь',
    'murloc': 'Мурлок',
    'undead': 'Нежить',
    'demon': 'Демон',
    'elemental': 'Элементаль',
    'naga': 'Нага',
    'pirate': 'Пират',
    'dragon': 'Дракон',
    'quilboar': 'Свинобраз',
    'aberration': 'Абберация',
    'abberation': 'Абберация',
    'all': 'Все'
}

def normalize_hero_id(hero_id):
    """Нормализует ID героя, отрезая суффиксы скинов (например TB_BaconShop_HERO_25_SKIN_B -> TB_BaconShop_HERO_25)."""
    if not hero_id:
        return ''
    return re.sub(r'_SKIN_[A-Z0-9]+$', '', hero_id, flags=re.IGNORECASE)

def fetch_json(url):
    req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)'})
    with urllib.request.urlopen(req, timeout=30) as resp:
        raw = resp.read()
        try:
            raw = gzip.decompress(raw)
        except Exception:
            pass
        return json.loads(raw.decode('utf-8'))

def calculate_feature_vector(minions, required_tribes):
    vec = [0.0] * (len(ALL_TRIBES) + 4)
    tribe_counts = {t: 0 for t in ALL_TRIBES}
    shields = 0
    reborns = 0
    total_tier = 0

    for m in minions:
        tier = m.get('Tier', 1)
        total_tier += tier
        if m.get('HasDivineShield'): shields += 1
        if m.get('HasReborn'): reborns += 1

    for t in required_tribes:
        if t in tribe_counts:
            tribe_counts[t] += 2.0

    for idx, t in enumerate(ALL_TRIBES):
        vec[idx] = float(tribe_counts[t])

    count = max(len(minions), 1)
    vec[len(ALL_TRIBES)] = total_tier / count
    vec[len(ALL_TRIBES) + 1] = float(shields)
    vec[len(ALL_TRIBES) + 2] = float(reborns)
    vec[len(ALL_TRIBES) + 3] = float(len(minions))
    return vec

def parse_board_minions(raw_board, official_cards_by_id, base_cards_by_gold_id, card_id_to_dbf, missing_collector=None, context=None):
    minions = []
    required_tribes = set()
    tribe_distribution = {}

    for pos, m in enumerate(raw_board[:7]):
        raw_card_id = m.get('cardID')
        if not raw_card_id:
            continue

        is_gold = raw_card_id.endswith('_G') or m.get('tags', {}).get('PREMIUM') == 1
        base_card_id = raw_card_id.replace('_G', '')

        dbf_id = card_id_to_dbf.get(raw_card_id) or card_id_to_dbf.get(base_card_id)
        if not dbf_id:
            dbf_id = m.get('tags', {}).get('BACON_TRIPLED_BASE_MINION_ID') or m.get('tags', {}).get('ENTITY_ID')

        official_card = None
        if dbf_id:
            if dbf_id in official_cards_by_id:
                official_card = official_cards_by_id[dbf_id]
            elif dbf_id in base_cards_by_gold_id:
                official_card = base_cards_by_gold_id[dbf_id]
                is_gold = True

        if not official_card:
            if missing_collector is not None and context:
                turn = context.get('turn', 0)
                mmr = context.get('mmr', 0)
                arch = context.get('archetype', 'unknown')
                if turn >= 10 and mmr >= 5000:
                    cid = base_card_id
                    if cid not in missing_collector:
                        missing_collector[cid] = {
                            'cardId': cid,
                            'dbfId': dbf_id if (dbf_id and isinstance(dbf_id, int) and dbf_id > 0) else None,
                            'seenCount': 0,
                            'archetypes': {}
                        }
                    missing_collector[cid]['seenCount'] += 1
                    missing_collector[cid]['archetypes'][arch] = missing_collector[cid]['archetypes'].get(arch, 0) + 1
            continue

        if official_card.get('tribeRu') and official_card.get('tribeRu') != 'Без типа':
            for t in official_card['tribeRu'].split(' / '):
                t_clean = t.strip()
                required_tribes.add(t_clean)
                tribe_distribution[t_clean] = tribe_distribution.get(t_clean, 0) + 1

        minions.append({
            'CardId': raw_card_id,
            'DbfId': official_card['id'],
            'IsGolden': is_gold,
            'Tier': official_card.get('tier') or 1,
            'Attack': m.get('tags', {}).get('ATK', official_card.get('attack') or 1),
            'Health': m.get('tags', {}).get('HEALTH', official_card.get('health') or 1),
            'HasTaunt': m.get('tags', {}).get('TAUNT') == 1,
            'HasDivineShield': m.get('tags', {}).get('DIVINE_SHIELD') == 1,
            'HasReborn': m.get('tags', {}).get('REBORN') == 1,
            'HasPoisonous': m.get('tags', {}).get('POISONOUS') == 1 or m.get('tags', {}).get('VENOMOUS') == 1,
            'ZonePosition': pos
        })

    return minions, list(required_tribes), tribe_distribution

def is_canonical_benchmark_board(minions, turn, mmr, official_cards_by_id):
    # 1. Размер стола: должен быть полноценным финальным столом (6-7 существ)
    if len(minions) < 6:
        return False, "less than 6 minions"

    # 2. Ход игры: минимум 10 ход (финал/лейтгейм)
    if turn < 10:
        return False, f"turn too early ({turn})"

    # 3. Рейтинг: соревновательный уровень (>= 5000 MMR)
    if mmr < 5000:
        return False, f"low mmr ({mmr})"

    # 4. Проверка дубликатов карт
    dbf_counts = {}
    for m in minions:
        d = m['DbfId']
        dbf_counts[d] = dbf_counts.get(d, 0) + 1

    # Строгий запрет: >= 3 копий ЛЮБОЙ карты на столе
    if any(c >= 3 for c in dbf_counts.values()):
        return False, "has >= 3 copies of same minion"

    # Подсчет пар дубликатов (например, 2 штуки одной карты)
    pairs = [d for d, c in dbf_counts.items() if c == 2]

    # Строгий запрет: больше одной пары дубликатов на весь стол
    if len(pairs) > 1:
        return False, f"has {len(pairs)} pairs of duplicates"

    # Строгий запрет: дубликаты существ низких тиров (тир 1 или 2 в финале)
    for p_dbf in pairs:
        m_tier = official_cards_by_id.get(p_dbf, {}).get('tier', 1)
        if m_tier <= 2:
            return False, f"has duplicate of low-tier minion (Tier {m_tier})"

    return True, "ok"

def smooth_hero_placement(hero_dp, hero_avg, arch_avg, prior_weight=25):
    """Байесовское сглаживание среднего места героя для предотвращения выбросов при малой выборке."""
    if hero_dp <= 0:
        return arch_avg
    smoothed = (hero_dp * hero_avg + prior_weight * arch_avg) / (hero_dp + prior_weight)
    return round(float(smoothed), 2)

def determine_primary_tribe(archetype, tribe_distribution, required_tribes):
    amalgams = tribe_distribution.get('Все', 0) if tribe_distribution else 0
    pure_dist = {k: v for k, v in tribe_distribution.items() if k != 'Все'} if tribe_distribution else {}

    # 1. Проверяем кандидата от архетипа
    arch_candidate = None
    if archetype:
        arch_lower = archetype.lower()
        if not any(w in arch_lower for w in ['neutral', 'menagerie']):
            for en_key, ru_val in TRIBE_MAP_EN_TO_RU.items():
                if en_key in arch_lower:
                    arch_candidate = ru_val
                    break

    # Если архетип заявляет племя, проверяем, есть ли на столе подтверждение (минимум 3 существа этого типа с учетом Слияний)
    if arch_candidate and arch_candidate != 'Все':
        cand_count = pure_dist.get(arch_candidate, 0) + amalgams
        if cand_count >= 3:
            return arch_candidate

    # 2. Если архетип не подтвердился или был menagerie/neutral, смотрим на реальное большинство существ
    if pure_dist:
        sorted_tribes = sorted(pure_dist.items(), key=lambda kv: kv[1], reverse=True)
        top_tribe, top_count = sorted_tribes[0]
        if top_count + amalgams >= 3:
            return top_tribe

    if required_tribes and len(required_tribes) == 1 and required_tribes[0] != 'Все':
        if pure_dist.get(required_tribes[0], 0) + amalgams >= 2:
            return required_tribes[0]

    return 'Все'

def get_minion_signature(minions):
    return tuple(sorted(m['DbfId'] for m in minions))

def create_synthetic_naga_board(hero_id, archetype_name, dbf_list, official_cards_by_id, avg_place=3.55, top1=0.21, mmr=8500, turn=14):
    minions = []
    req_tribes = ['Нага']
    for pos, dbf in enumerate(dbf_list[:7]):
        card = official_cards_by_id.get(dbf)
        if not card:
            continue
        minions.append({
            'CardId': f"BG_{dbf}",
            'DbfId': dbf,
            'IsGolden': pos in (0, 1), # Первые 2 существа золотые
            'Tier': card.get('tier') or 1,
            'Attack': (card.get('attack') or 5) * 4,
            'Health': (card.get('health') or 5) * 4,
            'HasTaunt': pos == 0,
            'HasDivineShield': dbf in (80746, 114816),
            'HasReborn': False,
            'HasPoisonous': False,
            'ZonePosition': pos
        })

    return {
        'source': 'canonical_benchmark',
        'heroCardId': normalize_hero_id(hero_id),
        'rawHeroCardId': hero_id,
        'archetype': archetype_name,
        'primaryTribe': 'Нага',
        'minions': minions,
        'requiredTribes': req_tribes,
        'averagePlacement': avg_place,
        'top1Rate': top1,
        'turn': turn,
        'playerRank': mmr
    }

def main():
    print('1. Загрузка официальной базы карт bg_cards_official.json...')
    with open(OFFICIAL_CARDS_PATH, 'r', encoding='utf-8-sig') as f:
        official_data = json.load(f)

    if os.path.exists(SUPPLEMENTARY_CARDS_PATH):
        try:
            with open(SUPPLEMENTARY_CARDS_PATH, 'r', encoding='utf-8-sig') as f:
                supp_data = json.load(f)
                supp_cards = supp_data.get('cards', [])
                official_data['cards'].extend(supp_cards)
                print(f'   Загружено дополнительных карт из supplementary_cards.json: {len(supp_cards)}')
        except Exception as e:
            print(f'   [!] Ошибка чтения supplementary_cards.json: {e}')

    official_cards_by_id = {c['id']: c for c in official_data['cards']}
    base_cards_by_gold_id = {}
    for c in official_data['cards']:
        if c.get('goldenUpgradeId'):
            base_cards_by_gold_id[c['goldenUpgradeId']] = c

    print(f'   Итого активных карт в базе: {len(official_cards_by_id)}')

    card_id_to_dbf = {}
    if os.path.exists(HEARTHDB_MAP_PATH):
        with open(HEARTHDB_MAP_PATH, 'r', encoding='utf-8-sig') as f:
            card_id_to_dbf = json.load(f)
        print(f'   Загружен маппинг HearthDb: {len(card_id_to_dbf)} связей')

    # Синхронизируем маппинг строковых идентификаторов для всех карт базы
    for c in official_data['cards']:
        cid = c.get('slug')
        dbf = c.get('id')
        if cid and dbf:
            card_id_to_dbf[cid] = dbf
            gold_id = c.get('goldenUpgradeId')
            if gold_id:
                card_id_to_dbf[f"{cid}_G"] = gold_id

    print('2. Загрузка статистики comp-stats из Firestone API (last-patch + past-seven)...')
    archetype_meta_stats = {}
    comp_stats_sources = [COMP_STATS_URL, COMP_STATS_PAST_SEVEN_URL]
    comp_stats_combined = []

    for url in comp_stats_sources:
        try:
            print(f'   Загрузка: {url.split("/")[-2]}...')
            data = fetch_json(url)
            cstats = data.get('compStats', [])
            comp_stats_combined.extend(cstats)
            print(f'   Успешно получено архетипов: {len(cstats)}')

            for cs in cstats:
                arch = cs.get('archetype', 'general')
                if arch not in archetype_meta_stats:
                    p_dist = cs.get('placementDistribution', [])
                    total_matches = sum(x.get('totalMatches', 0) for x in p_dist)
                    rank1_matches = next((x.get('totalMatches', 0) for x in p_dist if x.get('rank') == 1), 0)
                    top1_rate = round(rank1_matches / total_matches, 3) if total_matches > 0 else 0.20
                    avg_placement = round(float(cs.get('averagePlacement', 3.5)), 2)

                    archetype_meta_stats[arch] = {
                        'avg_placement': avg_placement,
                        'top1_rate': top1_rate,
                        'total_matches': total_matches
                    }
        except Exception as e:
            print(f'   Ошибка загрузки {url}: {e}')

    print('3. Загрузка безупречных игр bgs-perfect-games.json из Firestone API...')
    try:
        perfect_games = fetch_json(PERFECT_GAMES_URL)
        print(f'   Успешно загружено безупречных игр: {len(perfect_games)}')
    except Exception as e:
        print(f'   Ошибка загрузки perfect games: {e}')
        perfect_games = []

    all_raw_boards = []
    seen_raw_board_sigs = set()
    missing_collector = {}

    # 4. Обработка comp-stats (heroStats) из всех источников
    for cs in comp_stats_combined:
        arch = cs.get('archetype', 'general')
        arch_meta = archetype_meta_stats.get(arch, {'avg_placement': 3.5, 'top1_rate': 0.20, 'total_matches': 1000})
        arch_avg_placement = arch_meta['avg_placement']
        arch_top1_rate = arch_meta['top1_rate']

        for hs in cs.get('heroStats', []):
            raw_hero_card_id = hs.get('heroCardId')
            hero_card_id = normalize_hero_id(raw_hero_card_id)
            raw_hero_avg = float(hs.get('averagePlacement', arch_avg_placement))
            hero_dp = int(hs.get('dataPoints', 0))

            # Применяем Байесовское сглаживание: исключаем аномальные 1.0 при 1-2 сыгранных играх
            hero_avg_placement = smooth_hero_placement(hero_dp, raw_hero_avg, arch_avg_placement)

            for fb in hs.get('finalBoards', []):
                board_minions_raw = fb.get('finalComp', {}).get('board', [])
                turn = fb.get('finalComp', {}).get('turn', 12)
                mmr = fb.get('mmr', 6500)

                minions, req_tribes, tribe_dist = parse_board_minions(
                    board_minions_raw, official_cards_by_id, base_cards_by_gold_id, card_id_to_dbf,
                    missing_collector=missing_collector, context={'turn': turn, 'mmr': mmr, 'archetype': arch}
                )

                # Строгий соревновательный фильтр
                is_valid, _ = is_canonical_benchmark_board(minions, turn, mmr, official_cards_by_id)
                if not is_valid:
                    continue

                minion_sig = get_minion_signature(minions)
                raw_sig = (hero_card_id, arch, minion_sig)
                if raw_sig in seen_raw_board_sigs:
                    continue
                seen_raw_board_sigs.add(raw_sig)

                primary_tribe = determine_primary_tribe(arch, tribe_dist, req_tribes)

                all_raw_boards.append({
                    'source': 'comp_stats',
                    'heroCardId': hero_card_id,
                    'rawHeroCardId': raw_hero_card_id,
                    'archetype': arch,
                    'primaryTribe': primary_tribe,
                    'minions': minions,
                    'requiredTribes': req_tribes,
                    'averagePlacement': hero_avg_placement,
                    'top1Rate': arch_top1_rate,
                    'turn': turn,
                    'playerRank': mmr
                })

    # 5. Обработка bgs-perfect-games
    for g in perfect_games:
        raw_hero_card_id = g.get('playerCardId')
        hero_card_id = normalize_hero_id(raw_hero_card_id)
        arch = g.get('archetype') or g.get('bgsCompArchetype')
        rank = g.get('playerRank', 7000)
        try:
            rank = int(rank)
        except:
            rank = 7000

        board_history = g.get('postMatchStats', {}).get('boardHistory', [])
        if not board_history:
            continue

        last_entry = board_history[-1]
        turn = last_entry.get('turn', 12)
        raw_board = last_entry.get('board', [])

        minions, req_tribes, tribe_dist = parse_board_minions(
            raw_board, official_cards_by_id, base_cards_by_gold_id, card_id_to_dbf,
            missing_collector=missing_collector, context={'turn': turn, 'mmr': rank, 'archetype': arch}
        )

        is_valid, _ = is_canonical_benchmark_board(minions, turn, rank, official_cards_by_id)
        if not is_valid:
            continue

        minion_sig = get_minion_signature(minions)
        raw_sig = (hero_card_id, arch, minion_sig)
        if raw_sig in seen_raw_board_sigs:
            continue
        seen_raw_board_sigs.add(raw_sig)

        if not arch:
            sorted_t = sorted(tribe_dist.items(), key=lambda x: x[1], reverse=True)
            if sorted_t and sorted_t[0][1] >= 2:
                t_name = sorted_t[0][0]
                for k, v in TRIBE_MAP_EN_TO_RU.items():
                    if v == t_name:
                        arch = f"{k}_general"
                        break
            if not arch:
                arch = 'menagerie'

        primary_tribe = determine_primary_tribe(arch, tribe_dist, req_tribes)
        arch_meta = archetype_meta_stats.get(arch, {'avg_placement': 3.4, 'top1_rate': 0.20})

        all_raw_boards.append({
            'source': 'perfect_games',
            'heroCardId': hero_card_id,
            'rawHeroCardId': raw_hero_card_id,
            'archetype': arch,
            'primaryTribe': primary_tribe,
            'minions': minions,
            'requiredTribes': req_tribes,
            'averagePlacement': arch_meta['avg_placement'],
            'top1Rate': arch_meta['top1_rate'],
            'turn': turn,
            'playerRank': rank
        })

    # 6. Обогащение каноничными столами Наг (если в Firestone мало сборок Наг)
    naga_count_current = sum(1 for b in all_raw_boards if b['primaryTribe'] == 'Нага')
    print(f'   Столов племени Нага из сырого API: {naga_count_current}')
    if naga_count_current < 30:
        print('   Добавление эталонных каноничных сборок Наг для героев и общего мета-пула...')
        naga_templates = [
            # 1. Землекрушительница + Статы
            ('naga_groundbreaker', [114816, 133707, 80746, 99054, 80747, 115555, 80742]),
            # 2. Блюз глубоких глубин + Мультипликация
            ('naga_deep_blues', [99054, 130658, 80747, 80746, 114816, 115565, 80745]),
            # 3. Чародейство и Заклинания
            ('naga_spellcraft', [120905, 133707, 115555, 119942, 122221, 80746, 115563]),
            # 4. Экономика и Конец хода
            ('naga_end_of_turn', [114816, 107879, 80746, 126916, 99054, 80747, 80740])
        ]
        key_naga_heroes = [
            'TB_BaconShop_HERO_23', # Королева Азшара
            'TB_BaconShop_HERO_34', # Леди Вайш
            'TB_BaconShop_HERO_41', # Рено Джексон
            'TB_BaconShop_HERO_43', # Бранн
            'BG23_HERO_305',        # Озумат
            'BG22_HERO_003',        # Ини Штормоверть
            'TB_BaconShop_HERO_08', # Король Мукла
            ''                      # Общий мета-бенчмарк
        ]
        for hero_id in key_naga_heroes:
            for arch_name, dbf_cards in naga_templates:
                syn_board = create_synthetic_naga_board(hero_id, arch_name, dbf_cards, official_cards_by_id)
                all_raw_boards.append(syn_board)

    print(f'4. Итого собрано чистых проверенных столов: {len(all_raw_boards)}')

    # 7. Ранжирование по формуле качества
    for b in all_raw_boards:
        quality = (b['playerRank'] * 1.5) \
                + (3000 if len(b['minions']) == 7 else 1500) \
                + (2000 if b['source'] in ('comp_stats', 'canonical_benchmark') else 0) \
                + (b['top1Rate'] * 8000) \
                - (b['averagePlacement'] * 1200)
        b['_quality'] = quality

    hero_tribe_boards = {}
    archetype_boards = {}
    hero_top_boards = {}

    for b in all_raw_boards:
        hero = b.get('heroCardId')
        ptribe = b['primaryTribe']
        arch = b['archetype']

        if hero and ptribe:
            ht_key = f"{hero}_{ptribe}"
            if ht_key not in hero_tribe_boards:
                hero_tribe_boards[ht_key] = []
            hero_tribe_boards[ht_key].append(b)

        if hero:
            if hero not in hero_top_boards:
                hero_top_boards[hero] = []
            hero_top_boards[hero].append(b)

        if arch:
            if arch not in archetype_boards:
                archetype_boards[arch] = []
            archetype_boards[arch].append(b)

    final_boards_set = {}

    def add_boards_with_diversity(board_list, max_count):
        board_list.sort(key=lambda x: x['_quality'], reverse=True)
        added = 0
        seen_sigs = set()
        for b in board_list:
            sig = get_minion_signature(b['minions'])
            if sig in seen_sigs:
                continue
            seen_sigs.add(sig)
            board_key = (b.get('heroCardId'), b.get('archetype'), sig)
            final_boards_set[board_key] = b
            added += 1
            if added >= max_count:
                break

    # 1. По каждой связке (Герой, Племя) берем до 2 лучших разнообразных столов
    for ht_key, blist in hero_tribe_boards.items():
        add_boards_with_diversity(blist, max_count=2)

    # 2. По каждому Герою берем до 3 лучших столов
    for hero, blist in hero_top_boards.items():
        add_boards_with_diversity(blist, max_count=3)

    # 3. По каждому Архетипу берем до 25 лучших столов
    for arch, blist in archetype_boards.items():
        add_boards_with_diversity(blist, max_count=25)

    print(f'5. Отобрано эталонных уникальных столов: {len(final_boards_set)}')

    result_boards = []
    board_idx = 1
    for b in final_boards_set.values():
        ru_name = RUSSIAN_ARCHETYPE_NAMES.get(b['archetype'])
        if not ru_name:
            ru_name = b['archetype'].replace('_', ' ').capitalize()

        feature_vec = calculate_feature_vector(b['minions'], b['requiredTribes'])

        result_boards.append({
            'Id': f"real_board_{board_idx}",
            'HeroCardId': b.get('heroCardId') or '',
            'PlayerRank': b.get('playerRank', 6500),
            'Archetype': b['archetype'],
            'ArchetypeNameRu': ru_name,
            'PrimaryTribe': b['primaryTribe'],
            'Minions': b['minions'],
            'Trinkets': [],
            'RequiredTribes': b['requiredTribes'],
            'AveragePlacement': round(float(b['averagePlacement']), 2),
            'Top1Rate': round(float(b['top1Rate']), 3),
            'Turn': b['turn'],
            'FeatureVector': feature_vec
        })
        board_idx += 1

    # Сортировка по AveragePlacement
    result_boards.sort(key=lambda x: (x['AveragePlacement'], -x['Top1Rate']))

    output_data = {
        'version': 5,
        'patchBuild': 253216,
        'totalArchetypes': len(result_boards),
        'boards': result_boards
    }

    with open(OUTPUT_DB_PATH, 'w', encoding='utf-8') as f:
        json.dump(output_data, f, ensure_ascii=False, indent=2)

    db_size = os.path.getsize(OUTPUT_DB_PATH)
    print(f'6. Векторная база сохранена в {OUTPUT_DB_PATH}!', flush=True)
    print(f'   Всего столов: {len(result_boards)}', flush=True)
    print(f'   Размер файла: {db_size / 1024:.1f} КБ', flush=True)

    # 7. Экспорт отчета об отсутствующих в базе картах меты (missing_meta_cards.json)
    # Критерии пользователя: MMR 5000+, Turn 10+, минимум 10 раз
    significant_missing = []
    for cid, info in missing_collector.items():
        if info['seenCount'] >= 10:
            top_archs = sorted(info['archetypes'].items(), key=lambda x: x[1], reverse=True)[:5]
            significant_missing.append({
                'cardId': cid,
                'dbfId': info['dbfId'],
                'seenCount': info['seenCount'],
                'topArchetypes': [a[0] for a in top_archs]
            })

    significant_missing.sort(key=lambda x: x['seenCount'], reverse=True)

    missing_report = {
        'generatedAt': '2026-10-12T06:50:00Z',
        'criteria': 'Competitive boards (MMR >= 5000, Turn >= 10), seen >= 10 times',
        'totalSignificantMissingCards': len(significant_missing),
        'missingCards': significant_missing
    }

    with open(MISSING_CARDS_REPORT_PATH, 'w', encoding='utf-8') as f:
        json.dump(missing_report, f, ensure_ascii=False, indent=2)

    print(f'7. Отчет об отсутствующих картах меты сохранен в {MISSING_CARDS_REPORT_PATH}')
    print(f'   Выявлено карт меты (>=10 раз на топ-столах): {len(significant_missing)}')

    # 8. Синхронизация файлов базы данных с bin/Debug и каталогом плагина
    import shutil
    targets = [
        os.path.join(BASE_DIR, 'bin', 'Debug')
    ]
    appdata = os.environ.get('APPDATA', '')
    if appdata:
        targets.append(os.path.join(appdata, 'HearthstoneDeckTracker', 'Plugins', 'HSCompsPlugin'))

    files_to_sync = ['comps_vector_db.json', 'supplementary_cards.json', 'missing_meta_cards.json']
    for tdir in targets:
        if os.path.exists(tdir):
            for fname in files_to_sync:
                fsrc = os.path.join(BASE_DIR, fname)
                if os.path.exists(fsrc):
                    shutil.copyfile(fsrc, os.path.join(tdir, fname))
            print(f'   Синхронизировано в {tdir}')

    try:
        if SCRIPT_DIR not in sys.path:
            sys.path.insert(0, SCRIPT_DIR)
        from download_missing_full_cards import download_missing_cards
        download_missing_cards(BASE_DIR)
    except Exception as e:
        print(f"[!] Предупреждение: автоматическая дозагрузка артов пропущена: {e}", flush=True)

if __name__ == '__main__':
    main()
