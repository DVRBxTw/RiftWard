using System.Collections.Generic;
using UnityEngine;

namespace RiftWard.Sim
{
    /// Per-tick player input. Movement x = strafe right, y = forward. Angles in radians.
    public struct PlayerInput
    {
        public int Seq;
        public Vector2 Move;
        public float Yaw, Pitch;
        public Vector3 Aim;
        public bool Fire, Dash, Jump, Descend, Reload, Skill, Interact;
        public int Slot;
    }

    /// Account-side data a player brings into a mission.
    public sealed class Loadout
    {
        public List<string> Owned = new List<string> { "pistol" };
        public List<string> Equipped = new List<string> { "pistol" };
        public Dictionary<string, Dictionary<string, int>> Levels = new Dictionary<string, Dictionary<string, int>>();
        /// Rarity per owned weapon; missing entries count as 普通 (1).
        public Dictionary<string, int> Rarity = new Dictionary<string, int>();
        /// Every copy drawn per weapon (rarities) and which copy is in use.
        public Dictionary<string, List<int>> Copies = new Dictionary<string, List<int>>();
        public Dictionary<string, int> Active = new Dictionary<string, int>();

        public int RarityOf(string weapon) => Rarity.TryGetValue(weapon, out var r) ? Mathf.Clamp(r, 0, Defs.WeaponRarityMult.Length - 1) : 1;

        public int Level(string weapon, string stat) =>
            Levels.TryGetValue(weapon, out var stats) && stats.TryGetValue(stat, out var v) ? Mathf.Clamp(v, 0, Defs.MaxUpgradeLevel) : 0;

        /// Keeps only owned, unique weapons; at most two equipped; never leaves the player unarmed.
        public void Normalize()
        {
            var owned = new List<string>();
            foreach (var w in Owned) if (Defs.Weapons.ContainsKey(w) && !owned.Contains(w)) owned.Add(w);
            if (owned.Count == 0) owned.Add("rifle");
            Owned = owned;
            var kit = new List<string>();
            foreach (var w in Equipped) if (owned.Contains(w) && !kit.Contains(w) && kit.Count < 2) kit.Add(w);
            if (kit.Count == 0) kit.AddRange(owned.GetRange(0, Mathf.Min(2, owned.Count)));
            Equipped = kit;
        }
    }

    public sealed class AdminSettings
    {
        public float Damage = 1f, Speed = 1f;
        public bool God, InfiniteAmmo, Fly, AutoAim;

        public AdminSettings Clamped() => new AdminSettings
        {
            Damage = Mathf.Clamp(Damage, 1f, 20f), Speed = Mathf.Clamp(Speed, 0.5f, 3f),
            God = God, InfiniteAmmo = InfiniteAmmo, Fly = Fly, AutoAim = AutoAim,
        };
    }

    public sealed class Actor
    {
        public int Id;
        public Vector3 Position;
        public float Hp = 100f, MaxHp = 100f;
        public float Yaw, Pitch, VerticalVelocity;
        /// 傳奇+ weapon perks: guaranteed crits left (AR-27) and haste time left (P-9).
        public int PerkCrits;
        public float PerkHaste;
        public float FireCooldown, DashCooldown, DashLeft;
        public Vector3 DashDirection;
        public int Ack;

        // Weapon state
        public Loadout Loadout = new Loadout();
        public int Slot;
        public int Ammo, Mag;
        public float Reload, ReloadTime;
        public float Damage, FireInterval, Spread, HeadMultiplier, Zoom;
        public int Pellets = 1;
        public Dictionary<string, int> AmmoByWeapon = new Dictionary<string, int>();

        // Mission buffs
        public float Speed = Defs.BaseSpeed, DamageMult = 1f, RateMult = 1f, ReloadMult = 1f, SkillMult = 1f;
        public int MagBonus;
        public float Regen, Critical, Lifesteal, Explode;
        public float SkillCooldown, Revive;

        // Companion gadgets (mission upgrades)
        public string Name = "";
        public int Drones;
        public float DroneDamageMult = 1f, DroneRateMult = 1f, DroneTimer;
        public bool Plasma;
        public float PlasmaDamageMult = 1f, PlasmaRateMult = 1f, PlasmaTimer;
        public bool DroneEvolved, PlasmaEvolved;
        public int OrbitLevel, MineLevel, MissileLevel, LightningLevel;
        public float OrbitTimer, MineTimer, MissileTimer, LightningTimer;
        public float Magnet = Defs.BaseMagnet;
        /// Fraction of incoming damage ignored (mission buff, capped at Defs.MaxArmor).
        public float Armor;

        // Admin
        public AdminSettings Admin = new AdminSettings();
        public bool IsAdmin;

        // Per-mission combat record (sent to the account service as lifetime stats).
        public int StatKills, StatHeadshots, StatShots, StatHits;
        /// Achievement records: times downed, teammates revived, bosses and elites the team defeated while this actor was present.
        public int StatDowns, StatRevives, StatBossKills, StatEliteKills;
        public bool WasAlive = true;
        /// Shots whose aim point disagreed with the player's view and were corrected (anti-cheat record).
        public int AimFlags;

        public bool Alive => Hp > 0f;
        public string WeaponId => Loadout.Equipped[Mathf.Clamp(Slot, 0, Loadout.Equipped.Count - 1)];
    }

    public sealed class Enemy
    {
        public int Id;
        public Vector3 Position;
        public float Hp, MaxHp, Speed, Damage, Yaw;
        public float MeleeCooldown = 0.6f, ShotCooldown;
        public bool Boss, Shoots = true, RangedOnly;
        public int Kind;
        public float Scale = 1f, Keep = 1.4f;
        public int Xp;
        public int Phase = 1;
        public bool Elite;
        public int Wave;
        /// 0 idle, 1 sniper aiming (telegraph), 2 boss enraged.
        public int State;
        public float RingCooldown = 2f;
        /// Imperial Guard: time to the next charge and the time left in the current wind-up / rush.
        public float ChargeCooldown = 3f, ChargeTime;
        /// Emperor: blink and summon timers and the rotating angle of his spiral barrage.
        public float BlinkCooldown = 6f, SummonCooldown = 9f, SpiralAngle, SpiralCooldown;

        /// Direction the enemy faces (towards its target).
        public Vector3 Facing => new Vector3(Mathf.Sin(Yaw), 0f, Mathf.Cos(Yaw));
    }

    public enum BulletKind { Enemy = 0, Sniper = 1, Grenade = 2, Mine = 3, Missile = 4 }

    public sealed class Bullet
    {
        public int Id;
        public Vector3 Position, Velocity;
        public float Damage, Life = Defs.BulletLife;
        public BulletKind Kind;
        public int Owner = -1;
        public int Target = -1;
    }

    public enum PickupKind { Health = 0, Ammo = 1, Xp = 2, Chest = 3 }

    public sealed class Pickup
    {
        public int Id;
        public Vector3 Position;
        public PickupKind Kind;
        public float Life = 20f;
        public int Value;
    }

    /// Things the client turns into effects and sounds; drained every tick.
    public struct ShotEvent
    {
        public int Shooter;
        public Vector3 Origin, End;
        public bool Hit, Head, Killed;
        public float Dealt;
    }

    public struct ExplosionEvent
    {
        public Vector3 Position;
        public float Radius;
        public Color32 Color;
    }
}
