"""Makes the made-up Chrome profile the tests read (tests/KUDUBROWSE.Tests/Fixtures/ChromeUserData).

Run: python tools/make-chrome-fixture.py   (only needed if the fixture is changed)
The History file is a real SQLite database with Chrome's "urls" table: 3000 pages (so the table spreads over
several levels of pages), one with a very long title (so it continues on extra pages), and a hidden one.
"""
import json, os, sqlite3

root = os.path.join(os.path.dirname(__file__), "..", "tests", "KUDUBROWSE.Tests", "Fixtures", "ChromeUserData")
default = os.path.join(root, "Default")
work = os.path.join(root, "Profile 1")
os.makedirs(default, exist_ok=True)
os.makedirs(work, exist_ok=True)

# Chrome's times: microseconds since 1601-01-01. 13_400_000_000_000_000 is 18 August 2025.
BASE = 13_400_000_000_000_000
DAY = 86_400_000_000

with open(os.path.join(root, "Local State"), "w", encoding="utf-8") as f:
    json.dump({"profile": {"info_cache": {"Default": {"avatar_icon": "x", "name": "Person 1"},
                                          "Profile 1": {"name": "Work \"desk\""}}}}, f)

bookmarks = {
    "checksum": "0", "version": 1,
    "roots": {
        "bookmark_bar": {"type": "folder", "name": "Bookmarks bar", "children": [
            {"type": "url", "name": "Kerala Tourism", "url": "https://www.keralatourism.org/", "date_added": str(BASE)},
            {"type": "folder", "name": "News", "children": [
                {"type": "url", "name": "BBC", "url": "https://www.bbc.co.uk/news", "date_added": str(BASE)},
                {"type": "url", "name": "Bad", "url": "javascript:alert(1)", "date_added": str(BASE)}]}]},
        "other": {"type": "folder", "name": "Other bookmarks", "children": [
            {"type": "url", "name": "Wikipedia", "url": "https://en.wikipedia.org/", "date_added": str(BASE)}]},
        "synced": {"type": "folder", "name": "Mobile bookmarks", "children": [
            {"type": "url", "name": "Phone page", "url": "https://m.example.com/", "date_added": str(BASE)}]}}}
with open(os.path.join(default, "Bookmarks"), "w", encoding="utf-8") as f:
    json.dump(bookmarks, f)
with open(os.path.join(work, "Bookmarks"), "w", encoding="utf-8") as f:
    json.dump({"roots": {"bookmark_bar": {"type": "folder", "children": []}, "other": {"type": "folder", "children": []}}}, f)

path = os.path.join(default, "History")
if os.path.exists(path):
    os.remove(path)
db = sqlite3.connect(path)
db.execute("PRAGMA page_size=4096")
db.execute("CREATE TABLE meta(key LONGVARCHAR NOT NULL UNIQUE PRIMARY KEY, value LONGVARCHAR)")
db.execute("CREATE TABLE urls(id INTEGER PRIMARY KEY AUTOINCREMENT,url LONGVARCHAR,title LONGVARCHAR,"
           "visit_count INTEGER DEFAULT 0 NOT NULL,typed_count INTEGER DEFAULT 0 NOT NULL,"
           "last_visit_time INTEGER NOT NULL,hidden INTEGER DEFAULT 0 NOT NULL)")
rows = [(f"https://site{i % 40}.example.com/page/{i}", f"Page number {i} – café ✓", i % 7 + 1, 0, BASE + i * 1_000_000, 0) for i in range(3000)]
rows.append(("https://long.example.com/", "Long " + "x" * 9000, 3, 0, BASE + 5 * DAY, 0))
rows.append(("https://hidden.example.com/", "Hidden", 1, 0, BASE, 1))
rows.append(("chrome://settings/", "Settings", 1, 0, BASE, 0))
db.executemany("INSERT INTO urls(url,title,visit_count,typed_count,last_visit_time,hidden) VALUES(?,?,?,?,?,?)", rows)
db.commit()
db.execute("VACUUM")
db.close()
print("made", root, os.path.getsize(path), "bytes")
