# Design: Master Library, Shared Catalog & Smart Wheels

**Status:** Proposed (direction approved by owner 2026-10-11; simplified to an edition-only catalog the same day) · **Issues:** CORE-16 / #248 (catalog + library entries), CORE-10 / #207 (wheels) · **Related:** LIBRARY-2 #181, LIBRARY-3 #186, LIBRARY-4 #201, LIBRARY-5 #211, LIBRARY-6 #223, LIBRARY-7 #224, LOOKUP-2 #202

## Problem

Today a `books` row is three things at once: public book data (title, author, ISBN, cover), the user's personal record (type, deleted state), and the wheel entry. Managing titles is a chore because of this:

- The same book in two formats needs two unrelated rows.
- Metadata fixes don't propagate. Every user re-looks-up and re-fetches the same covers.
- There's only one wheel, and keeping it current means deleting finished books (which loses history).

## Goal

**Add a book once; it can be on any number of wheels; wheels keep themselves current using rules each user controls.**

## Model

```
┌──────────── shared, instance-wide (public bibliographic data only) ────────────┐
│ catalog_editions  id · isbn13 (unique) · title · author · page_count ·          │
│                   cover_id · provider · ol_work_key (stored, unused for now)    │
│ catalog_covers    id · bytes (bytea) · content_type · source_url · fetched_at   │
└────────────────────────────────────▲────────────────────────────────────────────┘
                                     │ edition_id (optional)
┌──────────── per user (the master list) ─┴───────────────────────────────────────┐
│ library_entries   id · user_id · title · author · cover (display; seeded from   │
│                   first edition, user-overridable) · status · notes ·           │
│                   added_at · removed_at (soft, restorable)                      │
│ entry_formats     entry_id · book_type_id · edition_id? · added_by_scanner      │
│ entry_tags        entry_id · tag_id                                             │
│ reading_log       entry_id · started_at · finished_at · outcome (finished|dnf)  │
└────────────────────────────────────▲────────────────────────────────────────────┘
                                     │ entry_id
┌──────────── per user, per wheel ───┴────────────────────────────────────────────┐
│ wheels       id · user_id · name · kind (manual|smart|custom) · rules · settings│
│ wheel_items  wheel_id · entry_id · pinned · excluded · weight · added_at        │
│ spin_selections  … + wheel_id, entry_id                                         │
└─────────────────────────────────────────────────────────────────────────────────┘
```

There is deliberately **no "work" table**. Grouping editions into one book is the user's decision (see below). The Open Library work key is stored on editions, so a work level could be added later without re-fetching anything.

## Owner decisions (2026-10-11)

| # | Decision | Consequence |
|---|---|---|
| 1 | A book can be on **any number of wheels** at once | `wheel_items` is many-to-many over library entries |
| 2 | Smart-wheel behaviour is **user-dependent** | Rules are stored per wheel, per user. Nothing is global or hard-wired. |
| 3 | A book that drops off a wheel can be **re-added at any time** | Status can go back to To Read ("Read again"). Pinning overrides rules. Excluding hides a book without changing its status. |
| 4 | **Deleting is user-scoped only** | Removing sets `library_entries.removed_at` for that user. Catalog rows are never deleted by users. |
| 5 | Multiple versions/editions → **one entry, many formats** | `entry_formats` child rows, **grouped by user confirmation**, not by automatic matching |
| 6 | Catalog edits are **shared via admin corrections**; users get **personal overrides** | Admin edits catalog editions. The display fields on the entry are the user's own. |
| 7 | A spin never changes status automatically (#186) | Rules react only to explicit status changes |
| 8 | **Keep it simple:** edition-only catalog, no work layer | No matching algorithm, no auto-merge, no admin merge tool |

## Grouping formats (user-driven)

When the user adds, scans or looks up a book:

1. Look up or upsert the **edition** by ISBN in the shared catalog (cached, LOOKUP-2).
2. Compare its normalized title + primary author with the user's **own** library entries (including removed ones).
3. Then:
   - **No match:** create a new entry with one format.
   - **Match on an active entry:** ask *"Is this another format of **X**?"* **Yes** adds a format to X. **No** creates a new entry.
   - **Match on a removed entry:** offer **Restore X** (and optionally add this format).
   - **Same ISBN already in the library:** it's a plain duplicate; show "Already in your library" (#201).
4. From the edit dialog the user can **move** a format to another entry, or **split** it into a new entry. Mistakes are always fixable by the user.

Matching only ever compares against the user's own library. It never merges anything in the shared catalog.

## Duplicate prevention

The approach has three layers: hard rules where a match is certain, prompts where it is only probable, and cleanup for anything that slips through.

**1. Hard rules (database-enforced)**

| Rule | Prevents |
|---|---|
| Unique `isbn13` on `catalog_editions`; normalize (strip hyphens, ISBN-10 → 13, checksum) and upsert with `ON CONFLICT` | The same edition twice in the shared catalog, including concurrent scans |
| Unique (user, edition) across `entry_formats`, **including removed entries** | The same ISBN twice in a library. Re-adding restores instead of inserting. |
| Unique (wheel, entry) on `wheel_items` | A book twice on one wheel |
| ISBN-less books never create catalog rows | Free-text near-duplicates in the shared catalog |

**2. Prompts (user decides)**

- `library_entries.match_key` is computed at write time and indexed: lowercase, accents stripped, punctuation stripped, leading "the/a/an" stripped, subtitle after ":" dropped, series tags like "(Book 1)" dropped, then `|` and the author surname. Example: `hobbit|tolkien`.
- A collision triggers the grouping prompts above.
- A "No, these are different" answer is stored in `entry_distinct_pairs` so the app doesn't ask again.
- Later: `pg_trgm` similarity for near-misses, suggestions only (LIBRARY-11).

**3. One entry point**

Manual add, scan, Book Search, import and Goodreads CSV all call one `ResolveCandidate()`, which returns **Exact / Removed / Likely / New**. Batch flows present the outcomes as a review list.

**4. Cleanup**

A "Possible duplicates" view (same match key) with **Merge**: keep one entry, move the other's formats, wheel memberships, tags, spin history and reading log onto it, and soft-remove the other. It's per user and undoable, and never touches the shared catalog.

Uniqueness deliberately counts removed entries. Exempting them would let a re-add create a fresh duplicate and orphan its history. Counting them, then restoring, gives both "no duplicates" and "re-add any time".

## Wheel membership

A library entry **E** is on wheel **W** when:

```
W.kind = manual :  E ∈ wheel_items(W) and not excluded
W.kind = smart  :  (rules(W) match E  or  wheel_items(W, E).pinned)
                   and not wheel_items(W, E).excluded
always          :  E.removed_at is null
```

- **Rules** (v1): status ∈ {…}, format ∈ {…}, has tag / lacks tag, page count ≤ / ≥, added before / after. Rules combine with AND, plus OR within a single field.
- **Default wheel on migration:** smart, "My to-read list", rule `status = to_read`. That's exactly today's behaviour.
- **"Finished drops off"** isn't special-cased. It only happens because the user's rule says `status = to_read`. A user who wants finished books to stay edits the rule or pins the book.

## Key flows

| Flow | Behaviour |
|---|---|
| Scan or look up ISBN | Upsert the edition in the catalog, then follow the grouping steps above |
| Add to wheels | From the entry: checkboxes for manual wheels, plus smart wheels shown read-only with a "pin" toggle |
| Finish a book | Status → finished and a reading_log row is written. It leaves smart wheels whose rule needs to_read. |
| Read again | Status → to_read (the log is kept). It rejoins matching smart wheels immediately. |
| Remove from wheel | Manual wheel: delete the wheel_item. Smart wheel: set `excluded`. |
| Remove from library | Soft-remove the entry (user only). Undo or restore any time (LIBRARY-2). It disappears from all of that user's wheels. |
| Fix metadata | Admin: edit the catalog edition (everyone benefits). User: edit the entry's display fields (only them). |
| Import / Goodreads CSV | Upsert editions, then apply the grouping rules. Batch mode shows the "another format?" decisions as a review list. |

## Privacy

The catalog holds only public bibliographic data. Whether an edition exists in the catalog reveals nothing about *who* has it. Entries, formats, status, tags, notes, wheels and history are per user and never exposed across users. Sharing (CORE-11) is a future, explicit opt-in.

## Migration plan (staged, non-breaking)

1. **Ship library UX on today's schema:** search/sort (#180), trash/restore (#181), duplicate prompts (#201), bulk (#240).
2. **Catalog backfill:** create `catalog_editions` from existing books that have ISBNs (idempotent, with a report).
3. **Library entries:** one entry per existing book row (**no automatic merging**), each with one format. Repoint `spin_selections`. Offer an optional one-time "possible duplicates" review so users can combine entries. `/api/books` remains a compatibility view for one release.
4. **Wheels:** create the default smart wheel per user, then ship the switcher, manual wheels and the rule builder.
5. **Cleanup:** drop the legacy `books` columns after one release.

## Open questions

- Should **tags** be per user only (current plan), or can households share tag vocabularies? This can wait for CORE-11.
- **Reading log granularity:** just start/finish dates, or also page progress? v1 recommendation: dates only.
