"""Add official WRCG vocabulary to the repository database.

Run once when maintaining data.zdb. This is not a runtime migration. Missing voice
recordings must not fall back to a different direction (e.g. bridge vs under bridge).
An explicit --merge-test-data path adds only these tokens to a local test database,
after a SQLite backup, preserving all unrelated records. This is not app startup code.
"""
from pathlib import Path
import sqlite3
import argparse
import uuid

DATABASE = Path(__file__).resolve().parents[2] / "src/ZTMZ.PacenoteTool.Base/data.zdb"
LOCALES = ("en-us", "zh-cn", "fr-fr", "de-de", "es-es", "it-it", "ja-jp", "ko-kr", "pt-br", "ru-ru")
TOKENS = (
    ("under_bridge", 7, ("under bridge", "桥下通过", "sous le pont", "unter der Brücke", "bajo el puente", "sotto il ponte", "橋の下", "다리 아래", "sob a ponte", "под мостом")),
    ("under_arch", 7, ("under arch", "拱门下通过", "sous l’arche", "unter dem Torbogen", "bajo el arco", "sotto l’arco", "アーチの下", "아치 아래", "sob o arco", "под аркой")),
    ("go_back", 9, ("turn back", "掉头返回", "faire demi-tour", "umkehren", "dar la vuelta", "tornare indietro", "引き返す", "되돌아가기", "voltar", "развернуться назад")),
    ("forest", 4, ("forest", "树林", "forêt", "Wald", "bosque", "bosco", "森林", "숲", "floresta", "лес")),
    ("canyon", 4, ("canyon", "峡谷", "canyon", "Schlucht", "cañón", "gola", "峡谷", "협곡", "desfiladeiro", "каньон")),
)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--merge-test-data", type=Path)
    args = parser.parse_args()
    target = args.merge_test_data.resolve() if args.merge_test_data else DATABASE
    with sqlite3.connect(target.as_uri() + "?mode=rw", uri=True) as db:
        if args.merge_test_data:
            backup = target.with_name(target.name + ".before-wrcg-" + uuid.uuid4().hex + ".bak")
            with sqlite3.connect(backup) as saved:
                db.backup(saved)
            print(f"Backup: {backup}")
        for token, category, names in TOKENS:
            existing = db.execute("SELECT id FROM pacenote_filenames WHERE filename=?", (token,)).fetchall()
            if len(existing) > 1:
                raise ValueError(f"Ambiguous token: {token}")
            if existing:
                token_id = existing[0][0]
                actual = db.execute("SELECT description,type FROM pacenote WHERE id=?", (token_id,)).fetchone()
                if actual != (names[1], category):
                    raise ValueError(f"Existing token differs: {token}")
            else:
                token_id = db.execute("SELECT MAX(id)+1 FROM pacenote").fetchone()[0]
                db.execute("INSERT INTO pacenote(id,description,type,complexity,fallback) VALUES(?,?,?,0,NULL)", (token_id,names[1],category))
                db.execute("INSERT INTO pacenote_filenames(id,filename,is_primary) VALUES(?,?,1)", (token_id,token))
            for locale, name in zip(LOCALES, names, strict=True):
                db.execute("INSERT INTO pacenote_localizations(pacenote_id,locale,display_name,description) VALUES(?,?,?,?) ON CONFLICT(pacenote_id,locale) DO NOTHING", (token_id,locale,name,name))
        if db.execute("PRAGMA integrity_check").fetchone()[0] != "ok":
            raise ValueError("Database integrity check failed")
    print("Official WRCG vocabulary updated (5 tokens, 10 languages).")

if __name__ == "__main__":
    main()
