import json
import os
import sys
import urllib.request
from concurrent.futures import ThreadPoolExecutor

sys.stdout.reconfigure(encoding='utf-8')

def download_missing_cards(base_dir=None):
    if not base_dir:
        base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    
    official_cards_path = os.path.join(base_dir, "bg_cards_official.json")
    vector_db_path = os.path.join(base_dir, "comps_vector_db.json")
    output_dir = os.path.join(base_dir, "assets", "cards", "full")

    if not os.path.exists(official_cards_path) or not os.path.exists(vector_db_path):
        print(f"[!] Файлы базы не найдены для проверки артов в {base_dir}")
        return 0, 0

    os.makedirs(output_dir, exist_ok=True)

    with open(official_cards_path, "r", encoding="utf-8") as f:
        official_data = json.load(f)

    official_cards = {c["id"]: c for c in official_data.get("cards", [])}
    official_by_slug = {c["slug"]: c for c in official_data.get("cards", []) if c.get("slug")}

    with open(vector_db_path, "r", encoding="utf-8") as f:
        vector_data = json.load(f)

    needed_cards = {}
    for board in vector_data.get("boards", []):
        for minion in board.get("Minions", []):
            dbf_id = minion.get("DbfId", 0)
            card_id = minion.get("CardId", "")
            key = dbf_id if dbf_id > 0 else card_id
            if key not in needed_cards:
                needed_cards[key] = {
                    "DbfId": dbf_id,
                    "CardId": card_id
                }

    print(f"7. Проверка наличия официальных артов для {len(needed_cards)} уникальных существ...", flush=True)

    tasks = []
    for key, info in needed_cards.items():
        dbf_id = info["DbfId"]
        card_id = info["CardId"]
        clean_card_id = card_id.replace("_G", "") if card_id else ""

        p_dbf = os.path.join(output_dir, f"{dbf_id}.png") if dbf_id > 0 else None
        p_card = os.path.join(output_dir, f"{card_id}.png") if card_id else None
        p_clean = os.path.join(output_dir, f"{clean_card_id}.png") if clean_card_id else None

        already_have = False
        for p in [p_dbf, p_card, p_clean]:
            if p and os.path.exists(p) and os.path.getsize(p) > 10000:
                already_have = True
                break

        if already_have:
            continue

        off_card = official_cards.get(dbf_id)
        if not off_card and clean_card_id:
            off_card = official_by_slug.get(clean_card_id)

        if off_card and off_card.get("imageRu"):
            url = off_card["imageRu"]
            targets = []
            if p_dbf: targets.append(p_dbf)
            if p_card: targets.append(p_card)
            if p_clean and p_clean != p_card: targets.append(p_clean)
            tasks.append((url, targets, off_card.get("nameRu", ""), dbf_id, card_id))

    if not tasks:
        print("   Все необходимые официальные арты карт уже присутствуют локально (100%).", flush=True)
        return 0, 0

    print(f"   Требуется дозагрузить карт: {len(tasks)}...", flush=True)

    def download_card(task):
        url, targets, name, dbf, cid = task
        try:
            req = urllib.request.Request(
                url,
                headers={"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"}
            )
            with urllib.request.urlopen(req, timeout=15) as resp:
                data = resp.read()
                if len(data) > 5000:
                    for target in targets:
                        with open(target, "wb") as out_f:
                            out_f.write(data)
                    return True, f"OK: {name} ({dbf}/{cid})"
                else:
                    return False, f"Too small: {name}"
        except Exception as e:
            return False, f"FAILED: {name} - {e}"

    success = 0
    failed = 0
    with ThreadPoolExecutor(max_workers=16) as executor:
        for ok, msg in executor.map(download_card, tasks):
            if ok:
                success += 1
            else:
                failed += 1

    print(f"   Завершено: успешно загружено {success}, ошибок {failed}.", flush=True)
    return success, failed

if __name__ == '__main__':
    download_missing_cards()
