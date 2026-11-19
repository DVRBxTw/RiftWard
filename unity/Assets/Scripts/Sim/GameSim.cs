using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = System.Random;

namespace RiftWard.Sim
{
    public enum MissionState { Idle, Playing, Victory, Defeat }

    /// Authoritative mission simulation. Runs on the server (or offline host); clients only render its snapshots.
    public sealed class GameSim
    {
        public readonly Dictionary<int, Actor> Actors = new Dictionary<int, Actor>();
        public readonly Dictionary<int, PlayerInput> Inputs = new Dictionary<int, PlayerInput>();
        public readonly Dictionary<int, Enemy> Enemies = new Dictionary<int, Enemy>();
        public readonly Dictionary<int, Bullet> Bullets = new Dictionary<int, Bullet>();
        public readonly Dictionary<int, Pickup> Pickups = new Dictionary<int, Pickup>();

        public readonly List<ShotEvent> Shots = new List<ShotEvent>();
        public readonly List<ExplosionEvent> Explosions = new List<ExplosionEvent>();
        public readonly List<string> Announcements = new List<string>();

        public MissionState State = MissionState.Idle;
        public int Region;
        /// Difficulty tier (index into Defs.Difficulties): scales enemy health, damage, count and the gold reward.
        public int Difficulty;
        public int Wave;
        public float WaveDelay = 1.8f;
        public float Elapsed;
        public int Kills;
        public bool EnemyVariety = true;
        /// 無盡模式: waves never stop; enemies keep scaling past wave 20 and a boss comes every 10 waves.
        public bool Endless;

        public int Level = 1, Xp, XpNeed = Defs.XpForLevel(1), PendingLevels;
        public bool BuffPending;
        /// Super chest picks still queued, and which pick of the current chest is on screen (0 = a normal level-up).
        public int SuperRounds, SuperPick;
        public float BuffWait;
        /// Rolled buff codes per player awaiting a choice.
        public readonly Dictionary<int, int[]> BuffOffers = new Dictionary<int, int[]>();

        /// Letter grades of cleared waves, in order.
        public readonly List<string> WaveGrades = new List<string>();
        readonly List<float> waveScores = new List<float>();
        float waveStart, waveDamage;
        bool waveGraded = true;

        int nextEnemy, nextBullet, nextPickup;
        readonly Random rng;

        public GameSim(int seed = 0) { rng = seed == 0 ? new Random() : new Random(seed); }

        float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        float Rand01() => (float)rng.NextDouble();

        // ------------------------------------------------------------ setup

        public Actor AddActor(int id, Loadout loadout, bool isAdmin = false)
        {
            var actor = new Actor { Id = id, Loadout = loadout ?? new Loadout(), IsAdmin = isAdmin };
            actor.Loadout.Normalize();
            Actors[id] = actor;
            ResetActor(actor);
            return actor;
        }

        public void RemoveActor(int id)
        {
            Actors.Remove(id);
            Inputs.Remove(id);
            BuffOffers.Remove(id);
            if (BuffPending && BuffOffers.Count == 0) ResolveBuffs();
        }

        void ResetActor(Actor a)
        {
            int index = a.Id % 4;
            a.Position = new Vector3(index * 2 - 3, 0, -10);
            a.Hp = a.MaxHp = 100f;
            a.Yaw = 0; a.VerticalVelocity = 0; a.FireCooldown = 0; a.DashCooldown = 0; a.DashLeft = 0;
            a.Speed = Defs.BaseSpeed; a.DamageMult = 1; a.RateMult = 1; a.ReloadMult = 1; a.SkillMult = 1;
            a.MagBonus = 0; a.Regen = 0; a.Armor = 0; a.GearDefense = 0; a.Critical = 0; a.Lifesteal = 0; a.Explode = 0; a.HealBudget = 0; System.Array.Clear(a.BuffStacks, 0, a.BuffStacks.Length);
            a.SkillCooldown = 0; a.Revive = 0; a.Slot = 0; a.Reload = 0;
            a.StatKills = 0; a.StatHeadshots = 0; a.StatShots = 0; a.StatHits = 0; a.StatDamage = 0;
            for (int i = 0; i < a.MeleeHitTimes.Length; i++) a.MeleeHitTimes[i] = -9f;
            a.StatDowns = 0; a.StatRevives = 0; a.StatBossKills = 0; a.StatEliteKills = 0; a.WasAlive = true; a.AimFlags = 0;
            a.AmmoByWeapon.Clear();
            RefreshWeapon(a);
            a.Ammo = a.Mag;
            ApplyAdmin(a);
            // Talents and gear outlive the reset: a player added before the mission starts keeps them.
            ApplyBonusStats(a);
        }

        public void StartMission(int region, int difficulty = 0)
        {
            Region = Mathf.Clamp(region, 0, Defs.Regions.Length - 1);
            Difficulty = Mathf.Clamp(difficulty, 0, Defs.Difficulties.Length - 1);
            Enemies.Clear(); Bullets.Clear(); Pickups.Clear(); Inputs.Clear(); BuffOffers.Clear();
            Wave = 0; WaveDelay = 1.8f; Elapsed = 0; Kills = 0;
            Level = 1; Xp = 0; XpNeed = Defs.XpForLevel(1); PendingLevels = 0; BuffPending = false; SuperRounds = 0; SuperPick = 0;
            WaveGrades.Clear(); waveScores.Clear(); waveGraded = true; waveDamage = 0;
            foreach (var a in Actors.Values) ResetActor(a);
            State = MissionState.Playing;
        }

        // ------------------------------------------------------------ weapons

        /// Permanent account bonuses (talents + equipped gear) applied when a player joins a mission.
        public void ApplyProfileBonus(int id, float hp, float damagePct, float speedPct, float reloadPct, float critPct, float hpPct = 0f, float defPct = 0f)
        {
            if (!Actors.TryGetValue(id, out var a)) return;
            hp = Mathf.Clamp(hp, 0, 400); damagePct = Mathf.Clamp(damagePct, 0, 200); speedPct = Mathf.Clamp(speedPct, 0, 60);
            reloadPct = Mathf.Clamp(reloadPct, 0, 60); critPct = Mathf.Clamp(critPct, 0, 60); hpPct = Mathf.Clamp(hpPct, 0, 60);
            defPct = Mathf.Clamp(defPct, 0, 40);
            a.ProfileBonus = new[] { hp, damagePct, speedPct, reloadPct, critPct, hpPct, defPct };
            ApplyBonusStats(a);
        }

        /// Adds the stored account bonus on top of the actor's current (freshly reset) stats.
        void ApplyBonusStats(Actor a)
        {
            if (a.ProfileBonus == null) return;
            float hp = a.ProfileBonus[0], damagePct = a.ProfileBonus[1], speedPct = a.ProfileBonus[2], reloadPct = a.ProfileBonus[3], critPct = a.ProfileBonus[4];
            a.HpPct = a.ProfileBonus.Length > 5 ? a.ProfileBonus[5] / 100f : 0f;
            a.MaxHp = (a.MaxHp + hp) * (1f + a.HpPct);
            // Gear defense: its own layer on top of the in-mission armour, so it still counts once that is capped.
            a.GearDefense = a.ProfileBonus.Length > 6 ? Mathf.Clamp(a.ProfileBonus[6] / 100f, 0f, 0.4f) : 0f;
            a.Hp = a.MaxHp;
            a.DamageMult *= 1f + damagePct / 100f;
            a.Speed *= 1f + speedPct / 100f;
            a.ReloadMult = Mathf.Max(0.4f, a.ReloadMult * (1f - reloadPct / 100f));
            a.Critical = Mathf.Min(Defs.MaxCritical, a.Critical + critPct / 100f);
            RefreshWeapon(a);
        }

        public void RefreshWeapon(Actor a)
        {
            string id = a.WeaponId;
            var st = Defs.Weapons[id].StatsAt(a.Loadout.Level(id, "damage"), a.Loadout.Level(id, "mag"), a.Loadout.Level(id, "reload"), a.Loadout.Level(id, "over"));
            float adminDamage = a.IsAdmin ? a.Admin.Damage : 1f;
            a.Damage = st.Damage * a.DamageMult * adminDamage * Defs.WeaponRarityMult[a.Loadout.RarityOf(id)];
            a.FireInterval = Mathf.Max(0.045f, st.Interval * a.RateMult);
            a.Mag = st.Mag + a.MagBonus;
            a.ReloadTime = Mathf.Max(0.35f, st.Reload * a.ReloadMult);
            a.Pellets = st.Pellets;
            a.Pierce = !Defs.Weapons[id].Melee && a.Loadout.RarityOf(id) >= Defs.PierceMinRarity && a.Loadout.Level(id, "pierce") > 0 ? Defs.PierceExtra(id) : 0;
            a.PierceFalloff = Defs.PierceFalloff(id);
            a.Spread = st.Spread;
            a.HeadMultiplier = st.HeadMultiplier;
            a.Zoom = st.Zoom;
            a.Range = st.Range;
            a.Ammo = Defs.Weapons[id].Melee ? a.Mag : Mathf.Min(a.Ammo, a.Mag);   // a blade never runs dry
        }

        public void SwitchWeapon(Actor a, int slot)
        {
            if (slot == a.Slot || slot < 0 || slot >= a.Loadout.Equipped.Count) return;
            a.AmmoByWeapon[a.WeaponId] = a.Ammo;
            a.Slot = slot;
            RefreshWeapon(a);
            a.Ammo = a.AmmoByWeapon.TryGetValue(a.WeaponId, out var saved) ? Mathf.Min(saved, a.Mag) : a.Mag;
            a.Reload = 0;
            a.FireCooldown = Mathf.Max(a.FireCooldown, 0.25f);
        }

        void StartReload(Actor a)
        {
            if (a.Reload > 0 || a.Ammo >= a.Mag) return;
            a.Reload = a.ReloadTime;
        }

        // ------------------------------------------------------------ admin

        public void SetAdmin(int id, AdminSettings settings)
        {
            if (!Actors.TryGetValue(id, out var a) || !a.IsAdmin) return;
            a.Admin = settings.Clamped();
            ApplyAdmin(a);
        }

        void ApplyAdmin(Actor a)
        {
            RefreshWeapon(a);
        }

        public static float EffectiveSpeed(Actor a) => a.Speed * (a.IsAdmin ? a.Admin.Speed : 1f) * (a.Blocking ? Defs.BlockSpeed : 1f);
        bool Flying(Actor a) => a.IsAdmin && a.Admin.Fly;

        // ------------------------------------------------------------ movement (shared with client prediction)

        public static Vector3 LookDirection(float yaw, float pitch) =>
            new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));

        public void MoveActor(Actor a, PlayerInput input, float dt) => MoveActor(a, input, dt, EffectiveSpeed(a), Flying(a));

        public static void MoveActor(Actor a, PlayerInput input, float dt, float speed, bool fly)
        {
            a.FireCooldown = Mathf.Max(0, a.FireCooldown - dt);
            a.DashCooldown = Mathf.Max(0, a.DashCooldown - dt);
            if (!a.Alive) return;
            if (input.Block && !a.Melee) speed *= Defs.AimSpeed;   // aiming down sights: a steady walk
            Vector2 mv = Vector2.ClampMagnitude(input.Move, 1f);
            float yaw = float.IsFinite(input.Yaw) ? input.Yaw : 0f;
            var forward = new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw));
            var right = new Vector3(Mathf.Cos(yaw), 0, -Mathf.Sin(yaw));
            Vector3 motion = right * mv.x + forward * mv.y;
            if (input.Dash && a.DashCooldown <= 0 && mv.sqrMagnitude > 0)
            {
                a.DashCooldown = Defs.DashCooldown;
                a.DashLeft = Defs.DashDuration;
                a.DashDirection = motion.normalized;
            }
            if (a.DashLeft > 0)
            {
                float next = Mathf.Max(0, a.DashLeft - dt);
                float start = 1f - a.DashLeft / Defs.DashDuration, finish = 1f - next / Defs.DashDuration;
                a.Position += a.DashDirection * Defs.DashDistance * (Mathf.SmoothStep(0, 1, finish) - Mathf.SmoothStep(0, 1, start));
                a.DashLeft = next;
            }
            if (fly)
            {
                float lift = (input.Jump ? 1f : 0f) - (input.Descend ? 1f : 0f);
                a.VerticalVelocity = 0;
                a.Position += motion * speed * dt;
                a.Position.y = Mathf.Clamp(a.Position.y + lift * Defs.FlySpeed * dt, 0, Defs.FlyCeiling);
            }
            else
            {
                if (input.Jump && a.Position.y <= 0.01f) a.VerticalVelocity = Defs.JumpVelocity;
                a.VerticalVelocity -= Defs.Gravity * dt;
                a.Position += motion * speed * dt;
                a.Position.y = Mathf.Max(0, a.Position.y + a.VerticalVelocity * dt);
            }
            a.Position.x = Mathf.Clamp(a.Position.x, -Defs.ArenaHalf, Defs.ArenaHalf);
            a.Position.z = Mathf.Clamp(a.Position.z, -Defs.ArenaHalf, Defs.ArenaHalf);
            a.Position = PushOutOfCover(a.Position, 0.4f);
            a.Yaw = yaw;
        }

        public static Vector3 PushOutOfCover(Vector3 p, float radius)
        {
            foreach (var c in Defs.Covers)
            {
                if (p.y >= c[4]) continue;
                float dx = p.x - c[0], dz = p.z - c[1];
                float ox = c[2] + radius - Mathf.Abs(dx), oz = c[3] + radius - Mathf.Abs(dz);
                if (ox > 0 && oz > 0)
                {
                    if (ox < oz) p.x += ox * (dx >= 0 ? 1 : -1);
                    else p.z += oz * (dz >= 0 ? 1 : -1);
                }
            }
            return p;
        }

        /// Distance along a ray to the first cover wall or the ground, capped at maxDistance (slab test per box).
        public static float RayBlock(Vector3 origin, Vector3 dir, float maxDistance)
        {
            float best = maxDistance;
            if (dir.y < -1e-4f) best = Mathf.Min(best, origin.y / -dir.y);
            foreach (var c in Defs.Covers)
            {
                float tMin = 0f, tMax = best;
                if (!Slab(origin.x, dir.x, c[0] - c[2], c[0] + c[2], ref tMin, ref tMax)) continue;
                if (!Slab(origin.y, dir.y, 0f, c[4], ref tMin, ref tMax)) continue;
                if (!Slab(origin.z, dir.z, c[1] - c[3], c[1] + c[3], ref tMin, ref tMax)) continue;
                best = tMin;
            }
            return best;

            static bool Slab(float o, float d, float lo, float hi, ref float tMin, ref float tMax)
            {
                if (Mathf.Abs(d) < 1e-6f) return o >= lo && o <= hi;
                float t1 = (lo - o) / d, t2 = (hi - o) / d;
                if (t1 > t2) (t1, t2) = (t2, t1);
                tMin = Mathf.Max(tMin, t1); tMax = Mathf.Min(tMax, t2);
                return tMin <= tMax;
            }
        }

        public static bool InCover(Vector3 p)
        {
            foreach (var c in Defs.Covers)
                if (p.y < c[4] && Mathf.Abs(p.x - c[0]) < c[2] && Mathf.Abs(p.z - c[1]) < c[3]) return true;
            return false;
        }

        // ------------------------------------------------------------ main tick

        public void Tick(float dt)
        {
            if (State != MissionState.Playing) return;
            if (BuffPending)
            {
                BuffWait += dt;
                if (BuffWait > Defs.BuffTimeout)
                {
                    foreach (var id in BuffOffers.Keys.ToList()) ApplyBuff(id, BuffOffers[id][0]);
                    ResolveBuffs();
                }
                return;
            }
            if (PendingLevels > 0)
            {
                PendingLevels--;
                SuperPick = 0;
                BeginBuffs();
                return;
            }
            if (SuperRounds > 0)
            {
                SuperPick = Defs.SuperChestPicks - SuperRounds + 1;
                SuperRounds--;
                BeginBuffs();
                return;
            }
            Elapsed += dt;
            var hpBefore = new Dictionary<int, float>();
            foreach (var a in Actors.Values) hpBefore[a.Id] = a.Hp;
            var armorBase = new Dictionary<int, float>(hpBefore);
            foreach (var a in Actors.Values)
            {
                Inputs.TryGetValue(a.Id, out var input);
                a.Pitch = input.Pitch;
                a.PerkHaste = Mathf.Max(0, a.PerkHaste - dt);
                MoveActor(a, input, dt);
                a.Ack = input.Seq;
                if (!a.Alive) { a.Blocking = false; a.Aiming = false; continue; }
                if (a.Melee)
                {
                    a.Blocking = input.Block && !input.Fire;
                    a.Aiming = false;
                    a.Reload = 0; a.Ammo = a.Mag;
                    if (input.Fire && a.FireCooldown <= 0)
                    {
                        a.FireCooldown = a.FireInterval / (a.PerkHaste > 0 ? 1.4f : 1f);
                        Swing(a);
                    }
                    continue;
                }
                a.Blocking = false;
                UpdateAmmo(a, input, dt);
                a.Aiming = input.Block && a.Reload <= 0;
                if (input.Fire && a.FireCooldown <= 0 && a.Reload <= 0)
                {
                    if (a.Ammo > 0)
                    {
                        // P-9 perk: faster fire for a moment after a kill.
                        a.FireCooldown = a.FireInterval / (a.PerkHaste > 0 ? 1f + PerkStrength(a, 0.4f) : 1f);
                        a.Ammo--;
                        Vector3 aim = input.Aim == Vector3.zero ? a.Position + Vector3.up + LookDirection(a.Yaw, 0) * 30f : CheckedAim(a, input);
                        Fire(a.Id, aim);
                        if (a.Ammo <= 0) StartReload(a);
                    }
                    else StartReload(a);
                }
            }
            var alive = Actors.Values.Where(a => a.Alive).ToList();
            if (alive.Count == 0 && Actors.Count > 0)
            {
                State = MissionState.Defeat;
                return;
            }
            UpdateEnemies(alive, dt);
            if (Enemies.Count == 0)
            {
                WaveDelay -= dt;
                if (WaveDelay <= 0)
                {
                    if (Wave >= Defs.WavesPerMission && !Endless)
                    {
                        State = MissionState.Victory;
                        return;
                    }
                    Wave++;
                    SpawnWave();
                    WaveDelay = 2f;
                }
            }
            UpdateEnemyFire(dt);
            UpdateGadgets(dt);
            UpdateOrdnance(dt);
            UpdateBullets(dt);
            ApplyArmor(armorBase);
            UpdatePlayers(dt);
            UpdateEnemyKinds(dt);
            UpdateGrenades(dt);
            UpdatePickups(dt);
            ApplyArmor(armorBase);
            foreach (var a in Actors.Values)
                if (hpBefore.TryGetValue(a.Id, out var before) && a.Hp < before && !(a.IsAdmin && a.Admin.God)) waveDamage += before - a.Hp;
            if (!waveGraded && Wave > 0 && Enemies.Count == 0) GradeWave();
        }

        /// Damage taken since the last check is reduced by each player's armour. Hits leave health below zero until
        /// here (overkill counts), so armour can soften a killing blow but never turns it into a hit that leaves you
        /// at a sliver forever; health is clamped at zero afterwards.
        void ApplyArmor(Dictionary<int, float> baseline)
        {
            foreach (var a in Actors.Values)
            {
                if (baseline.TryGetValue(a.Id, out var before) && (a.Armor > 0 || a.GearDefense > 0) && a.Hp < before && before > 0)
                    a.Hp = before - (before - a.Hp) * (1f - a.Armor) * (1f - a.GearDefense);
                if (a.Hp < 0) a.Hp = 0;
                baseline[a.Id] = a.Hp;
            }
        }

        /// Score a cleared wave: fast clears with little damage taken earn an S.
        void GradeWave()
        {
            waveGraded = true;
            float seconds = Elapsed - waveStart;
            float par = 12f + 1.2f * Wave;
            float score = Mathf.Clamp(100f - waveDamage * 0.6f / Mathf.Max(1, Actors.Count) - Mathf.Max(0, seconds - par) * 1.5f, 0, 100);
            string grade = Grade(score);
            waveScores.Add(score);
            WaveGrades.Add(grade);
            Announcements.Add($"第 {Wave} 波完成 · 評分 {grade}");
        }

        /// Largest angle allowed between the reported aim point (seen from the eyes) and the view direction. Third-person
        /// aims from the camera behind the shoulder, which at the 65 m aim distance is only a few degrees off.
        public const float AimTolerance = 12f;

        /// The client's aim point, unless it points somewhere the player is not looking (an aim hack sending targets
        /// directly): then the shot follows the view and the actor's AimFlags record counts it.
        Vector3 CheckedAim(Actor a, PlayerInput input)
        {
            Vector3 eye = a.Position + Vector3.up * Defs.EyeHeight;
            Vector3 look = LookDirection(input.Yaw, input.Pitch);
            Vector3 toAim = input.Aim - eye;
            if (toAim.sqrMagnitude > 0.01f && Vector3.Angle(toAim, look) <= AimTolerance) return input.Aim;
            a.AimFlags++;
            return eye + look * Defs.ShotRange;
        }

        public static string Grade(float score) => score >= 85 ? "S" : score >= 65 ? "A" : score >= 40 ? "B" : "C";

        public string MissionGrade() => waveScores.Count == 0 ? "C" : Grade(waveScores.Average() - (State == MissionState.Defeat ? 25 : 0));

        void UpdateAmmo(Actor a, PlayerInput input, float dt)
        {
            if (a.Reload > 0)
            {
                a.Reload = Mathf.Max(0, a.Reload - dt);
                if (a.Reload <= 0) a.Ammo = a.Mag;
            }
            else if (input.Reload) StartReload(a);
        }

        void UpdateEnemies(List<Actor> alive, float dt)
        {
            foreach (var e in Enemies.Values)
            {
                Actor target = null;
                float distance = float.MaxValue;
                foreach (var a in alive)
                {
                    float d = Vector3.Distance(e.Position, a.Position);
                    if (d < distance) { distance = d; target = a; }
                }
                if (target == null) continue;
                Vector3 toward = target.Position - e.Position;
                toward.y = 0;
                if (toward.sqrMagnitude > 0.0001f) e.Yaw = Mathf.Atan2(toward.x, toward.z);
                Vector3 dir = toward.normalized;
                float speed = e.Kind == Defs.KindGuard ? (e.State == 1 ? 0.15f : e.State == 3 ? Defs.GuardChargeSpeed : 1f) * e.Speed : e.Speed;
                if (distance > e.Keep || e.State == 3 && e.Kind == Defs.KindGuard) e.Position += dir * speed * dt;
                else if (distance < e.Keep - 2f) e.Position -= dir * e.Speed * 0.6f * dt;
                e.Position = PushOutOfCover(e.Position, e.Boss ? 1f : 0.5f);
                e.Position.x = Mathf.Clamp(e.Position.x, -Defs.ArenaHalf, Defs.ArenaHalf);
                e.Position.z = Mathf.Clamp(e.Position.z, -Defs.ArenaHalf, Defs.ArenaHalf);
                e.MeleeCooldown = Mathf.Max(0, e.MeleeCooldown - dt);
                // A crowd cannot all land at once: at most MeleeCrowd hits per player per melee interval. The rest
                // keep their swing ready and strike as a slot frees up, so a swarm is deadly but not instant.
                if (distance < (e.Boss ? 2.4f : 1.7f) && e.MeleeCooldown <= 0 && !e.RangedOnly && TakeMeleeSlot(target))
                {
                    e.MeleeCooldown = 0.85f;
                    target.Hp -= e.Damage;
                }
            }
        }

        // ------------------------------------------------------------ waves

        public float LevelScale => 1f + 0.08f * (Level - 1);

        /// Entry points on the arena edge: the middle of each side and the four corners.
        static readonly Vector3[] SpawnGates =
        {
            new Vector3(0, 0, 20.5f), new Vector3(0, 0, -20.5f), new Vector3(20.5f, 0, 0), new Vector3(-20.5f, 0, 0),
            new Vector3(17.5f, 0, 17.5f), new Vector3(-17.5f, 0, 17.5f), new Vector3(17.5f, 0, -17.5f), new Vector3(-17.5f, 0, -17.5f),
        };
        const float SpawnMinPlayerDistance = 12f;

        /// The gates farthest from every living player. A second gate must be at least 90 degrees away from the
        /// first (seen from the arena centre), so a big wave arrives as a pincer, never as a ring around the players.
        List<Vector3> PickGates(int wanted)
        {
            var players = new List<Vector3>();
            foreach (var a in Actors.Values) if (a.Alive) players.Add(a.Position);
            if (players.Count == 0) players.Add(Vector3.zero);
            float Score(Vector3 g)
            {
                float best = float.MaxValue;
                foreach (var p in players) best = Mathf.Min(best, Vector3.Distance(new Vector3(p.x, 0, p.z), g));
                return best + Rand(0f, 2f);   // a little randomness so equal choices vary between waves
            }
            var ranked = new List<(Vector3 gate, float score)>();
            foreach (var g in SpawnGates) ranked.Add((g, Score(g)));
            ranked.Sort((x, y) => y.score.CompareTo(x.score));
            var picked = new List<Vector3> { ranked[0].gate };
            if (wanted > 1)
                foreach (var (gate, _) in ranked)
                    if (Vector3.Angle(gate, picked[0]) >= 90f) { picked.Add(gate); break; }
            return picked;
        }

        /// A spot scattered around a gate, inside the arena, out of cover and never close to a player.
        Vector3 SpawnNear(Vector3 gate, float scatter)
        {
            Vector3 best = gate;
            float bestDistance = -1f;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var p = gate + new Vector3(Rand(-scatter, scatter), 0, Rand(-scatter, scatter));
                p.x = Mathf.Clamp(p.x, -21.5f, 21.5f);
                p.z = Mathf.Clamp(p.z, -21.5f, 21.5f);
                p = PushOutOfCover(p, 0.6f);
                float nearest = float.MaxValue;
                foreach (var a in Actors.Values) if (a.Alive) nearest = Mathf.Min(nearest, Vector3.Distance(a.Position, p));
                if (nearest >= SpawnMinPlayerDistance) return p;
                if (nearest > bestDistance) { bestDistance = nearest; best = p; }
            }
            return best;   // players are everywhere: use the farthest candidate found
        }

        public void SpawnWave()
        {
            int players = Mathf.Max(1, Actors.Count);
            var tier = Defs.Difficulty(Difficulty);
            int count = Mathf.Min(24 + tier.ExtraEnemies, Defs.Regions[Region].BaseEnemies + Mathf.CeilToInt(Wave / 2f) + (players - 1) * 2 + tier.ExtraEnemies);
            int elapsedWaves = Mathf.Max(0, Wave - 1);
            float region = Region;
            float partyScale = 1f + 0.6f * (players - 1);
            float levelDamage = 1f + 0.05f * (Level - 1);
            var pool = new List<int>();
            if (Wave >= 2) pool.Add(Defs.KindCharger);
            if (Wave >= 3) pool.Add(Defs.KindBomber);
            if (Wave >= 4) pool.Add(Defs.KindSniper);
            if (Wave >= 5) pool.Add(Defs.KindShield);
            if (Wave >= 6) pool.Add(Defs.KindSplitter);
            if (Wave >= 7 && Enemies.Values.Count(x => x.Kind == Defs.KindMedic) < 2) pool.Add(Defs.KindMedic);
            if (Region >= Defs.RegionsPerChapter && Wave >= 4) pool.Add(Defs.KindGuard);   // chapter 2: imperial guards
            bool hasBoss = false;
            var gates = PickGates(count > 10 ? 2 : 1);
            for (int i = 0; i < count; i++)
            {
                bool boss = (Endless ? Wave % 10 == 0 : Wave == 10 || Wave == 20) && i == 0;
                // The boss and the first half come through the farthest gate, the rest through the second (if any).
                var gate = gates[boss || i < (count + 1) / 2 ? 0 : gates.Count - 1];
                var e = new Enemy { Id = nextEnemy++, Position = SpawnNear(gate, boss ? 0.5f : 3.2f), Boss = boss, ShotCooldown = Rand(1.5f, 3.5f) };
                if (boss)
                {
                    hasBoss = true;
                    bool emperor = Wave % 20 == 0 && Region == Defs.Regions.Length - 1;
                    float baseHealth = Wave == 10 ? 3000f : emperor ? 9000f : 7000f;
                    if (Wave > 20) baseHealth *= 1f + 0.4f * (Wave / 10 - 2);   // endless: every later boss is tougher
                    e.Hp = baseHealth * (1f + 0.18f * region) * partyScale * LevelScale;
                    e.Damage = (20f + 3f * region) * (1f + 0.03f * elapsedWaves) * levelDamage;
                    e.Speed = 1.6f * (1f + 0.1f * region) * (1f + Mathf.Min(0.2f, 0.01f * elapsedWaves));
                    e.Kind = emperor ? Defs.KindEmperor : Wave >= 20 ? Defs.KindCommander : Defs.KindHeavyBoss;
                    e.Scale = Defs.Kinds[e.Kind].Scale;
                    e.Xp = 300;
                }
                else
                {
                    e.Hp = (55f + 10f * region) * (1f + 0.08f * elapsedWaves) * LevelScale;
                    e.Damage = (10f + 2f * region) * (1f + 0.04f * elapsedWaves) * levelDamage;
                    e.Speed = (2.1f + 0.3f * region) * (1f + Mathf.Min(0.25f, 0.015f * elapsedWaves));
                    e.Xp = 20 + 5 * Region;
                    int kind = Defs.KindGrunt;
                    if (EnemyVariety && pool.Count > 0 && Rand01() < 0.55f) kind = pool[rng.Next(pool.Count)];
                    ApplyKind(e, kind);
                }
                e.Hp *= tier.Hp * Defs.EndlessHp(Wave);
                // 無盡: tougher enemies are worth more, so levels keep coming past wave 20.
                if (Endless && !boss) e.Xp = Mathf.RoundToInt(e.Xp * Mathf.Sqrt(Defs.EndlessHp(Wave)));
                e.Damage *= tier.Damage * Defs.EndlessDamage(Wave);
                e.MaxHp = e.Hp;
                e.Wave = Wave;
                Enemies[e.Id] = e;
            }
            // Every third regular wave promotes one enemy to an elite that drops a chest.
            if (!hasBoss && Wave % (int)Defs.EliteEvery == 0)
            {
                var elite = Enemies.Values.Where(x => !x.Boss && x.Wave == Wave).OrderBy(_ => rng.Next()).FirstOrDefault();
                if (elite != null)
                {
                    elite.Elite = true;
                    elite.Hp = elite.MaxHp = elite.MaxHp * 4f;
                    elite.Damage *= 1.4f;
                    elite.Scale *= 1.35f;
                    elite.Xp *= 3;
                    Announcements.Add("精英敵人出現 · 擊敗可獲得寶箱");
                }
            }
            foreach (var a in Actors.Values)
                if (a.Alive) a.Hp = Mathf.Min(a.MaxHp, a.Hp + a.Regen);
            waveStart = Elapsed;
            waveDamage = 0;
            waveGraded = false;
            bool finalBoss = hasBoss && Enemies.Values.Any(x => x.Kind == Defs.KindEmperor);
            Announcements.Add(finalBoss ? "赫利奧皇帝 降臨" : hasBoss ? "BOSS 來襲" : Endless && Wave > Defs.WavesPerMission && Wave % 5 == 0 ? $"第 {Wave} 波 · 敵人持續強化" : $"第 {Wave} 波");
        }

        public void ApplyKind(Enemy e, int kind)
        {
            e.Kind = kind;
            e.Scale = Defs.Kinds[kind].Scale;
            int baseXp = 20 + 5 * Region;
            switch (kind)
            {
                case Defs.KindCharger: e.Hp *= 0.7f; e.Speed *= 1.7f; e.Shoots = false; e.Xp = baseXp + 2; break;
                case Defs.KindSniper: e.Hp *= 0.8f; e.Speed *= 0.9f; e.Keep = 13f; e.RangedOnly = true; e.ShotCooldown = Rand(2.5f, 4f); e.Xp = baseXp + 10; break;
                case Defs.KindSplitter: e.Hp *= 1.3f; e.Speed *= 0.85f; e.Xp = baseXp + 6; break;
                case Defs.KindSplitling: e.Hp *= 0.3f; e.Speed *= 1.45f; e.Shoots = false; e.Damage *= 0.6f; e.Xp = 6; break;
                case Defs.KindMedic: e.Hp *= 0.9f; e.Keep = 8f; e.Shoots = false; e.RingCooldown = Defs.MedicEvery; e.Xp = baseXp + 12; break;
                case Defs.KindBomber: e.Hp *= 0.45f; e.Speed *= 1.9f; e.Shoots = false; e.RangedOnly = true; e.Xp = baseXp; break;
                case Defs.KindShield: e.Hp *= 1.6f; e.Speed *= 0.85f; e.Xp = baseXp + 12; break;
                case Defs.KindGuard: e.Hp *= 2.2f; e.Speed *= 0.95f; e.Shoots = false; e.Xp = baseXp + 18; e.ChargeCooldown = Rand(2f, 4f); break;
                default: e.Xp = baseXp; break;
            }
            e.MaxHp = e.Hp;
        }

        Enemy NewEnemy(Vector3 position)
        {
            float region = Region;
            var tier = Defs.Difficulty(Difficulty);
            return new Enemy
            {
                Id = nextEnemy++, Position = position, Hp = (55f + 10f * region) * LevelScale * tier.Hp, Speed = 2.1f + 0.3f * region,
                Damage = (10f + 2f * region) * (1f + 0.05f * (Level - 1)) * tier.Damage, ShotCooldown = Rand(1.5f, 3f),
            };
        }

        // ------------------------------------------------------------ player fire

        /// Perk effect for the actor's current weapon: 0 below 傳奇, the base value at 傳奇, double at 神話.
        static float PerkStrength(Actor a, float value)
        {
            int rarity = a.Loadout.RarityOf(a.WeaponId);
            return rarity >= 4 ? value * 2f : rarity >= 3 ? value : 0f;
        }

        /// Admin auto-aim: the visible enemy head closest to the crosshair within a 30 degree cone.
        Vector3? AutoAimTarget(Vector3 origin, Vector3 aim)
        {
            Vector3? best = null;
            float bestAngle = 30f;
            foreach (var e in Enemies.Values)
            {
                var head = e.Position + Vector3.up * Defs.HeadHeight * e.Scale;
                var to = head - origin;
                float distance = to.magnitude;
                if (distance < 0.1f || distance > Defs.ShotRange) continue;
                float angle = Vector3.Angle(aim, to);
                if (angle >= bestAngle || RayBlock(origin, to / distance, distance) < distance - 0.05f) continue;
                bestAngle = angle; best = head;
            }
            return best;
        }

        public void Fire(int id, Vector3 target)
        {
            var a = Actors[id];
            Vector3 origin = a.Position + Vector3.up * Defs.EyeHeight;
            Vector3 aim = (target - origin).normalized;
            if (a.IsAdmin && a.Admin.AutoAim && AutoAimTarget(origin, aim) is Vector3 lockOn) aim = (lockOn - origin).normalized;
            float damage = a.Damage;
            // AR-27 perk: a banked guaranteed crit from an earlier headshot.
            if (a.PerkCrits > 0) { a.PerkCrits--; damage *= 2f; }
            else if (Rand01() < a.Critical) damage *= 2f;
            a.StatShots++;
            int hitsBefore = a.StatHits;
            for (int i = 0; i < a.Pellets; i++)
            {
                Vector3 dir = aim;
                float spread = a.Spread * (a.Aiming ? (a.Pellets > 1 ? Defs.AimSpreadPellets : Defs.AimSpread) : 1f);
                if (spread > 0 && !(a.IsAdmin && a.Admin.AutoAim))
                    dir = (aim + new Vector3(Rand(-spread, spread), Rand(-spread, spread) * 0.7f, Rand(-spread, spread))).normalized;
                // 穿透: the same ray goes on past each enemy it hits, weaker each time; walls still stop it.
                HashSet<Enemy> passed = null;
                float dmg = damage;
                for (int n = 0; n <= a.Pierce; n++)
                {
                    var hit = FireRay(a, origin, dir, dmg, passed);
                    if (hit == null || n == a.Pierce) break;
                    (passed ??= new HashSet<Enemy>()).Add(hit);
                    dmg *= 1f - a.PierceFalloff;
                }
            }
            if (a.StatHits > hitsBefore) a.StatHits = hitsBefore + 1;
        }

        /// Sideways offset for a leg joint, across the enemy's facing (it faces its movement / target).
        static Vector3 LegOffset(Enemy e, float side)
        {
            var f = new Vector3(Mathf.Sin(e.Yaw), 0, Mathf.Cos(e.Yaw));
            return new Vector3(f.z, 0, -f.x) * side;
        }

        /// One ray; returns the enemy it hit (or null). Enemies in skip are passed through (piercing rounds).
        Enemy FireRay(Actor a, Vector3 origin, Vector3 dir, float damage, HashSet<Enemy> skip = null)
        {
            Enemy best = null;
            // Walls and the ground stop the shot: nothing behind them can be hit.
            float nearest = RayBlock(origin, dir, Defs.ShotRange);
            bool head = false;
            foreach (var e in Enemies.Values)
            {
                if (skip != null && skip.Contains(e)) continue;
                float s = e.Scale;
                var headCenter = e.Position + Vector3.up * Defs.HeadHeight * s;
                float headRadius = Defs.HeadRadius * s * (e.Boss ? 1.07f : 1f);
                var bodyCenter = e.Position + Vector3.up * s * (e.Boss ? 0.93f : 1f);
                float bodyRadius = e.Boss ? 0.64f * s : 0.75f * s;
                int weak = e.Boss ? Defs.WeakPoint(e.Kind) : Defs.WeakHead;
                // A boss whose weak point is elsewhere takes plain damage on the head. The head is tested on its own:
                // the body sphere is large and overlaps it, and on big enemies (elites, guards) its centre often lies
                // nearer along the ray, which used to turn clean head hits into body hits.
                bool throughHead = Crosses(headCenter, headRadius);
                TestSphere(headCenter, headRadius, weak == Defs.WeakHead);
                if (!throughHead) TestSphere(bodyCenter, bodyRadius, false);

                bool Crosses(Vector3 center, float radius)
                {
                    float along = Vector3.Dot(center - origin, dir);
                    return along > 0 && along < nearest && Vector3.Distance(center, origin + dir * along) < radius;
                }
                // Leg joints sit below the body sphere: their own target, and the weak point on the 指揮官.
                if (e.Boss)
                    foreach (float side in new[] { -0.19f, 0.19f })
                        TestSphere(e.Position + Vector3.up * 0.46f * s + LegOffset(e, side * s), 0.2f * s, weak == Defs.WeakLegs);

                void TestSphere(Vector3 center, float radius, bool isHead)
                {
                    float along = Vector3.Dot(center - origin, dir);
                    if (along > 0 && along < nearest && Vector3.Distance(center, origin + dir * along) < radius)
                    {
                        best = e; nearest = along; head = isHead;
                    }
                }
            }
            // Chest core (重裝兵): a hit through the middle of the body, where the glowing core sits, is critical.
            // Leg joints (指揮官) likewise: the crude body sphere overlaps them, so a ray passing through a joint counts.
            if (best != null && !head && best.Boss)
            {
                bool Through(Vector3 point, float radius)
                {
                    float along = Vector3.Dot(point - origin, dir);
                    return along > 0 && Vector3.Distance(point, origin + dir * along) < radius;
                }
                float bs = best.Scale;
                int weak = Defs.WeakPoint(best.Kind);
                if (weak == Defs.WeakChest) head = Through(best.Position + Vector3.up * 1.28f * bs, 0.24f * bs);
                else if (weak == Defs.WeakLegs)
                    head = Through(best.Position + Vector3.up * 0.46f * bs + LegOffset(best, -0.19f * bs), 0.2f * bs)
                        || Through(best.Position + Vector3.up * 0.46f * bs + LegOffset(best, 0.19f * bs), 0.2f * bs);
            }
            Vector3 end = origin + dir * nearest;
            float dealt = 0;
            bool killed = false;
            if (best != null)
            {
                a.StatHits++;
                if (head) a.StatHeadshots++;
                float headMult = head ? a.HeadMultiplier * (a.WeaponId == "sniper" ? 1f + PerkStrength(a, 0.4f) : 1f) : 1f;
                dealt = damage * headMult * DamageMultiplier(best, dir, head);
                Credit(a, best, dealt);
                best.Hp -= dealt;
                if (head && a.WeaponId == "rifle" && PerkStrength(a, 1f) > 0) a.PerkCrits = Mathf.RoundToInt(PerkStrength(a, 1f));
                if (best.Hp <= 0)
                {
                    KillEnemy(best, a.Id); killed = true;
                    if (a.WeaponId == "shotgun") Heal(a, PerkStrength(a, 6f));
                    if (a.WeaponId == "pistol" && PerkStrength(a, 1f) > 0) a.PerkHaste = 2f;
                }
            }
            // A pierced round that finds nothing more just ends: no second tracer for it.
            if (skip == null || best != null)
                Shots.Add(new ShotEvent { Shooter = a.Id, Origin = origin, End = end, Hit = best != null, Head = head, Dealt = dealt, Killed = killed, Pierced = skip != null });
            return best;
        }

        /// Shield troopers block body shots from the front; flank them or aim for the head.
        /// Damage factor for a bullet travelling along velocity into a player: blocked from the front with a blade.
        public static float BlockedBy(Actor a, Vector3 velocity)
        {
            if (!a.Blocking || velocity.sqrMagnitude < 1e-4f) return 1f;
            var incoming = new Vector3(-velocity.x, 0, -velocity.z).normalized;
            return Vector3.Angle(incoming, LookDirection(a.Yaw, 0)) <= Defs.BlockHalfArc ? Defs.BlockFactor : 1f;
        }

        /// One sword swing: every enemy within reach inside the frontal arc (and not behind cover) is hit, nearest
        /// first, up to MeleeCleave. A swing that hits nothing still reports a miss so clients play the swing.
        public void Swing(Actor a)
        {
            Vector3 eye = a.Position + Vector3.up * Defs.EyeHeight;
            Vector3 forward = LookDirection(a.Yaw, 0);
            float damage = a.Damage;
            if (Rand01() < a.Critical) damage *= 2f;
            a.StatShots++;
            var targets = new List<(Enemy e, float d)>();
            foreach (var e in Enemies.Values)
            {
                var flat = new Vector3(e.Position.x - a.Position.x, 0, e.Position.z - a.Position.z);
                float d = flat.magnitude, reach = a.Range + 0.45f * e.Scale;
                if (d > reach || (d > 0.3f && Vector3.Angle(flat, forward) > Defs.MeleeHalfArc)) continue;
                var chest = e.Position + Vector3.up * e.Scale;
                var to = chest - eye;
                if (to.sqrMagnitude > 0.01f && RayBlock(eye, to.normalized, to.magnitude) < to.magnitude - 0.1f) continue;
                targets.Add((e, d));
            }
            targets.Sort((x, y) => x.d.CompareTo(y.d));
            int hits = 0;
            foreach (var (e, _) in targets)
            {
                if (hits >= Defs.MeleeCleave) break;
                hits++;
                var chest = e.Position + Vector3.up * e.Scale;
                // The nearest takes the full blow; the others in the arc take MeleeCleaveFalloff of it.
                float dealt = damage * DamageMultiplier(e, (chest - eye).normalized, false) * (hits == 1 ? 1f : Defs.MeleeCleaveFalloff);
                Credit(a, e, dealt);
                e.Hp -= dealt;
                bool killed = false;
                Heal(a, PerkStrength(a, 2f));   // 嗜能之刃
                if (e.Hp <= 0) { KillEnemy(e, a.Id); killed = true; }
                Shots.Add(new ShotEvent { Shooter = a.Id, Origin = eye, End = chest, Hit = true, Dealt = dealt, Killed = killed });
            }
            if (hits > 0) a.StatHits++;
            else Shots.Add(new ShotEvent { Shooter = a.Id, Origin = eye, End = eye + forward * a.Range, Hit = false });
        }

        public static float DamageMultiplier(Enemy e, Vector3 dir, bool head)
        {
            if (e.Kind == Defs.KindShield && !head && Vector3.Dot(dir, e.Facing) < -0.3f) return 0.25f;
            if (e.Kind == Defs.KindGuard && !head && Vector3.Dot(dir, e.Facing) < -0.3f) return 0.4f;   // energy shield in front
            return 1f;
        }

        void KillEnemy(Enemy e, int shooter)
        {
            if (!Enemies.Remove(e.Id)) return;
            Kills++;
            if (e.Boss) GainXp(300);
            else
            {
                var crystal = new Pickup { Id = nextPickup++, Position = new Vector3(e.Position.x, 0, e.Position.z), Kind = PickupKind.Xp, Value = e.Xp, Life = 40f };
                Pickups[crystal.Id] = crystal;
            }
            if (e.Elite) SpawnPickup(e.Position + new Vector3(0.8f, 0, 0), PickupKind.Chest);
            if (e.Boss && (Endless || e.Wave < Defs.WavesPerMission))
            {
                SpawnPickup(e.Position + new Vector3(-1f, 0, 0.6f), PickupKind.SuperChest);
                Pickups[nextPickup - 1].Life = 90f;
                Announcements.Add("BOSS 掉落超級寶箱！");
            }
            if (e.Kind == Defs.KindSplitter)
                for (int i = 0; i < 2 && Enemies.Count < Defs.MaxEnemies; i++)
                {
                    var spawn = NewEnemy(PushOutOfCover(e.Position + new Vector3(i == 0 ? -0.7f : 0.7f, 0, 0), 0.4f));
                    spawn.Hp *= 1f + 0.08f * Mathf.Max(0, Wave - 1);
                    ApplyKind(spawn, Defs.KindSplitling);
                    spawn.MaxHp = spawn.Hp;
                    spawn.Wave = e.Wave;
                    Enemies[spawn.Id] = spawn;
                }
            // Bosses and elites are team victories: everyone in the mission gets the record.
            if (e.Boss || e.Elite)
                foreach (var member in Actors.Values) { if (e.Boss) member.StatBossKills++; else member.StatEliteKills++; }
            if (Actors.TryGetValue(shooter, out var a) && a.Alive)
            {
                a.StatKills++;
                if (a.Lifesteal > 0) Heal(a, a.Lifesteal);
                if (a.Explode > 0) Explode(e.Position + Vector3.up, 3.2f, a.Explode, -1, new Color32(0xff, 0xb3, 0x47, 255));
            }
            if (e.Boss) SpawnPickup(e.Position, PickupKind.Health);
            else if (Rand01() < Defs.PickupChance) SpawnPickup(e.Position, Rand01() < 0.4f ? PickupKind.Health : PickupKind.Ammo);
        }

        // ------------------------------------------------------------ companion gadgets

        /// Gadget shots carry Shooter = -(owner + 1) so clients draw them without recoil or muzzle flash.
        public const int GadgetDrone = 0, GadgetPlasma = 1, GadgetLightning = 2, GadgetLaser = 3;
        public static int GadgetShooter(int owner, int type = GadgetDrone) => -(owner + 1 + 1000 * type);
        public static int GadgetType(int shooter) => (-shooter - 1) / 1000;
        public static int GadgetOwner(int shooter) => (-shooter - 1) % 1000;

        void UpdateGadgets(float dt)
        {
            float scale = 1f + 0.25f * Region;
            foreach (var a in Actors.Values)
            {
                if (!a.Alive) continue;
                if (a.Drones > 0 && (a.DroneTimer -= dt) <= 0)
                {
                    a.DroneTimer = Defs.DroneInterval * a.DroneRateMult * (a.DroneEvolved ? 0.75f : 1f);
                    var targets = Enemies.Values.Where(e => e.Hp > 0 && (e.Position - a.Position).sqrMagnitude < Defs.DroneRange * Defs.DroneRange)
                        .OrderBy(e => (e.Position - a.Position).sqrMagnitude).Take(a.Drones).ToList();
                    for (int i = 0; i < targets.Count; i++)
                    {
                        var e = targets[i];
                        if (!Enemies.ContainsKey(e.Id)) continue;
                        Vector3 origin = a.Position + Defs.DroneOffset(i, a.Drones, Elapsed);
                        Vector3 end = e.Position + Vector3.up * 1.1f * e.Scale;
                        float dealt = Defs.DroneDamage * scale * a.DroneDamageMult * a.DamageMult * (a.DroneEvolved ? 2f : 1f);
                        Credit(a, e, dealt);
                        e.Hp -= dealt;
                        bool killed = e.Hp <= 0;
                        if (killed) KillEnemy(e, a.Id);
                        Shots.Add(new ShotEvent { Shooter = GadgetShooter(a.Id, a.DroneEvolved ? GadgetLaser : GadgetDrone), Origin = origin, End = end, Hit = true, Dealt = dealt, Killed = killed });
                    }
                    if (targets.Count == 0) a.DroneTimer = 0.2f;
                }
                if (a.Plasma && (a.PlasmaTimer -= dt) <= 0)
                {
                    // Aim at the enemy with the most neighbours inside the blast radius.
                    var inRange = Enemies.Values.Where(e => e.Hp > 0 && (e.Position - a.Position).sqrMagnitude < Defs.PlasmaRange * Defs.PlasmaRange).ToList();
                    if (inRange.Count == 0) { a.PlasmaTimer = 0.3f; continue; }
                    float radius = Defs.PlasmaRadius * (a.PlasmaEvolved ? 1.5f : 1f);
                    var targets = inRange.OrderByDescending(e => inRange.Count(o => (o.Position - e.Position).sqrMagnitude < radius * radius)).Take(a.PlasmaEvolved ? 2 : 1).ToList();
                    a.PlasmaTimer = Defs.PlasmaInterval * a.PlasmaRateMult;
                    foreach (var target in targets)
                    {
                        Vector3 at = target.Position + Vector3.up;
                        Shots.Add(new ShotEvent { Shooter = GadgetShooter(a.Id, GadgetPlasma), Origin = a.Position + new Vector3(0, 2.0f, 0), End = at, Hit = true });
                        Explode(at, radius, Defs.PlasmaDamage * scale * a.PlasmaDamageMult * a.DamageMult, a.Id, new Color32(0x9b, 0x7b, 0xff, 255));
                    }
                }
            }
        }

        void UpdateOrdnance(float dt)
        {
            float scale = 1f + 0.25f * Region;
            foreach (var a in Actors.Values)
            {
                if (!a.Alive) continue;
                // Orbiting blades: damage everything they sweep through on a short tick.
                if (a.OrbitLevel > 0 && (a.OrbitTimer -= dt) <= 0)
                {
                    a.OrbitTimer = 0.35f;
                    int n = Defs.BladeCount(a.OrbitLevel);
                    float damage = (10f + 6f * a.OrbitLevel) * scale * a.DamageMult;
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 blade = a.Position + Defs.BladeOffset(i, n, Elapsed);
                        foreach (var e in Enemies.Values.ToList())
                        {
                            if (!Enemies.ContainsKey(e.Id)) continue;
                            if (new Vector2(e.Position.x - blade.x, e.Position.z - blade.z).magnitude > 0.8f + 0.4f * e.Scale) continue;
                            Credit(a, e, damage);
                            e.Hp -= damage;
                            if (e.Hp <= 0) KillEnemy(e, a.Id);
                        }
                    }
                }
                // Proximity mines dropped at the player's feet.
                if (a.MineLevel > 0 && (a.MineTimer -= dt) <= 0)
                {
                    a.MineTimer = 4.5f * (1f - 0.1f * (a.MineLevel - 1));
                    if (Bullets.Values.Count(b => b.Kind == BulletKind.Mine && b.Owner == a.Id) < 6)
                    {
                        var mine = new Bullet { Id = nextBullet++, Kind = BulletKind.Mine, Owner = a.Id, Position = new Vector3(a.Position.x, 0.1f, a.Position.z), Life = 25f,
                            Damage = (55f + 20f * (a.MineLevel - 1)) * scale * a.DamageMult };
                        Bullets[mine.Id] = mine;
                    }
                }
                // Homing missiles, one per level, each after a different target.
                if (a.MissileLevel > 0 && (a.MissileTimer -= dt) <= 0)
                {
                    var targets = Enemies.Values.Where(e => (e.Position - a.Position).sqrMagnitude < 900f).OrderBy(e => (e.Position - a.Position).sqrMagnitude).Take(Mathf.Min(5, a.MissileLevel)).ToList();
                    a.MissileTimer = targets.Count == 0 ? 0.3f : 2.6f;
                    for (int i = 0; i < targets.Count; i++)
                    {
                        float side = (i - (targets.Count - 1) / 2f) * 2.5f;
                        var missile = new Bullet { Id = nextBullet++, Kind = BulletKind.Missile, Owner = a.Id, Target = targets[i].Id, Life = 4f,
                            Position = a.Position + Vector3.up * 2f, Velocity = new Vector3(Mathf.Cos(a.Yaw) * side, 7f, -Mathf.Sin(a.Yaw) * side),
                            Damage = (30f + 8f * a.MissileLevel) * scale * a.DamageMult };
                        Bullets[missile.Id] = missile;
                    }
                }
                // Chain lightning: jumps between nearby enemies, losing strength each hop.
                if (a.LightningLevel > 0 && (a.LightningTimer -= dt) <= 0)
                {
                    var first = Enemies.Values.Where(e => (e.Position - a.Position).sqrMagnitude < 18f * 18f).OrderBy(e => (e.Position - a.Position).sqrMagnitude).FirstOrDefault();
                    if (first == null) { a.LightningTimer = 0.3f; continue; }
                    a.LightningTimer = 3.2f * (1f - 0.08f * (a.LightningLevel - 1));
                    float damage = (28f + 10f * a.LightningLevel) * scale * a.DamageMult;
                    var hit = new HashSet<int>();
                    Vector3 from = a.Position + Vector3.up * 1.6f;
                    var current = first;
                    for (int jump = 0; jump < 2 + a.LightningLevel && current != null; jump++)
                    {
                        hit.Add(current.Id);
                        Vector3 to = current.Position + Vector3.up * current.Scale;
                        Credit(a, current, damage);
                        current.Hp -= damage;
                        bool killed = current.Hp <= 0;
                        Shots.Add(new ShotEvent { Shooter = GadgetShooter(a.Id, GadgetLightning), Origin = from, End = to, Hit = true, Dealt = damage, Killed = killed });
                        Vector3 at = current.Position;
                        if (killed) KillEnemy(current, a.Id);
                        damage *= 0.9f;
                        from = to;
                        current = Enemies.Values.Where(e => !hit.Contains(e.Id) && (e.Position - at).sqrMagnitude < 49f).OrderBy(e => (e.Position - at).sqrMagnitude).FirstOrDefault();
                    }
                }
            }
            foreach (var b in Bullets.Values.ToList())
            {
                if (b.Kind == BulletKind.Mine)
                {
                    b.Life -= dt;
                    var trigger = Enemies.Values.FirstOrDefault(e => new Vector2(e.Position.x - b.Position.x, e.Position.z - b.Position.z).magnitude < 1.6f);
                    if (trigger != null || b.Life <= 0)
                    {
                        Bullets.Remove(b.Id);
                        if (trigger != null) Explode(b.Position + Vector3.up * 0.5f, 3.5f, b.Damage, b.Owner, new Color32(0xff, 0xd1, 0x66, 255));
                    }
                }
                else if (b.Kind == BulletKind.Missile)
                {
                    b.Life -= dt;
                    if (!Enemies.TryGetValue(b.Target, out var target))
                    {
                        target = Enemies.Values.OrderBy(e => (e.Position - b.Position).sqrMagnitude).FirstOrDefault();
                        b.Target = target?.Id ?? -1;
                    }
                    if (target != null)
                    {
                        Vector3 aim = target.Position + Vector3.up * target.Scale - b.Position;
                        b.Velocity = Vector3.Lerp(b.Velocity, aim.normalized * 17f, Mathf.Min(1f, dt * 4f));
                        if (aim.magnitude < 0.8f + 0.5f * target.Scale)
                        {
                            Bullets.Remove(b.Id);
                            Explode(b.Position, 2.2f, b.Damage, b.Owner, new Color32(0xff, 0x8a, 0x3d, 255));
                            continue;
                        }
                    }
                    b.Position += b.Velocity * dt;
                    if (b.Life <= 0 || b.Position.y < 0) Bullets.Remove(b.Id);
                }
            }
        }

        /// One of the player's melee slots, if one has been free for a full melee interval (bosses' slams are separate).
        bool TakeMeleeSlot(Actor a)
        {
            for (int i = 0; i < a.MeleeHitTimes.Length; i++)
                if (Elapsed - a.MeleeHitTimes[i] >= 0.85f) { a.MeleeHitTimes[i] = Elapsed; return true; }
            return false;
        }

        /// Adds damage to a player's contribution, counting only the health the enemy actually had left.
        static void Credit(Actor a, Enemy e, float amount)
        {
            if (a != null && e.Hp > 0) a.StatDamage += Mathf.Min(amount, e.Hp);
        }

        void Explode(Vector3 position, float radius, float damage, int owner, Color32 color)
        {
            foreach (var e in Enemies.Values.ToList())
            {
                if (e.Hp <= 0 || !Enemies.ContainsKey(e.Id)) continue;
                float d = Vector3.Distance(e.Position + Vector3.up, position);
                if (d >= radius) continue;
                if (Actors.TryGetValue(owner, out var credited)) Credit(credited, e, damage * (1f - 0.5f * d / radius));
                e.Hp -= damage * (1f - 0.5f * d / radius);
                if (e.Hp <= 0) KillEnemy(e, owner);
            }
            Explosions.Add(new ExplosionEvent { Position = position, Radius = radius, Color = color });
        }

        // ------------------------------------------------------------ enemy fire & bullets

        void UpdateEnemyFire(float dt)
        {
            var alive = Actors.Values.Where(a => a.Alive).ToList();
            foreach (var e in Enemies.Values.ToList())
            {
                if (alive.Count == 0 || Bullets.Count >= Defs.MaxBullets) break;
                if (!e.Shoots) continue;
                e.ShotCooldown -= dt;
                if (e.ShotCooldown > 0) continue;
                Actor target = null;
                float best = Defs.BulletRange;
                foreach (var a in alive)
                {
                    float d = Vector3.Distance(e.Position, a.Position);
                    if (d < best) { best = d; target = a; }
                }
                if (target == null) { e.ShotCooldown = 0.5f; continue; }
                Vector3 origin = e.Position + Vector3.up * 1.15f * e.Scale;
                Vector3 aim = (target.Position + Vector3.up - origin).normalized;
                float damage = e.Damage * 0.6f;
                if (e.Kind == Defs.KindSniper)
                {
                    SpawnBullet(origin, aim * Defs.SniperBulletSpeed, e.Damage * 1.3f, BulletKind.Sniper);
                    e.ShotCooldown = Rand(3.5f, 5f);
                }
                else if (e.Boss)
                {
                    foreach (float spread in new[] { -0.36f, -0.18f, 0f, 0.18f, 0.36f })
                        SpawnBullet(origin, Quaternion.AngleAxis(spread * Mathf.Rad2Deg, Vector3.up) * aim * Defs.BossBulletSpeed, damage * 0.8f, BulletKind.Enemy);
                    e.ShotCooldown = Rand(1.8f, 2.6f);
                }
                else
                {
                    SpawnBullet(origin, aim * Defs.BulletSpeed, damage, BulletKind.Enemy);
                    e.ShotCooldown = Rand(2.4f, 4.2f);
                }
            }
        }

        public Bullet SpawnBullet(Vector3 origin, Vector3 velocity, float damage, BulletKind kind)
        {
            var b = new Bullet { Id = nextBullet++, Position = origin, Velocity = velocity, Damage = damage, Kind = kind };
            Bullets[b.Id] = b;
            return b;
        }

        void UpdateBullets(float dt)
        {
            var alive = Actors.Values.Where(a => a.Alive).ToList();
            foreach (var b in Bullets.Values.ToList())
            {
                if (b.Kind == BulletKind.Grenade || b.Kind == BulletKind.Mine || b.Kind == BulletKind.Missile) continue;
                if (b.Kind == BulletKind.Slam)
                {
                    b.Life -= dt;
                    if (b.Life > 0) continue;
                    Bullets.Remove(b.Id);
                    foreach (var a in alive)
                        if (new Vector2(a.Position.x - b.Position.x, a.Position.z - b.Position.z).magnitude < Defs.SlamRadius) a.Hp -= b.Damage;
                    Explosions.Add(new ExplosionEvent { Position = b.Position + Vector3.up * 0.3f, Radius = Defs.SlamRadius, Color = new Color32(0xff, 0x4b, 0x3a, 255) });
                    continue;
                }
                b.Position += b.Velocity * dt;
                b.Life -= dt;
                bool hit = false;
                foreach (var a in alive)
                {
                    Vector3 body = a.Position + Vector3.up * 0.9f;
                    if (Mathf.Abs(b.Position.y - body.y) < 1f && new Vector2(b.Position.x - body.x, b.Position.z - body.z).magnitude < Defs.BulletHitRadius)
                    {
                        a.Hp -= b.Damage * BlockedBy(a, b.Velocity);
                        hit = true;
                        break;
                    }
                }
                if (hit || b.Life <= 0 || Mathf.Abs(b.Position.x) > 23 || Mathf.Abs(b.Position.z) > 23 || b.Position.y < 0 || InCover(b.Position))
                    Bullets.Remove(b.Id);
            }
        }

        void UpdateGrenades(float dt)
        {
            foreach (var b in Bullets.Values.ToList())
            {
                if (b.Kind != BulletKind.Grenade) continue;
                b.Velocity.y -= Defs.Gravity * dt;
                b.Position += b.Velocity * dt;
                b.Life -= dt;
                if (b.Position.y <= 0.15f || b.Life <= 0 || InCover(b.Position) || Mathf.Abs(b.Position.x) > 22.5f || Mathf.Abs(b.Position.z) > 22.5f)
                {
                    Bullets.Remove(b.Id);
                    float mult = Actors.TryGetValue(b.Owner, out var owner) ? owner.DamageMult : 1f;
                    Explode(b.Position + Vector3.up * 0.5f, Defs.GrenadeRadius, Defs.GrenadeDamage * (1f + 0.15f * Region) * mult, b.Owner, new Color32(0xff, 0xb3, 0x47, 255));
                }
            }
        }

        void UpdateEnemyKinds(float dt)
        {
            var alive = Actors.Values.Where(a => a.Alive).ToList();
            foreach (var e in Enemies.Values.ToList())
            {
                if (!Enemies.ContainsKey(e.Id)) continue;
                if (e.Kind == Defs.KindSniper) e.State = e.ShotCooldown < Defs.SniperWarning ? 1 : 0;
                else if (e.Kind == Defs.KindBomber)
                {
                    if (alive.Any(a => Vector3.Distance(a.Position, e.Position) < 1.8f))
                    {
                        foreach (var a in alive)
                            if (Vector3.Distance(a.Position, e.Position) < 3.2f) a.Hp -= e.Damage * 2.2f;
                        Enemies.Remove(e.Id);
                        Explosions.Add(new ExplosionEvent { Position = e.Position + Vector3.up, Radius = 3.2f, Color = Defs.Kinds[Defs.KindBomber].Color });
                        continue;
                    }
                }
                if (e.Kind == Defs.KindMedic) { UpdateMedic(e, dt); continue; }
                if (e.Boss) UpdateSlam(e, alive, dt);
                if (e.Kind == Defs.KindGuard) { UpdateGuard(e, alive, dt); continue; }
                if (e.Kind == Defs.KindEmperor) { UpdateEmperor(e, alive, dt); continue; }
                if (!e.Boss) continue;
                if (e.Phase == 1 && e.Hp < e.MaxHp * 0.5f)
                {
                    e.Phase = 2;
                    e.State = 2;
                    e.Speed *= 1.3f;
                    for (int i = 0; i < 4 && Enemies.Count < Defs.MaxEnemies; i++)
                    {
                        float angle = Mathf.PI * 2f * i / 4f;
                        var minion = NewEnemy(e.Position + new Vector3(Mathf.Sin(angle) * 3, 0, Mathf.Cos(angle) * 3));
                        ApplyKind(minion, Defs.KindCharger);
                        Enemies[minion.Id] = minion;
                    }
                    Announcements.Add("BOSS 進入狂暴階段！");
                }
                if (e.Phase == 2)
                {
                    e.RingCooldown -= dt;
                    if (e.RingCooldown <= 0 && Bullets.Count < Defs.MaxBullets - 16)
                    {
                        e.RingCooldown = 3.5f;
                        float offset = Rand01() * Mathf.PI * 2f;
                        for (int k = 0; k < 14; k++)
                        {
                            float angle = offset + Mathf.PI * 2f * k / 14f;
                            SpawnBullet(e.Position + Vector3.up * 1.6f * (e.Scale / 1.5f), new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * 5.5f, e.Damage * 0.45f, BulletKind.Enemy);
                        }
                    }
                }
            }
        }

        /// 醫療兵: every few seconds a pulse heals wounded allies in range (State 1 flashes for the pulse).
        void UpdateMedic(Enemy e, float dt)
        {
            e.RingCooldown -= dt;
            e.State = e.RingCooldown < 0.5f ? 1 : 0;
            if (e.RingCooldown > 0) return;
            e.RingCooldown = Defs.MedicEvery;
            bool healed = false;
            foreach (var o in Enemies.Values)
            {
                if (o == e || o.Hp >= o.MaxHp || Vector3.Distance(o.Position, e.Position) > Defs.MedicRange) continue;
                o.Hp = Mathf.Min(o.MaxHp, o.Hp + o.MaxHp * Defs.MedicHeal * (o.Boss ? 0.15f : 1f));
                healed = true;
            }
            if (healed) Explosions.Add(new ExplosionEvent { Position = e.Position + Vector3.up * 0.2f, Radius = Defs.MedicRange, Color = Defs.Kinds[Defs.KindMedic].Color, Heal = true });
        }

        /// Boss ground slam: marks a circle under up to two players (everyone once enraged) that bursts after
        /// the wind-up; stepping out of the circle avoids it.
        void UpdateSlam(Enemy e, List<Actor> alive, float dt)
        {
            e.SlamCooldown -= dt;
            if (e.SlamCooldown > 0 || alive.Count == 0) return;
            bool enraged = e.Phase >= 2 || e.Hp < e.MaxHp * 0.5f;
            e.SlamCooldown = enraged ? Rand(3.6f, 4.6f) : Rand(5.5f, 7f);
            int marks = enraged ? alive.Count : Mathf.Min(2, alive.Count);
            foreach (var a in alive.OrderBy(_ => rng.Next()).Take(marks))
            {
                if (Bullets.Count >= Defs.MaxBullets) break;
                var b = SpawnBullet(new Vector3(a.Position.x, 0.05f, a.Position.z), Vector3.zero, e.Damage * 1.5f, BulletKind.Slam);
                b.Life = Defs.SlamWindup;
            }
        }

        /// Imperial Guard: walks in behind its shield; within charge range it winds up (State 1, glowing), then
        /// rushes (State 3) and slams the first player it reaches.
        void UpdateGuard(Enemy e, List<Actor> alive, float dt)
        {
            Actor nearest = null;
            float distance = float.MaxValue;
            foreach (var a in alive) { float d = Vector3.Distance(a.Position, e.Position); if (d < distance) { distance = d; nearest = a; } }
            switch (e.State)
            {
                case 1:
                    e.ChargeTime -= dt;
                    if (e.ChargeTime <= 0) { e.State = 3; e.ChargeTime = Defs.GuardCharge; }
                    break;
                case 3:
                    e.ChargeTime -= dt;
                    if (nearest != null && distance < 1.8f * e.Scale)
                    {
                        nearest.Hp -= e.Damage * 1.6f;
                        e.MeleeCooldown = 1.2f;
                        e.ChargeTime = 0;
                    }
                    if (e.ChargeTime <= 0) { e.State = 0; e.ChargeCooldown = Rand(4f, 6.5f); }
                    break;
                default:
                    e.ChargeCooldown -= dt;
                    if (e.ChargeCooldown <= 0 && nearest != null && distance > 3f && distance < 11f) { e.State = 1; e.ChargeTime = Defs.GuardWindup; }
                    break;
            }
        }

        /// The Emperor, final boss of chapter 2. Phase 1: fan volleys (shared boss fire) and summoned guards.
        /// Phase 2 (below 66%): blinks across the arena and adds bullet rings. Phase 3 (below 33%): faster, with a
        /// rotating double spiral barrage on top.
        void UpdateEmperor(Enemy e, List<Actor> alive, float dt)
        {
            float health = e.Hp / Mathf.Max(1f, e.MaxHp);
            if (e.Phase == 1 && health < Defs.EmperorPhase2)
            {
                e.Phase = 2; e.State = 2;
                Announcements.Add("皇帝：「你以為踏進王座，就能改變什麼？」");
            }
            if (e.Phase == 2 && health < Defs.EmperorPhase3)
            {
                e.Phase = 3;
                e.Speed *= 1.25f;
                Announcements.Add("皇帝怒吼 · 王座之光暴走！");
            }
            e.SummonCooldown -= dt;
            if (e.SummonCooldown <= 0)
            {
                e.SummonCooldown = e.Phase >= 3 ? 9f : 13f;
                for (int i = 0; i < 2 && Enemies.Count < Defs.MaxEnemies; i++)
                {
                    float angle = Rand(0f, Mathf.PI * 2f);
                    var guard = NewEnemy(e.Position + new Vector3(Mathf.Sin(angle) * 3.5f, 0, Mathf.Cos(angle) * 3.5f));
                    ApplyKind(guard, Defs.KindGuard);
                    guard.Wave = Wave;
                    Enemies[guard.Id] = guard;
                }
            }
            if (e.Phase >= 2)
            {
                e.BlinkCooldown -= dt;
                if (e.BlinkCooldown <= 0)
                {
                    e.BlinkCooldown = e.Phase >= 3 ? 5f : 7f;
                    Explosions.Add(new ExplosionEvent { Position = e.Position + Vector3.up * 2f, Radius = 3f, Color = Defs.Kinds[Defs.KindEmperor].Color });
                    e.Position = SpawnNear(PickGates(1)[0], 2f);
                    Explosions.Add(new ExplosionEvent { Position = e.Position + Vector3.up * 2f, Radius = 3.5f, Color = Defs.Kinds[Defs.KindEmperor].Color });
                }
                e.RingCooldown -= dt;
                if (e.RingCooldown <= 0 && Bullets.Count < Defs.MaxBullets - 16)
                {
                    e.RingCooldown = 3.2f;
                    float offset = Rand01() * Mathf.PI * 2f;
                    for (int k = 0; k < 16; k++)
                    {
                        float angle = offset + Mathf.PI * 2f * k / 16f;
                        SpawnBullet(e.Position + Vector3.up * 2.2f, new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * 5.5f, e.Damage * 0.4f, BulletKind.Enemy);
                    }
                }
            }
            if (e.Phase >= 3)
            {
                e.SpiralCooldown -= dt;
                if (e.SpiralCooldown <= 0 && Bullets.Count < Defs.MaxBullets - 4)
                {
                    e.SpiralCooldown = 0.16f;
                    e.SpiralAngle += 0.42f;
                    foreach (float arm in new[] { 0f, Mathf.PI })
                    {
                        float angle = e.SpiralAngle + arm;
                        SpawnBullet(e.Position + Vector3.up * 2.2f, new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * 6f, e.Damage * 0.32f, BulletKind.Enemy);
                    }
                }
            }
        }

        // ------------------------------------------------------------ players: weapons, skill, revive, admin

        /// Kill / hit healing, limited by the actor's heal budget.
        public static void Heal(Actor a, float amount)
        {
            float take = Mathf.Min(amount, a.HealBudget);
            if (take <= 0) return;
            a.HealBudget -= take;
            a.Hp = Mathf.Min(a.MaxHp, a.Hp + take);
        }

        void UpdatePlayers(float dt)
        {
            foreach (var a in Actors.Values)
            {
                Inputs.TryGetValue(a.Id, out var input);
                a.HealBudget = Mathf.Min(a.MaxHp * Defs.HealBank, a.HealBudget + a.MaxHp * Defs.HealPerSecond * dt);
                a.SkillCooldown = Mathf.Max(0, a.SkillCooldown - dt);
                if (a.IsAdmin && a.Admin.God) a.Hp = a.MaxHp;
                if (a.IsAdmin && a.Admin.InfiniteAmmo && a.Alive) { a.Ammo = a.Mag; a.Reload = 0; }
                if (a.WasAlive && !a.Alive) a.StatDowns++;
                a.WasAlive = a.Alive;
                if (!a.Alive)
                {
                    bool Helping(Actor o) => o.Id != a.Id && o.Alive && Vector3.Distance(o.Position, a.Position) < 2.4f &&
                                             Inputs.TryGetValue(o.Id, out var oi) && oi.Interact;
                    bool helped = Actors.Values.Any(Helping);
                    a.Revive = Mathf.Clamp(a.Revive + (helped ? dt : -dt * 0.5f), 0, Defs.ReviveTime);
                    if (a.Revive >= Defs.ReviveTime)
                    {
                        foreach (var o in Actors.Values.Where(Helping).ToList()) o.StatRevives++;
                        a.Hp = a.MaxHp * 0.4f; a.Revive = 0; a.WasAlive = true;
                    }
                    continue;
                }
                a.Revive = 0;
                SwitchWeapon(a, input.Slot);
                if (input.Skill && a.SkillCooldown <= 0)
                {
                    a.SkillCooldown = Defs.SkillCooldown * a.SkillMult;
                    Vector3 origin = a.Position + Vector3.up * Defs.EyeHeight;
                    Vector3 aim = input.Aim == Vector3.zero ? origin + LookDirection(a.Yaw, 0) * 30f : input.Aim;
                    Vector3 dir = (aim - origin).normalized;
                    var g = SpawnBullet(origin + dir * 0.6f, dir * 15f + Vector3.up * 3.5f, 0, BulletKind.Grenade);
                    g.Owner = a.Id;
                    g.Life = 1.8f;
                }
            }
        }

        // ------------------------------------------------------------ pickups

        /// Elite chests grant a free upgrade of at least rare quality to whoever opens them.
        void OpenChest(Actor a)
        {
            var offer = RollOffer(a);
            if (offer.Length == 0) { Hp(a); return; }   // every upgrade maxed: the chest patches you up instead
            int code = offer[rng.Next(offer.Length)];
            code = Defs.BuffIndex(code) + 100 * Mathf.Max(1, Defs.BuffTier(code));
            ApplyBuff(a.Id, code);
            string who = string.IsNullOrEmpty(a.Name) ? "" : a.Name + " ";
            Announcements.Add($"{who}開啟寶箱：{Defs.Buffs[Defs.BuffIndex(code)].Name}（{Defs.RarityNames[Defs.BuffTier(code)]}）");
        }

        void Hp(Actor a) => a.Hp = a.MaxHp;

        /// Health and ammo stay on the ground long enough to walk back for them.
        public const float SupplyLife = 45f;

        public void SpawnPickup(Vector3 position, PickupKind kind)
        {
            var p = new Pickup { Id = nextPickup++, Position = new Vector3(position.x, 0, position.z), Kind = kind,
                Life = kind == PickupKind.Health || kind == PickupKind.Ammo ? SupplyLife : 20f };
            Pickups[p.Id] = p;
        }

        void UpdatePickups(float dt)
        {
            bool cleared = Enemies.Count == 0;
            foreach (var p in Pickups.Values.ToList())
            {
                p.Life -= dt;
                if (p.Life <= 0)
                {
                    if (p.Kind == PickupKind.Xp) GainXp(p.Value);
                    Pickups.Remove(p.Id);
                    continue;
                }
                if (p.Kind == PickupKind.Xp)
                {
                    // Crystals drift to the nearest player inside their magnet radius; all fly in once a wave is cleared.
                    Actor nearest = null;
                    float best = float.MaxValue;
                    foreach (var a in Actors.Values)
                    {
                        if (!a.Alive) continue;
                        float d = new Vector2(a.Position.x - p.Position.x, a.Position.z - p.Position.z).magnitude;
                        if ((cleared || d < a.Magnet) && d < best) { best = d; nearest = a; }
                    }
                    if (nearest == null) continue;
                    if (best < 0.9f) { GainXp(p.Value); Pickups.Remove(p.Id); continue; }
                    Vector3 to = new Vector3(nearest.Position.x - p.Position.x, 0, nearest.Position.z - p.Position.z).normalized;
                    p.Position += to * Mathf.Min(best, (cleared ? 18f : 11f) * dt);
                    continue;
                }
                foreach (var a in Actors.Values)
                {
                    if (!a.Alive || new Vector2(a.Position.x - p.Position.x, a.Position.z - p.Position.z).magnitude > 1.4f) continue;
                    if (p.Kind == PickupKind.Chest)
                    {
                        OpenChest(a);
                    }
                    else if (p.Kind == PickupKind.SuperChest)
                    {
                        // Shared: the whole team gets the picks (downed players too), so it never matters who touches it.
                        SuperRounds += Defs.SuperChestPicks;
                        string who = string.IsNullOrEmpty(a.Name) ? "" : a.Name + " ";
                        Announcements.Add($"{who}開啟超級寶箱：全隊獲得 {Defs.SuperChestPicks} 次強化");
                    }
                    else if (p.Kind == PickupKind.Health)
                    {
                        if (a.Hp >= a.MaxHp) continue;
                        a.Hp = Mathf.Min(a.MaxHp, a.Hp + 30);
                    }
                    else
                    {
                        a.AmmoByWeapon.Clear();
                        a.Ammo = a.Mag;
                        a.Reload = 0;
                    }
                    Pickups.Remove(p.Id);
                    break;
                }
            }
        }

        // ------------------------------------------------------------ experience & buffs

        public void GainXp(int amount)
        {
            Xp += amount;
            while (Xp >= XpNeed)
            {
                Xp -= XpNeed;
                Level++;
                XpNeed = Defs.XpNeed(Level, Endless);
                PendingLevels++;
            }
        }

        void BeginBuffs()
        {
            BuffPending = true;
            BuffWait = 0;
            BuffOffers.Clear();
            // Super chest picks go to the whole team, downed players included: the chest is shared.
            foreach (var a in Actors.Values.Where(a => a.Alive || SuperPick > 0))
            {
                var offer = RollOffer(a);
                if (offer.Length > 0) BuffOffers[a.Id] = offer;   // a fully maxed player skips the pick
                else if (a.Alive) a.Hp = a.MaxHp;
            }
            if (BuffOffers.Count == 0) ResolveBuffs();
        }

        int[] RollOffer(Actor a)
        {
            // Gadget upgrades only appear once the gadget is owned; extra drones stop at the cap.
            var pool = Enumerable.Range(0, Defs.Buffs.Length).Where(i =>
                (i != Defs.BuffDroneDamage && i != Defs.BuffDroneRate || a.Drones > 0) &&
                (i != Defs.BuffPlasmaRate || a.Plasma) &&
                (i != Defs.BuffDrone || a.Drones < Defs.MaxDrones) &&
                (i != Defs.BuffOrbit || a.OrbitLevel < Defs.MaxGadgetLevel) &&
                (i != Defs.BuffMine || a.MineLevel < Defs.MaxGadgetLevel) &&
                (i != Defs.BuffMissile || a.MissileLevel < Defs.MaxGadgetLevel) &&
                (i != Defs.BuffLightning || a.LightningLevel < Defs.MaxGadgetLevel) &&
                (i != Defs.BuffMagnet || a.Magnet < 10f) &&
                (i != Defs.BuffArmor || a.Armor < Defs.MaxArmor) &&
                (i != 8 || a.Lifesteal < Defs.MaxLifesteal) &&
                a.BuffStacks[i] < Defs.BuffStackLimit(i, Endless)).OrderBy(_ => rng.Next()).Take(3).ToArray();
            for (int i = 0; i < pool.Length; i++)
            {
                float roll = Rand01();
                // Super chest picks are 稀有 or better, with a much better shot at 史詩.
                int tier = SuperPick > 0 ? (roll < 0.3f ? 2 : 1) : roll < 0.06f ? 2 : roll < 0.32f ? 1 : 0;
                pool[i] += 100 * tier;
            }
            return pool;
        }

        /// A player picks one of their offered buffs. Returns false if the choice was not offered.
        public bool ChooseBuff(int id, int code)
        {
            if (!BuffPending || !BuffOffers.TryGetValue(id, out var offer) || Array.IndexOf(offer, code) < 0) return false;
            ApplyBuff(id, code);
            if (BuffOffers.Count == 0) ResolveBuffs();
            return true;
        }

        public void ApplyBuff(int id, int code)
        {
            BuffOffers.Remove(id);
            if (!Actors.TryGetValue(id, out var a)) return;
            int index = Defs.BuffIndex(code);
            if (index >= 0 && index < a.BuffStacks.Length) a.BuffStacks[index]++;
            float m = Defs.RarityMultiplier[Defs.BuffTier(code)];
            int steps = Defs.BuffTier(code) == 2 ? 2 : 1;
            switch (index)
            {
                // 無盡: past the normal cap each damage pick compounds, so it keeps pace with multiplicative enemy scaling.
                case 0:
                    if (Endless && a.BuffStacks[0] > Defs.BuffMaxStacks[0]) a.DamageMult *= 1f + Defs.EndlessDamagePick * m;
                    else a.DamageMult += 0.2f * m;
                    break;
                case 1: a.RateMult = Mathf.Max(0.3f, a.RateMult * (1f - 0.15f * m)); break;
                case 2: a.Speed = Mathf.Min(12f, a.Speed * (1f + 0.12f * m)); break;
                case 3:
                    float bonus = Mathf.Round(30 * m * (1f + a.HpPct));   // the life talent grows every pick too
                    // 無盡: past the normal cap a pick grows max life by a share, keeping pace with enemy damage.
                    if (Endless && a.BuffStacks[3] > Defs.BuffMaxStacks[3]) bonus = Mathf.Round(a.MaxHp * Defs.EndlessLifePick * m);
                    a.MaxHp += bonus;
                    a.Hp = Mathf.Min(a.MaxHp, a.Hp + bonus);
                    break;
                case 4: a.Regen += Mathf.Round(15 * m); break;
                case 5: a.Critical = Mathf.Min(Defs.MaxCritical, a.Critical + 0.2f * m); break;
                case 6:
                    a.MagBonus += Mathf.RoundToInt(4 * m);
                    a.ReloadMult = Mathf.Max(0.4f, a.ReloadMult * 0.85f);
                    break;
                case 7: a.SkillMult = Mathf.Max(0.35f, a.SkillMult * (1f - 0.2f * m)); break;
                case 8: a.Lifesteal = Mathf.Min(Defs.MaxLifesteal, a.Lifesteal + Mathf.Round(4 * m)); break;
                case 9: a.Explode += Mathf.Round(35 * m); break;
                case Defs.BuffDrone: a.Drones = Mathf.Min(Defs.MaxDrones, a.Drones + steps); break;
                case Defs.BuffDroneDamage: a.DroneDamageMult += 0.35f * m; break;
                case Defs.BuffDroneRate: a.DroneRateMult = Mathf.Max(0.3f, a.DroneRateMult * (1f - 0.15f * m)); break;
                case Defs.BuffPlasma:
                    if (a.Plasma) a.PlasmaDamageMult += 0.4f * m;
                    else { a.Plasma = true; a.PlasmaTimer = 1.5f; }
                    break;
                case Defs.BuffPlasmaRate: a.PlasmaRateMult = Mathf.Max(0.3f, a.PlasmaRateMult * (1f - 0.15f * m)); break;
                case Defs.BuffOrbit: a.OrbitLevel = Mathf.Min(Defs.MaxGadgetLevel, a.OrbitLevel + steps); break;
                case Defs.BuffMine: a.MineLevel = Mathf.Min(Defs.MaxGadgetLevel, a.MineLevel + steps); a.MineTimer = Mathf.Min(a.MineTimer, 1f); break;
                case Defs.BuffMissile: a.MissileLevel = Mathf.Min(Defs.MaxGadgetLevel, a.MissileLevel + steps); break;
                case Defs.BuffLightning: a.LightningLevel = Mathf.Min(Defs.MaxGadgetLevel, a.LightningLevel + steps); break;
                case Defs.BuffMagnet: a.Magnet = Mathf.Min(10f, a.Magnet * (1f + 0.4f * m)); break;
                case Defs.BuffArmor: a.Armor = Mathf.Min(Defs.MaxArmor, a.Armor + 0.08f * m); break;
            }
            // Evolutions: a fully built gadget transforms into a stronger form.
            if (!a.DroneEvolved && a.Drones >= Defs.MaxDrones && a.DroneDamageMult >= 1.7f)
            {
                a.DroneEvolved = true;
                Announcements.Add("裝置進化：雷射無人機群");
            }
            if (!a.PlasmaEvolved && a.Plasma && a.PlasmaDamageMult >= 1.8f && a.PlasmaRateMult <= 0.8f)
            {
                a.PlasmaEvolved = true;
                Announcements.Add("裝置進化：電漿風暴");
            }
            RefreshWeapon(a);
            if (index == 6 && a.Reload <= 0) a.Ammo = a.Mag;
        }

        public void ResolveBuffs()
        {
            BuffPending = false;
            SuperPick = 0;
            BuffOffers.Clear();
            WaveDelay = Mathf.Min(WaveDelay, 1f);
        }

        // ------------------------------------------------------------ rewards

        /// Upper bound of one mission's gold; mirrors MAX_REWARD in server/service.py.
        public const int MaxMissionGold = 12000;

        public int MissionGold() =>
            Mathf.Clamp(Mathf.RoundToInt((Kills * 3 + Wave * 12 + (Level - 1) * 15 + (State == MissionState.Victory ? 200 + 150 * Region : 0)
                + (Endless ? (Wave / 10) * (150 + 100 * Region) : 0)) * Defs.Difficulty(Difficulty).Gold), 0, MaxMissionGold);
    }
}
