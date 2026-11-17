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
        public int Wave;
        public float WaveDelay = 1.8f;
        public float Elapsed;
        public int Kills;
        public bool EnemyVariety = true;

        public int Level = 1, Xp, XpNeed = Defs.XpForLevel(1), PendingLevels;
        public bool BuffPending;
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
            a.MagBonus = 0; a.Regen = 0; a.Critical = 0; a.Lifesteal = 0; a.Explode = 0;
            a.SkillCooldown = 0; a.Revive = 0; a.Slot = 0; a.Reload = 0;
            a.StatKills = 0; a.StatHeadshots = 0; a.StatShots = 0; a.StatHits = 0;
            a.AmmoByWeapon.Clear();
            RefreshWeapon(a);
            a.Ammo = a.Mag;
            ApplyAdmin(a);
        }

        public void StartMission(int region)
        {
            Region = Mathf.Clamp(region, 0, Defs.Regions.Length - 1);
            Enemies.Clear(); Bullets.Clear(); Pickups.Clear(); Inputs.Clear(); BuffOffers.Clear();
            Wave = 0; WaveDelay = 1.8f; Elapsed = 0; Kills = 0;
            Level = 1; Xp = 0; XpNeed = Defs.XpForLevel(1); PendingLevels = 0; BuffPending = false;
            WaveGrades.Clear(); waveScores.Clear(); waveGraded = true; waveDamage = 0;
            foreach (var a in Actors.Values) ResetActor(a);
            State = MissionState.Playing;
        }

        // ------------------------------------------------------------ weapons

        public void RefreshWeapon(Actor a)
        {
            string id = a.WeaponId;
            var st = Defs.Weapons[id].StatsAt(a.Loadout.Level(id, "damage"), a.Loadout.Level(id, "mag"), a.Loadout.Level(id, "reload"));
            float adminDamage = a.IsAdmin ? a.Admin.Damage : 1f;
            a.Damage = st.Damage * a.DamageMult * adminDamage;
            a.FireInterval = Mathf.Max(0.045f, st.Interval * a.RateMult);
            a.Mag = st.Mag + a.MagBonus;
            a.ReloadTime = Mathf.Max(0.35f, st.Reload * a.ReloadMult);
            a.Pellets = st.Pellets;
            a.Spread = st.Spread;
            a.HeadMultiplier = st.HeadMultiplier;
            a.Zoom = st.Zoom;
            a.Ammo = Mathf.Min(a.Ammo, a.Mag);
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

        float EffectiveSpeed(Actor a) => a.Speed * (a.IsAdmin ? a.Admin.Speed : 1f);
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
                BeginBuffs();
                return;
            }
            Elapsed += dt;
            var hpBefore = new Dictionary<int, float>();
            foreach (var a in Actors.Values) hpBefore[a.Id] = a.Hp;
            foreach (var a in Actors.Values)
            {
                Inputs.TryGetValue(a.Id, out var input);
                MoveActor(a, input, dt);
                a.Ack = input.Seq;
                if (!a.Alive) continue;
                UpdateAmmo(a, input, dt);
                if (input.Fire && a.FireCooldown <= 0 && a.Reload <= 0)
                {
                    if (a.Ammo > 0)
                    {
                        a.FireCooldown = a.FireInterval;
                        a.Ammo--;
                        Vector3 aim = input.Aim == Vector3.zero ? a.Position + Vector3.up + LookDirection(a.Yaw, 0) * 30f : input.Aim;
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
                    if (Wave >= Defs.WavesPerMission)
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
            UpdateBullets(dt);
            UpdatePlayers(dt);
            UpdateEnemyKinds(dt);
            UpdateGrenades(dt);
            UpdatePickups(dt);
            foreach (var a in Actors.Values)
                if (hpBefore.TryGetValue(a.Id, out var before) && a.Hp < before && !(a.IsAdmin && a.Admin.God)) waveDamage += before - a.Hp;
            if (!waveGraded && Wave > 0 && Enemies.Count == 0) GradeWave();
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
                if (distance > e.Keep) e.Position += dir * e.Speed * dt;
                else if (distance < e.Keep - 2f) e.Position -= dir * e.Speed * 0.6f * dt;
                e.Position = PushOutOfCover(e.Position, e.Boss ? 1f : 0.5f);
                e.Position.x = Mathf.Clamp(e.Position.x, -Defs.ArenaHalf, Defs.ArenaHalf);
                e.Position.z = Mathf.Clamp(e.Position.z, -Defs.ArenaHalf, Defs.ArenaHalf);
                e.MeleeCooldown = Mathf.Max(0, e.MeleeCooldown - dt);
                if (distance < (e.Boss ? 2.4f : 1.7f) && e.MeleeCooldown <= 0 && !e.RangedOnly)
                {
                    e.MeleeCooldown = 0.85f;
                    target.Hp = Mathf.Max(0, target.Hp - e.Damage);
                }
            }
        }

        // ------------------------------------------------------------ waves

        public float LevelScale => 1f + 0.08f * (Level - 1);

        public void SpawnWave()
        {
            int players = Mathf.Max(1, Actors.Count);
            int count = Mathf.Min(24, Defs.Regions[Region].BaseEnemies + Mathf.CeilToInt(Wave / 2f) + (players - 1) * 2);
            int elapsedWaves = Mathf.Max(0, Wave - 1);
            float region = Region;
            float partyScale = 1f + 0.6f * (players - 1);
            float levelDamage = 1f + 0.05f * (Level - 1);
            var pool = new List<int>();
            if (Wave >= 2) pool.Add(Defs.KindCharger);
            if (Wave >= 3) pool.Add(Defs.KindBomber);
            if (Wave >= 4) pool.Add(Defs.KindSniper);
            if (Wave >= 5) pool.Add(Defs.KindShield);
            bool hasBoss = false;
            for (int i = 0; i < count; i++)
            {
                float angle = Mathf.PI * 2f * i / count + Wave * 0.7f;
                bool boss = (Wave == 10 || Wave == 20) && i == 0;
                var e = new Enemy { Id = nextEnemy++, Position = new Vector3(Mathf.Sin(angle) * 19f, 0, Mathf.Cos(angle) * 19f), Boss = boss, ShotCooldown = Rand(1.5f, 3.5f) };
                if (boss)
                {
                    hasBoss = true;
                    float baseHealth = Wave == 10 ? 3000f : 7000f;
                    e.Hp = baseHealth * (1f + 0.18f * region) * partyScale * LevelScale;
                    e.Damage = (20f + 3f * region) * (1f + 0.03f * elapsedWaves) * levelDamage;
                    e.Speed = 1.6f * (1f + 0.1f * region) * (1f + Mathf.Min(0.2f, 0.01f * elapsedWaves));
                    e.Kind = Wave >= 20 ? Defs.KindCommander : Defs.KindHeavyBoss;
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
                e.MaxHp = e.Hp;
                Enemies[e.Id] = e;
            }
            foreach (var a in Actors.Values)
                if (a.Alive) a.Hp = Mathf.Min(a.MaxHp, a.Hp + a.Regen);
            waveStart = Elapsed;
            waveDamage = 0;
            waveGraded = false;
            Announcements.Add(hasBoss ? "BOSS 來襲" : $"第 {Wave} 波");
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
                case Defs.KindBomber: e.Hp *= 0.45f; e.Speed *= 1.9f; e.Shoots = false; e.RangedOnly = true; e.Xp = baseXp; break;
                case Defs.KindShield: e.Hp *= 1.6f; e.Speed *= 0.85f; e.Xp = baseXp + 12; break;
                default: e.Xp = baseXp; break;
            }
            e.MaxHp = e.Hp;
        }

        Enemy NewEnemy(Vector3 position)
        {
            float region = Region;
            return new Enemy
            {
                Id = nextEnemy++, Position = position, Hp = (55f + 10f * region) * LevelScale, Speed = 2.1f + 0.3f * region,
                Damage = (10f + 2f * region) * (1f + 0.05f * (Level - 1)), ShotCooldown = Rand(1.5f, 3f),
            };
        }

        // ------------------------------------------------------------ player fire

        public void Fire(int id, Vector3 target)
        {
            var a = Actors[id];
            Vector3 origin = a.Position + Vector3.up * Defs.EyeHeight;
            Vector3 aim = (target - origin).normalized;
            float damage = a.Damage;
            if (Rand01() < a.Critical) damage *= 2f;
            a.StatShots++;
            int hitsBefore = a.StatHits;
            for (int i = 0; i < a.Pellets; i++)
            {
                Vector3 dir = aim;
                if (a.Spread > 0)
                    dir = (aim + new Vector3(Rand(-a.Spread, a.Spread), Rand(-a.Spread, a.Spread) * 0.7f, Rand(-a.Spread, a.Spread))).normalized;
                FireRay(a, origin, dir, damage);
            }
            if (a.StatHits > hitsBefore) a.StatHits = hitsBefore + 1;
        }

        void FireRay(Actor a, Vector3 origin, Vector3 dir, float damage)
        {
            Enemy best = null;
            float nearest = Defs.ShotRange;
            bool head = false;
            foreach (var e in Enemies.Values)
            {
                float s = e.Scale;
                var headCenter = e.Position + Vector3.up * Defs.HeadHeight * s;
                float headRadius = Defs.HeadRadius * s * (e.Boss ? 1.07f : 1f);
                var bodyCenter = e.Position + Vector3.up * s * (e.Boss ? 0.93f : 1f);
                float bodyRadius = e.Boss ? 0.64f * s : 0.75f * s;
                TestSphere(headCenter, headRadius, true);
                TestSphere(bodyCenter, bodyRadius, false);

                void TestSphere(Vector3 center, float radius, bool isHead)
                {
                    float along = Vector3.Dot(center - origin, dir);
                    if (along > 0 && along < nearest && Vector3.Distance(center, origin + dir * along) < radius)
                    {
                        best = e; nearest = along; head = isHead;
                    }
                }
            }
            Vector3 end = origin + dir * nearest;
            float dealt = 0;
            bool killed = false;
            if (best != null)
            {
                a.StatHits++;
                if (head) a.StatHeadshots++;
                dealt = damage * (head ? a.HeadMultiplier : 1f) * DamageMultiplier(best, dir, head);
                best.Hp -= dealt;
                if (best.Hp <= 0) { KillEnemy(best, a.Id); killed = true; }
            }
            Shots.Add(new ShotEvent { Shooter = a.Id, Origin = origin, End = end, Hit = best != null, Head = head, Dealt = dealt, Killed = killed });
        }

        /// Shield troopers block body shots from the front; flank them or aim for the head.
        public static float DamageMultiplier(Enemy e, Vector3 dir, bool head)
        {
            if (e.Kind == Defs.KindShield && !head && Vector3.Dot(dir, e.Facing) < -0.3f) return 0.25f;
            return 1f;
        }

        void KillEnemy(Enemy e, int shooter)
        {
            if (!Enemies.Remove(e.Id)) return;
            Kills++;
            GainXp(e.Boss ? 300 : e.Xp);
            if (Actors.TryGetValue(shooter, out var a) && a.Alive)
            {
                a.StatKills++;
                if (a.Lifesteal > 0) a.Hp = Mathf.Min(a.MaxHp, a.Hp + a.Lifesteal);
                if (a.Explode > 0) Explode(e.Position + Vector3.up, 3.2f, a.Explode, -1, new Color32(0xff, 0xb3, 0x47, 255));
            }
            if (e.Boss) SpawnPickup(e.Position, PickupKind.Health);
            else if (Rand01() < Defs.PickupChance) SpawnPickup(e.Position, Rand01() < 0.4f ? PickupKind.Health : PickupKind.Ammo);
        }

        void Explode(Vector3 position, float radius, float damage, int owner, Color32 color)
        {
            foreach (var e in Enemies.Values.ToList())
            {
                if (e.Hp <= 0 || !Enemies.ContainsKey(e.Id)) continue;
                float d = Vector3.Distance(e.Position + Vector3.up, position);
                if (d >= radius) continue;
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
                if (b.Kind == BulletKind.Grenade) continue;
                b.Position += b.Velocity * dt;
                b.Life -= dt;
                bool hit = false;
                foreach (var a in alive)
                {
                    Vector3 body = a.Position + Vector3.up * 0.9f;
                    if (Mathf.Abs(b.Position.y - body.y) < 1f && new Vector2(b.Position.x - body.x, b.Position.z - body.z).magnitude < Defs.BulletHitRadius)
                    {
                        a.Hp = Mathf.Max(0, a.Hp - b.Damage);
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
                            if (Vector3.Distance(a.Position, e.Position) < 3.2f) a.Hp = Mathf.Max(0, a.Hp - e.Damage * 2.2f);
                        Enemies.Remove(e.Id);
                        Explosions.Add(new ExplosionEvent { Position = e.Position + Vector3.up, Radius = 3.2f, Color = Defs.Kinds[Defs.KindBomber].Color });
                        continue;
                    }
                }
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

        // ------------------------------------------------------------ players: weapons, skill, revive, admin

        void UpdatePlayers(float dt)
        {
            foreach (var a in Actors.Values)
            {
                Inputs.TryGetValue(a.Id, out var input);
                a.SkillCooldown = Mathf.Max(0, a.SkillCooldown - dt);
                if (a.IsAdmin && a.Admin.God) a.Hp = a.MaxHp;
                if (a.IsAdmin && a.Admin.InfiniteAmmo && a.Alive) { a.Ammo = a.Mag; a.Reload = 0; }
                if (!a.Alive)
                {
                    bool helped = Actors.Values.Any(o => o.Id != a.Id && o.Alive && Vector3.Distance(o.Position, a.Position) < 2.4f &&
                                                         Inputs.TryGetValue(o.Id, out var oi) && oi.Interact);
                    a.Revive = Mathf.Clamp(a.Revive + (helped ? dt : -dt * 0.5f), 0, Defs.ReviveTime);
                    if (a.Revive >= Defs.ReviveTime) { a.Hp = a.MaxHp * 0.4f; a.Revive = 0; }
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

        public void SpawnPickup(Vector3 position, PickupKind kind)
        {
            var p = new Pickup { Id = nextPickup++, Position = new Vector3(position.x, 0, position.z), Kind = kind };
            Pickups[p.Id] = p;
        }

        void UpdatePickups(float dt)
        {
            foreach (var p in Pickups.Values.ToList())
            {
                p.Life -= dt;
                if (p.Life <= 0) { Pickups.Remove(p.Id); continue; }
                foreach (var a in Actors.Values)
                {
                    if (!a.Alive || new Vector2(a.Position.x - p.Position.x, a.Position.z - p.Position.z).magnitude > 1.4f) continue;
                    if (p.Kind == PickupKind.Health)
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
                XpNeed = Defs.XpForLevel(Level);
                PendingLevels++;
            }
        }

        void BeginBuffs()
        {
            BuffPending = true;
            BuffWait = 0;
            BuffOffers.Clear();
            foreach (var a in Actors.Values.Where(a => a.Alive)) BuffOffers[a.Id] = RollOffer();
            if (BuffOffers.Count == 0) ResolveBuffs();
        }

        int[] RollOffer()
        {
            var pool = Enumerable.Range(0, Defs.Buffs.Length).OrderBy(_ => rng.Next()).Take(3).ToArray();
            for (int i = 0; i < pool.Length; i++)
            {
                float roll = Rand01();
                int tier = roll < 0.06f ? 2 : roll < 0.32f ? 1 : 0;
                pool[i] += 10 * tier;
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
            int index = code % 10;
            float m = Defs.RarityMultiplier[Mathf.Clamp(code / 10, 0, 2)];
            switch (index)
            {
                case 0: a.DamageMult += 0.2f * m; break;
                case 1: a.RateMult = Mathf.Max(0.3f, a.RateMult * (1f - 0.15f * m)); break;
                case 2: a.Speed = Mathf.Min(12f, a.Speed * (1f + 0.12f * m)); break;
                case 3:
                    float bonus = Mathf.Round(30 * m);
                    a.MaxHp += bonus;
                    a.Hp = Mathf.Min(a.MaxHp, a.Hp + bonus);
                    break;
                case 4: a.Regen += Mathf.Round(15 * m); break;
                case 5: a.Critical = Mathf.Min(0.8f, a.Critical + 0.2f * m); break;
                case 6:
                    a.MagBonus += Mathf.RoundToInt(4 * m);
                    a.ReloadMult = Mathf.Max(0.4f, a.ReloadMult * 0.85f);
                    break;
                case 7: a.SkillMult = Mathf.Max(0.35f, a.SkillMult * (1f - 0.2f * m)); break;
                case 8: a.Lifesteal += Mathf.Round(4 * m); break;
                case 9: a.Explode += Mathf.Round(35 * m); break;
            }
            RefreshWeapon(a);
            if (index == 6 && a.Reload <= 0) a.Ammo = a.Mag;
        }

        public void ResolveBuffs()
        {
            BuffPending = false;
            BuffOffers.Clear();
            WaveDelay = Mathf.Min(WaveDelay, 1f);
        }

        // ------------------------------------------------------------ rewards

        public int MissionGold() =>
            Mathf.Clamp(Kills * 3 + Wave * 12 + (Level - 1) * 15 + (State == MissionState.Victory ? 200 + 150 * Region : 0), 0, 3000);
    }
}
