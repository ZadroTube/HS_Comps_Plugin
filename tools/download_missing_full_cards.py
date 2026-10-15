import json
import os
import shutil
import sys
import urllib.request
from concurrent.futures import ThreadPoolExecutor

sys.stdout.reconfigure(encoding='utf-8')

def download_missing_cards(base_dir=None):
    if not base_dir:
        base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    
    official_cards_path = os.path.join(base_dir, "bg_cards_official.json")
    supp_cards_path = os.path.join(base_dir, "supplementary_cards.json")
    vector_db_path = os.path.join(base_dir, "comps_vector_db.json")
    output_dir = os.path.join(base_dir, "assets", "cards", "full")

    if not os.path.exists(official_cards_path) or not os.path.exists(vector_db_path):
        print(f"[!] Файлы базы не найдены для проверки артов в {base_dir}")
        return 0, 0

    os.makedirs(output_dir, exist_ok=True)

    with open(official_cards_path, "r", encoding="utf-8") as f:
        official_data = json.load(f)

    official_cards = {c["id"]: c for c in official_data.get("cards", []) if c.get("id")}
    official_by_slug = {c["slug"]: c for c in official_data.get("cards", []) if c.get("slug")}

    if os.path.exists(supp_cards_path):
        try:
            with open(supp_cards_path, "r", encoding="utf-8") as sf:
                supp_data = json.load(sf)
            for c in supp_data.get("cards", []):
                if c.get("id"):
                    official_cards[c["id"]] = c
                if c.get("slug"):
                    official_by_slug[c["slug"]] = c
        except Exception:
            pass

    with open(vector_db_path, "r", encoding="utf-8") as f:
        vector_data = json.load(f)

    needed_cards = {}
    for board in vector_data.get("boards", []):
        for minion in board.get("Minions", []):
            dbf_id = minion.get("DbfId", 0)
            card_id = minion.get("CardId", "")
            is_golden = minion.get("IsGolden", False) or (card_id and card_id.endswith("_G"))

            clean_id = card_id[:-2] if card_id.endswith("_G") else card_id

            # 1. Обычная версия карты
            norm_key = ('normal', dbf_id, clean_id)
            if norm_key not in needed_cards:
                needed_cards[norm_key] = {
                    "DbfId": dbf_id,
                    "CardId": clean_id,
                    "IsGolden": False
                }

            # 2. Золотая версия карты (если используется на столе)
            if is_golden:
                gold_key = ('golden', dbf_id, card_id)
                if gold_key not in needed_cards:
                    needed_cards[gold_key] = {
                        "DbfId": dbf_id,
                        "CardId": card_id,
                        "IsGolden": True
                    }

    print(f"7. Проверка наличия официальных артов для {len(needed_cards)} элементов столов (обычные и золотые)...", flush=True)

    tasks = []
    for key, info in needed_cards.items():
        is_gold = info["IsGolden"]
        dbf_id = info["DbfId"]
        card_id = info["CardId"]
        clean_card_id = card_id[:-2] if (card_id and card_id.endswith("_G")) else card_id

        off_card = official_cards.get(dbf_id)
        if not off_card and clean_card_id:
            off_card = official_by_slug.get(clean_card_id)

        if is_gold:
            gold_filename = f"{clean_card_id}_G.png"
            p_gold = os.path.join(output_dir, gold_filename)
            p_card = os.path.join(output_dir, f"{card_id}.png") if card_id else None
            gold_dbf = off_card.get("goldenUpgradeId", 0) if off_card else 0
            p_gold_dbf = os.path.join(output_dir, f"{gold_dbf}.png") if gold_dbf and gold_dbf > 0 else None

            already_have = False
            for p in [p_gold, p_card]:
                if p and os.path.exists(p) and os.path.getsize(p) > 5000:
                    already_have = True
                    break

            if already_have:
                continue

            name = off_card.get("nameRu", "") if off_card else clean_card_id

            url_candidates = []
            if clean_card_id.startswith("TB_BaconUps_"):
                url_candidates.append(f"https://art.hearthstonejson.com/v1/bgs/latest/ruRU/512x/{clean_card_id}_triple.png")
                url_candidates.append(f"https://art.hearthstonejson.com/v1/render/latest/ruRU/512x/{clean_card_id}.png")
            else:
                url_candidates.append(f"https://art.hearthstonejson.com/v1/bgs/latest/ruRU/512x/{clean_card_id}_G_triple.png")
                url_candidates.append(f"https://art.hearthstonejson.com/v1/bgs/latest/ruRU/512x/{clean_card_id}_triple.png")
                url_candidates.append(f"https://art.hearthstonejson.com/v1/render/latest/ruRU/512x/{clean_card_id}_G.png")

            # Гарантированный резерв с CloudFront, если на hearthstonejson нет золотого рендера
            if off_card and off_card.get("imageRu"):
                url_candidates.append(off_card["imageRu"])

            targets = [p_gold]
            if p_card and p_card != p_gold:
                targets.append(p_card)
            if p_gold_dbf and p_gold_dbf not in targets:
                targets.append(p_gold_dbf)

            tasks.append((url_candidates, targets, f"[Золотая] {name}", dbf_id, card_id))
        else:
            p_dbf = os.path.join(output_dir, f"{dbf_id}.png") if dbf_id > 0 else None
            p_card = os.path.join(output_dir, f"{card_id}.png") if card_id else None
            p_clean = os.path.join(output_dir, f"{clean_card_id}.png") if clean_card_id else None

            already_have = False
            for p in [p_dbf, p_card, p_clean]:
                if p and os.path.exists(p) and os.path.getsize(p) > 5000:
                    already_have = True
                    break

            if already_have:
                continue

            url_candidates = []
            if off_card and off_card.get("imageRu"):
                url_candidates.append(off_card["imageRu"])
            if clean_card_id:
                url_candidates.append(f"https://art.hearthstonejson.com/v1/bgs/latest/ruRU/512x/{clean_card_id}.png")
                url_candidates.append(f"https://art.hearthstonejson.com/v1/render/latest/ruRU/512x/{clean_card_id}.png")

            targets = []
            if p_dbf: targets.append(p_dbf)
            if p_card: targets.append(p_card)
            if p_clean and p_clean not in targets: targets.append(p_clean)

            name = off_card.get("nameRu", "") if off_card else clean_card_id
            tasks.append((url_candidates, targets, name, dbf_id, card_id))

    if not tasks:
        print("   Все необходимые официальные арты карт (обычные и золотые) уже присутствуют локально (100%).", flush=True)
        sync_to_appdata(output_dir)
        return 0, 0

    print(f"   Требуется дозагрузить артов карт: {len(tasks)}...", flush=True)

    def download_card(task):
        url_candidates, targets, name, dbf, cid = task
        for url in url_candidates:
            try:
                req = urllib.request.Request(
                    url,
                    headers={"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"}
                )
                with urllib.request.urlopen(req, timeout=15) as resp:
                    data = resp.read()
                    if len(data) > 2000:
                        for target in targets:
                            with open(target, "wb") as out_f:
                                out_f.write(data)
                        return True, f"OK: {name} ({dbf}/{cid})"
            except Exception:
                continue
        return False, f"FAILED: {name} ({cid})"

    success = 0
    failed = 0
    with ThreadPoolExecutor(max_workers=24) as executor:
        for ok, msg in executor.map(download_card, tasks):
            if ok:
                success += 1
            else:
                failed += 1

    sync_to_appdata(output_dir)
    print(f"   Завершено: успешно загружено {success}, ошибок {failed}.", flush=True)
    return success, failed

def sync_to_appdata(source_full_dir):
    try:
        appdata = os.environ.get("APPDATA")
        if not appdata:
            return
        hdt_full_dir = os.path.join(appdata, "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "assets", "cards", "full")
        if os.path.abspath(source_full_dir).lower() == os.path.abspath(hdt_full_dir).lower():
            return
        os.makedirs(hdt_full_dir, exist_ok=True)
        for fname in os.listdir(source_full_dir):
            if fname.endswith(".png"):
                src = os.path.join(source_full_dir, fname)
                dst = os.path.join(hdt_full_dir, fname)
                if not os.path.exists(dst):
                    shutil.copy2(src, dst)
    except Exception:
        pass

if __name__ == '__main__':
    download_missing_cards()
