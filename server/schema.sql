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
);
CREATE TABLE sessions (
    token_hash TEXT PRIMARY KEY,
    account_id INTEGER NOT NULL REFERENCES accounts(id),
    expires INTEGER NOT NULL
);
