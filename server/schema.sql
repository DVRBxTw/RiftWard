-- Documentation; service.py creates this schema automatically.
CREATE TABLE accounts (
    id INTEGER PRIMARY KEY,
    username TEXT UNIQUE COLLATE NOCASE NOT NULL,
    password_hash BLOB NOT NULL,
    salt BLOB NOT NULL,
    created INTEGER NOT NULL
);
CREATE TABLE progress (
    account_id INTEGER PRIMARY KEY REFERENCES accounts(id),
    unlocked INTEGER NOT NULL DEFAULT 0,
    tutorial_complete INTEGER NOT NULL DEFAULT 0
    -- Columns added by migrations in service.py: gold, loadout, stats, xp, keys, weapon_keys, talents, gear, pity,
    -- cleared (JSON list: highest difficulty cleared per region, -1 = none),
    -- daily (JSON: {day, tasks, progress, claimed, bonus} for today's daily missions).
);
CREATE TABLE sessions (
    token_hash TEXT PRIMARY KEY,
    account_id INTEGER NOT NULL REFERENCES accounts(id),
    expires INTEGER NOT NULL
);
CREATE TABLE mail (           -- system mail; reward columns are attachments collected by the player
    id INTEGER PRIMARY KEY, account_id INTEGER NOT NULL, title TEXT NOT NULL, body TEXT NOT NULL,
    gold INTEGER, keys INTEGER, weapon_keys INTEGER, created INTEGER, expires INTEGER, read INTEGER, claimed INTEGER, deleted INTEGER
);
CREATE TABLE friendships (    -- status: 'pending' (requester asked addressee) or 'accepted'
    requester INTEGER NOT NULL, addressee INTEGER NOT NULL, status TEXT NOT NULL, created INTEGER NOT NULL,
    PRIMARY KEY (requester, addressee)
);
CREATE TABLE audit (          -- anti-cheat log: rejected = 1 when the mission report was refused
    id INTEGER PRIMARY KEY, account_id INTEGER NOT NULL, created INTEGER NOT NULL, reason TEXT NOT NULL, detail TEXT NOT NULL, rejected INTEGER
);
