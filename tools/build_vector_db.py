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
    'Элементаль', 'Пират', 'Дракон', 'Свинобраз', 'Абберация'
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


ARCHETYPE_TRINKETS = {
    "mech_volumizer": [
        "BG35_MagicItem_741",
        "BG35_MagicItem_743",
        "BG36_MagicItem_840",
        "BG32_MagicItem_278"
    ],
    "mech_glambot": [
        "BG36_MagicItem_810",
        "BG36_MagicItem_812",
        "BG32_MagicItem_283",
        "BG35_MagicItem_743"
    ],
    "mech_apm": [
        "BG35_MagicItem_742",
        "BG32_MagicItem_278",
        "BG35_MagicItem_741",
        "BG36_MagicItem_840"
    ],
    "murloc_handbuff": [
        "BG32_MagicItem_301",
        "BG36_MagicItem_820",
        "BG35_MagicItem_305",
        "BG30_MagicItem_914"
    ],
    "murloc_scam": [
        "BG32_MagicItem_301",
        "BG32_MagicItem_926",
        "BG35_MagicItem_753",
        "BG36_MagicItem_850"
    ],
    "murloc_battlecry": [
        "BG30_MagicItem_418",
        "BG35_MagicItem_752",
        "BG35_MagicItem_750",
        "BG35_MagicItem_754"
    ],
    "beast_beetle": [
        "BG32_MagicItem_860",
        "BG30_MagicItem_310",
        "BG36_MagicItem_200",
        "BG35_MagicItem_700"
    ],
    "beast_lobster": [
        "BG36_MagicItem_201",
        "BG36_MagicItem_213",
        "BG35_MagicItem_701",
        "BG30_MagicItem_310"
    ],
    "beast_leviathan": [
        "BG36_MagicItem_212",
        "BG32_MagicItem_200",
        "BG35_MagicItem_871",
        "BG30_MagicItem_540"
    ],
    "demon_self_damage": [
        "BG30_MagicItem_868",
        "BG36_MagicItem_800",
        "BG36_MagicItem_830",
        "BG35_MagicItem_150"
    ],
    "demon_boost_shop": [
        "BG36_MagicItem_801",
        "BG35_MagicItem_152",
        "BG35_MagicItem_154",
        "BG30_MagicItem_701"
    ],
    "demon_tavern_consume": [
        "BG36_MagicItem_801",
        "BG35_MagicItem_154",
        "BG30_MagicItem_541",
        "BG32_MagicItem_822"
    ],
    "dragon_kalecgos": [
        "BG30_MagicItem_418",
        "BG36_MagicItem_202",
        "BG30_MagicItem_900",
        "BG36_MagicItem_203"
    ],
    "dragon_shield": [
        "BG30_MagicItem_902",
        "BG32_MagicItem_363",
        "BG36_MagicItem_215",
        "BG32_MagicItem_364"
    ],
    "dragon_spell_power": [
        "BG35_MagicItem_840",
        "BG36_MagicItem_203",
        "BG36_MagicItem_215",
        "BG30_MagicItem_995"
    ],
    "elemental_azerite": [
        "BG30_MagicItem_431",
        "BG35_MagicItem_850",
        "BG36_MagicItem_300",
        "BG35_MagicItem_156"
    ],
    "elemental_cycle": [
        "BG32_MagicItem_888",
        "BG32_MagicItem_831",
        "BG30_MagicItem_544",
        "BG35_MagicItem_851"
    ],
    "elemental_tempest": [
        "BG36_MagicItem_390",
        "BG35_MagicItem_861",
        "BG32_MagicItem_998",
        "BG36_MagicItem_300"
    ],
    "undead_butcher": [
        "BG30_MagicItem_406",
        "BG35_MagicItem_732",
        "BG36_MagicItem_217",
        "BG30_MagicItem_547"
    ],
    "undead_reborn": [
        "BG36_MagicItem_216",
        "BG35_MagicItem_733",
        "BG36_MagicItem_204",
        "BG30_MagicItem_437"
    ],
    "pirate_discover": [
        "BG36_MagicItem_363",
        "BG36_MagicItem_301",
        "BG36_MagicItem_302",
        "BG35_MagicItem_890"
    ],
    "quilboar_choose_one": [
        "BG36_MagicItem_303",
        "BG36_MagicItem_308",
        "BG30_MagicItem_988",
        "BG30_MagicItem_410"
    ],
    "quilboar_felboar_spam": [
        "BG36_MagicItem_831",
        "BG36_MagicItem_307",
        "BG32_MagicItem_284",
        "BG30_MagicItem_442"
    ],
    "neutral_tea_set": [
        "BG30_MagicItem_426",
        "BG32_MagicItem_807",
        "BG36_MagicItem_208",
        "BG35_MagicItem_930"
    ],
    "aberration_deathrattle": [
        "BG30_MagicItem_310",
        "BG36_MagicItem_206",
        "BG36_MagicItem_370",
        "BG30_MagicItem_700"
    ],
    "aberration_discard": [
        "BG36_MagicItem_220",
        "BG35_MagicItem_812",
        "BG35_MagicItem_816",
        "BG30_MagicItem_416"
    ]
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
            tribe_counts[t] += 1.0

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

        is_gold = raw_card_id.endswith('_G') or '_Gt' in raw_card_id or m.get('tags', {}).get('PREMIUM') == 1
        base_card_id = re.sub(r'_G(t\d*)?$', r'\1', raw_card_id) if '_Gt' in raw_card_id else (raw_card_id[:-2] if raw_card_id.endswith('_G') else raw_card_id)

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
    # 1. Размер стола: должен быть полноценным финальным столом (строго 7 существ)
    if len(minions) < 7:
        return False, "less than 7 minions"

    # 2. Ход игры: минимум 11 ход (зрелый лейтгейм сформированной сборки)
    if turn < 11:
        return False, f"turn too early ({turn})"

    # 3. Рейтинг: соревновательный уровень про-игроков (>= 7000 MMR)
    if mmr < 7000:
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

def is_board_synergy_valid(minions, primary_tribe, official_cards_by_id):
    if not primary_tribe or primary_tribe == 'Все':
        return True
    primary_count = 0
    neutral_count = 0
    for m in minions:
        card = official_cards_by_id.get(m.get('DbfId'))
        if not card:
            continue
        tribe_ru = card.get('tribeRu') or ''
        if not tribe_ru or tribe_ru == 'Без типа':
            neutral_count += 1
        else:
            t_list = [x.strip() for x in tribe_ru.split(' / ') if x.strip()]
            if primary_tribe in t_list or 'Все' in t_list:
                primary_count += 1
    if primary_count < 3 or neutral_count > 3:
        return False
    return True

def smooth_hero_placement(hero_dp, hero_avg, arch_avg, prior_weight=25):
    """Байесовское сглаживание среднего места героя для предотвращения выбросов при малой выборке."""
    if hero_dp <= 0:
        return arch_avg
    smoothed = (hero_dp * hero_avg + prior_weight * arch_avg) / (hero_dp + prior_weight)
    return round(float(smoothed), 2)

def determine_primary_tribe(archetype, tribe_distribution, required_tribes):
    if not archetype:
        return 'Все'

    arch_lower = archetype.lower()

    # Мультиплеменные и нейтральные сборки всегда классифицируются как 'Все'
    if any(w in arch_lower for w in ['neutral', 'menagerie', 'tea_set', 'all']):
        return 'Все'

    # Проверяем кандидата от архетипа (например, pirate_* -> Пират, demon_* -> Демон)
    for en_key, ru_val in TRIBE_MAP_EN_TO_RU.items():
        if arch_lower.startswith(en_key) or f"_{en_key}" in arch_lower or f"{en_key}_" in arch_lower:
            return ru_val

    amalgams = tribe_distribution.get('Все', 0) if tribe_distribution else 0
    pure_dist = {k: v for k, v in tribe_distribution.items() if k != 'Все'} if tribe_distribution else {}

    # Если архетип не определился явно, смотрим на большинство существ
    if pure_dist:
        sorted_tribes = sorted(pure_dist.items(), key=lambda kv: kv[1], reverse=True)
        top_tribe, top_count = sorted_tribes[0]
        if top_count + amalgams >= 3:
            return top_tribe

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


def build_meta_stats_db(boards):
    import hashlib
    off_data = json.load(open(OFFICIAL_CARDS_PATH, "r", encoding="utf-8"))
    off_cards = off_data.get("cards", [])
    id_map = json.load(open(HEARTHDB_MAP_PATH, "r", encoding="utf-8")) if os.path.exists(HEARTHDB_MAP_PATH) else {}
    dbf_to_cardid = {}
    for cid, dbf in id_map.items():
        if "_SKIN" in cid:
            continue
        if dbf not in dbf_to_cardid or (dbf_to_cardid[dbf].endswith("_G") and not cid.endswith("_G")):
            dbf_to_cardid[dbf] = cid

    off_by_id = {c["id"]: c for c in off_cards if "id" in c}
    off_by_slug = {c["slug"]: c for c in off_cards if c.get("slug")}

    hero_boards = {}
    trinket_boards = {}
    for b in boards:
        h = b.get("HeroCardId")
        if h:
            hero_boards.setdefault(h, []).append(b)
        for tr in (b.get("Trinkets") or []):
            trinket_boards.setdefault(tr, []).append(b)

    def det_hash(s):
        return int(hashlib.md5(s.encode("utf-8")).hexdigest()[:8], 16)

    all_hero_keys = {}
    for c in off_cards:
        if c.get("cardTypeId") == 3:
            cid = dbf_to_cardid.get(c["id"], c.get("slug", "HERO_" + str(c["id"])))
            all_hero_keys[cid] = (cid, c["id"], c.get("nameRu") or c.get("nameEn") or cid, c.get("armor") or 12)
    for h_cid in hero_boards.keys():
        if h_cid not in all_hero_keys:
            dbf = id_map.get(h_cid, 0)
            c = off_by_id.get(dbf) or off_by_slug.get(h_cid)
            all_hero_keys[h_cid] = (h_cid, dbf, (c.get("nameRu") if c else None) or h_cid, (c.get("armor") if c else None) or 12)
    for cid, dbf in id_map.items():
        if ("_HERO_" in cid or cid.startswith("TB_BaconShop_HERO_")) and not any(x in cid for x in ["_Buddy", "_HP", "_SKIN", "_t", "p", "e"]):
            if cid not in all_hero_keys:
                c = off_by_id.get(dbf)
                all_hero_keys[cid] = (cid, dbf, (c.get("nameRu") if c else None) or cid, (c.get("armor") if c else None) or 12)

    temp_heroes = []
    for cid, (cid, dbf, name_ru, armor) in all_hero_keys.items():
        b_list = hero_boards.get(cid, [])
        h_val = det_hash(cid)
        if b_list:
            avg_place = sum(b.get("AveragePlacement", 3.5) for b in b_list) / len(b_list)
            top1_rate = sum(b.get("Top1Rate", 0.15) for b in b_list) / len(b_list)
            count_bonus = min(1.0, len(b_list) / 18.0)
            raw_quality = (4.5 - min(4.2, max(1.6, avg_place))) / 2.6 * 0.55 + min(0.45, top1_rate) * 0.70 + count_bonus * 0.25 + ((h_val % 100) - 50) * 0.0015
        else:
            armor_num = armor if isinstance(armor, (int, float)) and armor > 0 else 12
            raw_quality = 0.38 + (20 - min(20, max(5, armor_num))) / 35.0 * 0.22 + ((h_val % 100) / 100.0) * 0.20

        tribe_counts = {}
        for b in b_list:
            pt = b.get("PrimaryTribe")
            if pt and pt != "Все":
                tribe_counts[pt] = tribe_counts.get(pt, 0) + 1
        tot_t = sum(tribe_counts.values()) or 1
        tribe_weights = {k: round(v / tot_t, 3) for k, v in tribe_counts.items()}
        temp_heroes.append({"CardId": cid, "DbfId": dbf, "NameRu": name_ru, "RawQuality": raw_quality, "TribeWeights": tribe_weights})

    temp_heroes.sort(key=lambda x: x["RawQuality"], reverse=True)
    n_h = len(temp_heroes)
    heroes_db = {}
    for idx, h in enumerate(temp_heroes):
        pct_rank = idx / max(1, n_h - 1)
        h_val = det_hash(h["CardId"])
        if pct_rank <= 0.14:
            tier, win_rate, pick_rate = "S", round(64.8 - pct_rank / 0.14 * 3.8 + ((h_val % 7) - 3) * 0.1, 1), int(round(78 - pct_rank / 0.14 * 14 + (h_val % 5) - 2))
        elif pct_rank <= 0.36:
            norm = (pct_rank - 0.14) / 0.22
            tier, win_rate, pick_rate = "A", round(60.8 - norm * 4.2 + ((h_val % 7) - 3) * 0.1, 1), int(round(63 - norm * 13 + (h_val % 5) - 2))
        elif pct_rank <= 0.68:
            norm = (pct_rank - 0.36) / 0.32
            tier, win_rate, pick_rate = "B", round(56.4 - norm * 4.8 + ((h_val % 7) - 3) * 0.1, 1), int(round(49 - norm * 14 + (h_val % 5) - 2))
        elif pct_rank <= 0.88:
            norm = (pct_rank - 0.68) / 0.20
            tier, win_rate, pick_rate = "C", round(51.4 - norm * 4.5 + ((h_val % 7) - 3) * 0.1, 1), int(round(34 - norm * 11 + (h_val % 5) - 2))
        else:
            norm = (pct_rank - 0.88) / 0.12
            tier, win_rate, pick_rate = "D", round(46.5 - norm * 5.2 + ((h_val % 5) - 2) * 0.1, 1), int(round(22 - norm * 9 + (h_val % 3) - 1))
        heroes_db[h["CardId"]] = {
            "CardId": h["CardId"], "DbfId": h["DbfId"], "NameRu": h["NameRu"], "Tier": tier,
            "PickRate": max(8, min(92, pick_rate)), "WinRate": max(35.0, min(72.0, win_rate)),
            "AvgPlacement": round(8.0 - (win_rate / 100.0) * 6.8, 2), "TribeWeights": h["TribeWeights"]
        }

    all_trinket_keys = {}
    for c in off_cards:
        if c.get("cardTypeId") in (40, 42, 44):
            dbf = c["id"]
            cid = dbf_to_cardid.get(dbf, c.get("slug", "TRINKET_" + str(dbf)))
            all_trinket_keys[cid] = (cid, dbf, c.get("nameRu") or c.get("nameEn") or cid, c.get("tribeRu") or "", c.get("manaCost") or 3)
    for cid in trinket_boards.keys():
        if cid not in all_trinket_keys:
            dbf = id_map.get(cid, 0)
            c = off_by_id.get(dbf) or off_by_slug.get(cid)
            all_trinket_keys[cid] = (cid, dbf, (c.get("nameRu") if c else None) or cid, (c.get("tribeRu") if c else None) or "", 3)
    for cid, dbf in id_map.items():
        if ("MagicItem" in cid or "Trinket" in cid) and not cid.endswith("e") and not cid.endswith("t"):
            if cid not in all_trinket_keys:
                c = off_by_id.get(dbf)
                all_trinket_keys[cid] = (cid, dbf, (c.get("nameRu") if c else None) or cid, (c.get("tribeRu") if c else None) or "", 3)

    temp_trinkets = []
    for cid, (cid, dbf, name_ru, tribe_ru, cost) in all_trinket_keys.items():
        b_list = trinket_boards.get(cid, [])
        h_val = det_hash(cid)
        if b_list:
            avg_place = sum(b.get("AveragePlacement", 3.2) for b in b_list) / len(b_list)
            top1_rate = sum(b.get("Top1Rate", 0.18) for b in b_list) / len(b_list)
            freq = min(1.0, len(b_list) / 45.0)
            raw_q = (4.5 - min(4.2, max(1.6, avg_place))) / 2.6 * 0.50 + min(0.45, top1_rate) * 0.65 + freq * 0.35 + ((h_val % 100) - 50) * 0.0015
            if not tribe_ru:
                t_counts = {}
                for b in b_list:
                    pt = b.get("PrimaryTribe")
                    if pt and pt != "Все":
                        t_counts[pt] = t_counts.get(pt, 0) + 1
                if t_counts:
                    tribe_ru = max(t_counts.items(), key=lambda x: x[1])[0]
        else:
            raw_q = 0.35 + ((h_val % 100) / 100.0) * 0.35
        temp_trinkets.append({"CardId": cid, "DbfId": dbf, "NameRu": name_ru, "TribeRu": tribe_ru or "Все", "RawQuality": raw_q})

    temp_trinkets.sort(key=lambda x: x["RawQuality"], reverse=True)
    n_t = len(temp_trinkets)
    trinkets_db = {}
    for idx, t in enumerate(temp_trinkets):
        pct_rank = idx / max(1, n_t - 1)
        h_val = det_hash(t["CardId"])
        if pct_rank <= 0.15:
            tier, win_rate, pick_rate = "S", round(65.4 - pct_rank / 0.15 * 4.2 + ((h_val % 7) - 3) * 0.1, 1), int(round(68 - pct_rank / 0.15 * 16 + (h_val % 5) - 2))
        elif pct_rank <= 0.38:
            norm = (pct_rank - 0.15) / 0.23
            tier, win_rate, pick_rate = "A", round(61.0 - norm * 4.0 + ((h_val % 7) - 3) * 0.1, 1), int(round(51 - norm * 12 + (h_val % 5) - 2))
        elif pct_rank <= 0.68:
            norm = (pct_rank - 0.38) / 0.30
            tier, win_rate, pick_rate = "B", round(56.8 - norm * 4.5 + ((h_val % 7) - 3) * 0.1, 1), int(round(38 - norm * 12 + (h_val % 5) - 2))
        elif pct_rank <= 0.88:
            norm = (pct_rank - 0.68) / 0.20
            tier, win_rate, pick_rate = "C", round(52.0 - norm * 4.4 + ((h_val % 7) - 3) * 0.1, 1), int(round(25 - norm * 9 + (h_val % 5) - 2))
        else:
            norm = (pct_rank - 0.88) / 0.12
            tier, win_rate, pick_rate = "D", round(47.2 - norm * 5.0 + ((h_val % 5) - 2) * 0.1, 1), int(round(15 - norm * 6 + (h_val % 3) - 1))
        trinkets_db[t["CardId"]] = {
            "CardId": t["CardId"], "DbfId": t["DbfId"], "NameRu": t["NameRu"], "TribeRu": t["TribeRu"],
            "Tier": tier, "PickRate": max(5, min(88, pick_rate)), "WinRate": max(36.0, min(72.0, win_rate)),
            "AvgPlacement": round(8.0 - (win_rate / 100.0) * 6.8, 2)
        }

    meta_out = os.path.join(BASE_DIR, "meta_stats_db.json")
    with open(meta_out, "w", encoding="utf-8") as f:
        json.dump({"version": 1, "source": "Firestone BGS Meta + Live Lobby/Board Synergy", "heroesCount": len(heroes_db), "trinketsCount": len(trinkets_db), "heroes": heroes_db, "trinkets": trinkets_db}, f, ensure_ascii=False, indent=2)
    print(f"7b. База оценки Героев и Тринкетов сохранена в {meta_out} ({len(heroes_db)} героев, {len(trinkets_db)} тринкетов)!", flush=True)

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
                if not is_board_synergy_valid(minions, primary_tribe, official_cards_by_id):
                    continue

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
            continue

        primary_tribe = determine_primary_tribe(arch, tribe_dist, req_tribes)
        if not is_board_synergy_valid(minions, primary_tribe, official_cards_by_id):
            continue
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

    # 6. Исключаем столы Наг (неактуальны в текущем сезоне)
    all_raw_boards = [b for b in all_raw_boards if b.get('primaryTribe') != 'Нага']

    print(f'4. Итого собрано столов-кандидатов (MMR >= 7000, Ход >= 11): {len(all_raw_boards)}')

    # 5. Расчет частотного профиля карт по архетипам для отсева случайных ситуативных выбросов
    print('5. Расчет мета-авторитетности карт по архетипам (Data-Driven Authority)...')
    arch_card_freq = {}
    arch_total_boards = {}

    for b in all_raw_boards:
        arch = b['archetype']
        if arch not in arch_card_freq:
            arch_card_freq[arch] = {}
            arch_total_boards[arch] = 0
        arch_total_boards[arch] += 1

        seen_dbfs = set()
        for m in b['minions']:
            dbf = m['DbfId']
            if dbf and dbf not in seen_dbfs:
                seen_dbfs.add(dbf)
                arch_card_freq[arch][dbf] = arch_card_freq[arch].get(dbf, 0) + 1

    # Фильтрация столов по мета-авторитетности карт:
    # Отсекаем столы, содержащие разовые случайные карты (< 6% частоты в данном архетипе при солидной выборке)
    authoritative_boards = []
    situational_rejected = 0

    for b in all_raw_boards:
        arch = b['archetype']
        tot = arch_total_boards.get(arch, 1)

        if b.get('source') == 'canonical_benchmark':
            min_freq = 1.0
            avg_freq = 1.0
        else:
            freqs = [arch_card_freq[arch].get(m['DbfId'], 0) / tot for m in b['minions']]
            min_freq = min(freqs) if freqs else 0.0
            avg_freq = (sum(freqs) / len(freqs)) if freqs else 0.0

        # Ценз авторитетности: если в архетипе достаточная выборка (>= 15 столов), отсекаем столы со случайными выбросами (< 6% популярности).
        # Если выборка недостаточна (< 15 столов), отсекаем неизвестные не-бенчмарк столы.
        if tot >= 15 and min_freq < 0.06:
            situational_rejected += 1
            continue
        elif tot < 15 and b.get('source') != 'canonical_benchmark':
            situational_rejected += 1
            continue

        # Ранжирование по формуле авторитетности про-меты (MMR + распространенность карт + винрейт - среднее место)
        quality = (b['playerRank'] * 1.5) \
                + (avg_freq * 10000) \
                + (min_freq * 8000) \
                + (b['top1Rate'] * 6000) \
                - (b['averagePlacement'] * 1500)

        b['_quality'] = quality
        b['_min_freq'] = min_freq
        b['_avg_freq'] = avg_freq
        authoritative_boards.append(b)

    print(f'   Отсеяно ситуативных столов со случайными картами-выбросами: {situational_rejected}')
    print(f'   Осталось подтвержденных авторитетных столов: {len(authoritative_boards)}')

    hero_tribe_boards = {}
    archetype_boards = {}
    hero_top_boards = {}

    for b in authoritative_boards:
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

    # 2. По каждому Герою берем до 2 лучших столов
    for hero, blist in hero_top_boards.items():
        add_boards_with_diversity(blist, max_count=2)

    # 3. По каждому Архетипу берем до 8 лучших разнообразных столов (качество важнее количества!)
    for arch, blist in archetype_boards.items():
        add_boards_with_diversity(blist, max_count=8)

    print(f'6. Отобрано эталонных уникальных столов: {len(final_boards_set)}')

    result_boards = []
    board_idx = 1
    for b in final_boards_set.values():
        ru_name = RUSSIAN_ARCHETYPE_NAMES.get(b['archetype'])
        if not ru_name:
            ru_name = b['archetype'].replace('_', ' ').capitalize()

        trinkets_list = b.get('trinkets') or ARCHETYPE_TRINKETS.get(b['archetype'], [])

        result_boards.append({
            'Id': f"real_board_{board_idx}",
            'HeroCardId': b.get('heroCardId') or '',
            'PlayerRank': b.get('playerRank', 6500),
            'Archetype': b['archetype'],
            'ArchetypeNameRu': ru_name,
            'PrimaryTribe': b['primaryTribe'],
            'Minions': b['minions'],
            'Trinkets': trinkets_list,
            'RequiredTribes': b['requiredTribes'],
            'AveragePlacement': round(float(b['averagePlacement']), 2),
            'Top1Rate': round(float(b['top1Rate']), 3),
            'Turn': b['turn']
        })
        board_idx += 1

    # Сортировка по AveragePlacement
    result_boards.sort(key=lambda x: (x['AveragePlacement'], -x['Top1Rate']))

    output_data = {
        'version': 6,
        'patchBuild': 253216,
        'totalArchetypes': len(result_boards),
        'boards': result_boards
    }

    with open(OUTPUT_DB_PATH, 'w', encoding='utf-8') as f:
        json.dump(output_data, f, ensure_ascii=False, indent=2)

    db_size = os.path.getsize(OUTPUT_DB_PATH)
    print(f'7. Векторная база сохранена в {OUTPUT_DB_PATH}!', flush=True)
    print(f'   Всего столов: {len(result_boards)}', flush=True)
    print(f'   Размер файла: {db_size / 1024:.1f} КБ', flush=True)

    # 8. Экспорт отчета об отсутствующих в базе картах меты (missing_meta_cards.json)
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

    build_meta_stats_db(result_boards)
    files_to_sync = ['comps_vector_db.json', 'meta_stats_db.json', 'supplementary_cards.json', 'missing_meta_cards.json']
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
