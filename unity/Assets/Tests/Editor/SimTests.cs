using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RiftWard.Sim;
using UnityEngine;

namespace RiftWard.Tests
{
    public class SimTests
    {
        static GameSim NewMission(Loadout loadout = null, bool admin = false)
        {
            var sim = new GameSim(1234) { EnemyVariety = false };
            sim.AddActor(1, loadout, admin);
            sim.StartMission(0);
            return sim;
        }

        static Enemy PlaceEnemy(GameSim sim, Vector3 offset, float hp = 1000f, int kind = Defs.KindGrunt, float yaw = 0f)
        {
            var e = new Enemy { Id = 900 + sim.Enemies.Count, Position = sim.Actors[1].Position + offset, Hp = hp, MaxHp = hp, Yaw = yaw };
            sim.ApplyKind(e, kind);
            e.Hp = e.MaxHp = hp;
            e.Shoots = false;
            e.MeleeCooldown = float.MaxValue;
            sim.Enemies[e.Id] = e;
            return e;
        }

        [Test]
        public void GearSkillDamageScalesGadgetsButNotGuns()
        {
            float Lightning(float gadgetPct)
            {
                var sim = NewMission(); sim.Enemies.Clear(); sim.WaveDelay = 999;
                var e = PlaceEnemy(sim, new Vector3(0, 0, 6), 10000);
                sim.ApplyProfileBonus(1, 0, 0, 0, 0, 0, 0, 0, gadgetPct);
                sim.Actors[1].LightningLevel = 1;
                sim.Tick(0.02f);
                return 10000 - e.Hp;
            }
            Assert.AreEqual(Lightning(0) * 1.5f, Lightning(50), 0.5f, "+50% 技能傷害 = 1.5x lightning");
            var gun = NewMission(); gun.Enemies.Clear(); gun.WaveDelay = 999;
            gun.ApplyProfileBonus(1, 0, 0, 0, 0, 0, 0, 0, 50);
            var plain = NewMission();
            Assert.AreEqual(plain.Actors[1].Damage, gun.Actors[1].Damage, 0.001f, "the gun is untouched");
        }

        [Test]
        public void PvpPlayersShootEachOtherRespawnAndTheClockDecides()
        {
            var sim = new GameSim(5);
            var lo = new Loadout { Owned = new System.Collections.Generic.List<string> { "pistol", "sniper" }, Equipped = new System.Collections.Generic.List<string> { "sniper", "pistol" } };
            var a = sim.AddActor(1, lo); a.Name = "A";
            var b = sim.AddActor(2, new Loadout()); b.Name = "B";
            sim.StartPvp();
            Assert.IsTrue(sim.Pvp);
            Assert.AreEqual(0, sim.Enemies.Count);
            Assert.AreEqual(Defs.PvpHp, b.MaxHp);
            // Face to face, 10 m apart, nothing in between.
            a.Position = new Vector3(0, 0, -5); b.Position = new Vector3(0, 0, 5);
            for (int i = 0; i < 100; i++) sim.Tick(0.02f);   // no waves ever spawn
            Assert.AreEqual(0, sim.Enemies.Count);
            var bodyShot = b.Position + Vector3.up * 1.1f;
            sim.Fire(1, bodyShot);
            Assert.AreEqual(Defs.PvpHp - a.Damage, b.Hp, 0.01f, "body shot, no burst cap in PvP");
            // A headshot finishes them: kill and death counted, kill feed line, respawn timer.
            a.FireCooldown = 0;
            sim.Fire(1, b.Position + Vector3.up * Defs.EyeHeight);
            Assert.IsFalse(b.Alive);
            Assert.AreEqual(1, a.StatKills);
            Assert.AreEqual(1, b.StatDowns);
            Assert.IsTrue(sim.Announcements.Any(t => t.Contains("擊殺")));
            for (int i = 0; i < Mathf.CeilToInt(Defs.PvpRespawn / 0.02f) + 5; i++) sim.Tick(0.02f);
            Assert.IsTrue(b.Alive, "back after PvpRespawn");
            Assert.AreEqual(b.MaxHp, b.Hp);
            Assert.AreEqual(1, b.StatDowns, "a respawn is not another death");
            Assert.AreEqual(MissionState.Playing, sim.State);
            // Standings and the end of the clock.
            Assert.AreEqual(1, sim.PvpStandings()[0].Id);
            sim.PvpTimeLeft = 0.01f;
            sim.Tick(0.02f);
            Assert.AreEqual(MissionState.Victory, sim.State);
        }

        [Test]
        public void DashLeavesAWindWakeThatSlowsEnemies()
        {
            var sim = NewMission(); sim.Enemies.Clear(); sim.WaveDelay = 999;
            var a = sim.Actors[1];
            sim.Inputs[1] = new PlayerInput { Move = new Vector2(0, 1), Dash = true };
            sim.Tick(0.02f);
            var wake = sim.Bullets.Values.Single(b => b.Kind == BulletKind.Wake);
            Assert.AreEqual(Defs.DashDistance, wake.Velocity.magnitude, 0.01f);
            // An enemy standing on the path is slowed; one far to the side is not.
            var onPath = PlaceEnemy(sim, new Vector3(0, 0, -2f));
            var aside = PlaceEnemy(sim, new Vector3(8f, 0, 0));
            onPath.Position = wake.Position + wake.Velocity * 0.5f; onPath.Position.y = 0;
            sim.Inputs[1] = new PlayerInput();
            sim.Tick(0.02f);
            Assert.Greater(onPath.SlowTime, 0f);
            Assert.AreEqual(0f, aside.SlowTime);
            for (int i = 0; i < 200; i++) sim.Tick(0.02f);
            Assert.IsFalse(sim.Bullets.Values.Any(b => b.Kind == BulletKind.Wake), "the wake fades after WakeLife");
        }

        [Test]
        public void TornadoThrowsEnemiesUpAndHurtsThem()
        {
            var sim = NewMission(); sim.Enemies.Clear(); sim.WaveDelay = 999;
            var a = sim.Actors[1];
            var e = PlaceEnemy(sim, new Vector3(0, 0, 6f), 5000);
            e.Shoots = true;
            sim.Inputs[1] = new PlayerInput { Ult = true };
            sim.Tick(0.02f);
            Assert.AreEqual(Defs.UltCooldown, a.UltCooldown, 0.05f);
            Assert.AreEqual(1, sim.Bullets.Values.Count(b => b.Kind == BulletKind.Tornado));
            sim.Inputs[1] = new PlayerInput();
            for (int i = 0; i < 60 && e.AirTime <= 0; i++) sim.Tick(0.02f);
            Assert.Greater(e.AirTime, 0f, "the tornado reaches the enemy and throws it");
            Assert.Less(e.Hp, 5000f);
            sim.Tick(0.3f);
            Assert.Greater(e.Position.y, 0.5f, "airborne");
            float hp = e.Hp;
            for (int i = 0; i < 40; i++) sim.Tick(0.02f);
            Assert.AreEqual(hp, e.Hp, 0.01f, "each enemy is hit once per tornado");
            // Nothing in range: X does nothing and spends no cooldown.
            var empty = NewMission(); empty.Enemies.Clear(); empty.WaveDelay = 999;
            empty.Inputs[1] = new PlayerInput { Ult = true };
            empty.Tick(0.02f);
            Assert.AreEqual(0f, empty.Actors[1].UltCooldown);
        }

        [Test]
        public void HeavyHitsAreCappedSoNobodyIsOneShot()
        {
            var sim = NewMission();
            var a = sim.Actors[1];
            PlaceEnemy(sim, new Vector3(0, 0, 30f));   // keeps the wave from ending
            Vector3 chest = a.Position + Vector3.up * 1.2f;
            // A hit worth ten times max life lands as at most HitCap of it.
            sim.SpawnBullet(chest, Vector3.zero, a.MaxHp * 10f, BulletKind.Enemy);
            sim.Tick(0.02f);
            Assert.IsTrue(a.Alive);
            Assert.AreEqual(a.MaxHp * (1f - Defs.HitCap), a.Hp, 0.5f);
            // A second one right after only fills the burst bucket up to BurstCap.
            sim.SpawnBullet(chest, Vector3.zero, a.MaxHp * 10f, BulletKind.Enemy);
            sim.Tick(0.02f);
            Assert.AreEqual(a.MaxHp * (1f - Defs.BurstCap), a.Hp, 1f);
            // Over one full second from full life no more than BurstCap + BurstDrain is lost.
            for (int i = 0; i < 50; i++) { sim.SpawnBullet(chest, Vector3.zero, a.MaxHp * 10f, BulletKind.Enemy); sim.Tick(0.02f); }
            Assert.IsTrue(a.Alive);
            Assert.GreaterOrEqual(a.Hp, a.MaxHp * (1f - Defs.BurstCap - Defs.BurstDrain) - 1f);
        }

        [Test]
        public void PartyWaveCountsScaleWithPlayersAcrossChaptersAndTiers()
        {
            for (int region = 0; region < Defs.Regions.Length; region++)
            for (int tier = 0; tier < Defs.Difficulties.Length; tier++)
            foreach (int wave in new[] { 1, 10, 20, 100 })
            {
                int solo = 0;
                for (int players = 1; players <= 4; players++)
                {
                    var sim = new GameSim(42);
                    for (int id = 1; id <= players; id++) sim.AddActor(id, new Loadout());
                    sim.StartMission(region, tier);
                    sim.Endless = wave > Defs.WavesPerMission;
                    sim.Wave = wave;
                    sim.SpawnWave();
                    if (players == 1) solo = sim.Enemies.Count;
                    Assert.AreEqual(solo * players, sim.Enemies.Count, $"region={region} tier={tier} wave={wave} players={players}");
                    Assert.AreEqual(wave % 10 == 0 ? 1 : 0, sim.Enemies.Values.Count(e => e.Boss));
                }
            }
        }

        [Test]
        public void NextWaveUsesRemainingPartySizeIncludingDownedMembers()
        {
            var sim = NewMission();
            sim.Wave = 1;
            sim.SpawnWave();
            int solo = sim.Enemies.Count;
            for (int id = 2; id <= 4; id++) sim.AddActor(id, new Loadout());
            sim.Actors[4].Hp = 0;
            sim.Enemies.Clear();
            sim.SpawnWave();
            Assert.AreEqual(solo * 4, sim.Enemies.Count);
            sim.RemoveActor(3);
            sim.Enemies.Clear();
            sim.SpawnWave();
            Assert.AreEqual(solo * 3, sim.Enemies.Count);
        }

        [Test]
        public void ProfileBonusSurvivesMissionStart()
        {
            // Online order: the server adds players and applies talents + gear before StartMission resets stats.
            var sim = new GameSim(1234) { EnemyVariety = false };
            sim.AddActor(1, new Loadout(), false);
            sim.ApplyProfileBonus(1, 20f, 10f, 5f, 8f, 4f);
            sim.StartMission(0);
            var a = sim.Actors[1];
            Assert.AreEqual(120f, a.MaxHp, 0.001f);
            Assert.AreEqual(120f, a.Hp, 0.001f);
            Assert.AreEqual(1.1f, a.DamageMult, 0.001f);
            Assert.AreEqual(Defs.BaseSpeed * 1.05f, a.Speed, 0.001f);
            Assert.AreEqual(0.92f, a.ReloadMult, 0.001f);
            Assert.AreEqual(0.04f, a.Critical, 0.001f);
            // Offline order (bonus after start) gives the same result, not a doubled one.
            var solo = NewMission(new Loadout());
            solo.ApplyProfileBonus(1, 20f, 10f, 5f, 8f, 4f);
            Assert.AreEqual(120f, solo.Actors[1].MaxHp, 0.001f);
            Assert.AreEqual(1.1f, solo.Actors[1].DamageMult, 0.001f);
        }

        [Test]
        public void CritCapsAtHalfAndInfernoScalesEnemies()
        {
            var sim = NewMission(new Loadout());
            sim.ApplyProfileBonus(1, 0f, 0f, 0f, 0f, 60f);
            Assert.AreEqual(Defs.MaxCritical, sim.Actors[1].Critical, 0.001f);
            Assert.AreEqual(0.5f, Defs.MaxCritical, 0.001f);
            // 地獄: the fourth tier, opened by clearing 噩夢 in that region.
            Assert.AreEqual(4, Defs.Difficulties.Length);
            Assert.AreEqual(6f, Defs.Difficulty(3).Hp, 0.001f);
            Assert.IsFalse(Defs.DifficultyOpen(new[] { 1 }, 0, 3));
            Assert.IsTrue(Defs.DifficultyOpen(new[] { 2 }, 0, 3));
            var normal = new GameSim(7) { EnemyVariety = false }; normal.AddActor(1, new Loadout(), false); normal.StartMission(0, 0);
            var inferno = new GameSim(7) { EnemyVariety = false }; inferno.AddActor(1, new Loadout(), false); inferno.StartMission(0, 3);
            normal.SpawnWave(); inferno.SpawnWave();
            float n = float.MaxValue, i = float.MaxValue;   // the weakest one: an elite (×4) may be promoted
            foreach (var e in normal.Enemies.Values) n = Mathf.Min(n, e.MaxHp);
            foreach (var e in inferno.Enemies.Values) i = Mathf.Min(i, e.MaxHp);
            Assert.AreEqual(n * Defs.Difficulty(3).Hp, i, 0.01f);
            Assert.AreEqual(normal.Enemies.Count + Defs.Difficulty(3).ExtraEnemies, inferno.Enemies.Count);
        }

        [Test]
        public void PistolIsStarterAndUpgradesApply()
        {
            var lo = new Loadout();
            lo.Levels["pistol"] = new System.Collections.Generic.Dictionary<string, int> { ["damage"] = 2, ["mag"] = 1 };
            var sim = NewMission(lo);
            var a = sim.Actors[1];
            Assert.AreEqual("pistol", a.WeaponId);
            Assert.AreEqual(28f * 1.24f, a.Damage, 0.001f);
            Assert.AreEqual(14, a.Mag);
        }

        [Test]
        public void DronesAndPlasmaFireOnCooldownAndUpgrade()
        {
            var sim = NewMission();
            var a = sim.Actors[1];
            sim.ApplyBuff(1, Defs.BuffDrone);
            sim.ApplyBuff(1, Defs.BuffDrone + 200);
            Assert.AreEqual(3, a.Drones);
            sim.ApplyBuff(1, Defs.BuffDroneRate);
            Assert.Less(a.DroneRateMult, 1f);
            var e = PlaceEnemy(sim, new Vector3(0, 0, 8), 5000f);
            sim.Enemies.Keys.Where(k => k != e.Id).ToList().ForEach(k => sim.Enemies.Remove(k));
            sim.Tick(0.02f);
            float afterFirst = e.Hp;
            Assert.Less(afterFirst, 5000f);
            Assert.IsTrue(sim.Shots.Any(s => s.Shooter == GameSim.GadgetShooter(1)));
            sim.Shots.Clear();
            sim.Tick(0.02f);
            Assert.AreEqual(afterFirst, e.Hp, 0.001f, "drones must wait for their cooldown");
            sim.ApplyBuff(1, Defs.BuffPlasma);
            Assert.IsTrue(a.Plasma);
            sim.ApplyBuff(1, Defs.BuffPlasma);
            Assert.Greater(a.PlasmaDamageMult, 1f);
        }

        [Test]
        public void KillsDropXpCrystalsThatTheMagnetCollects()
        {
            var sim = NewMission();
            var a = sim.Actors[1];
            sim.Enemies.Clear();
            var e = PlaceEnemy(sim, new Vector3(0, 0, 6), 1f);
            var keep = PlaceEnemy(sim, new Vector3(15, 0, 15), 99999f);
            int xp = sim.Xp;
            sim.Fire(1, e.Position + Vector3.up);
            var crystal = sim.Pickups.Values.Single(p => p.Kind == PickupKind.Xp);
            Assert.AreEqual(xp, sim.Xp, "XP waits until the crystal is picked up");
            a.Magnet = 8f;
            for (int i = 0; i < 60 && sim.Pickups.ContainsKey(crystal.Id); i++) sim.Tick(0.05f);
            Assert.IsFalse(sim.Pickups.ContainsKey(crystal.Id));
            Assert.Greater(sim.Xp + sim.Level * 1000, xp + 1000 - 1);
        }

        [Test]
        public void NewGadgetsLevelUpAndEvolve()
        {
            var sim = NewMission();
            var a = sim.Actors[1];
            foreach (var b in new[] { Defs.BuffOrbit, Defs.BuffMine, Defs.BuffMissile, Defs.BuffLightning }) sim.ApplyBuff(1, b + 200);
            Assert.AreEqual(2, a.OrbitLevel);
            Assert.AreEqual(2, a.LightningLevel);
            sim.Enemies.Clear();
            var e = PlaceEnemy(sim, new Vector3(0, 0, 9), 99999f);
            sim.Tick(0.02f);
            Assert.Less(e.Hp, 99999f, "lightning strikes the nearest enemy");
            Assert.IsTrue(sim.Shots.Any(s => s.Shooter < 0 && GameSim.GadgetType(s.Shooter) == GameSim.GadgetLightning && GameSim.GadgetOwner(s.Shooter) == 1));
            for (int i = 0; i < 40; i++) sim.Tick(0.05f);
            Assert.IsTrue(sim.Bullets.Values.Any(b => b.Kind == BulletKind.Mine));
            sim.ApplyBuff(1, Defs.BuffDrone + 200);
            sim.ApplyBuff(1, Defs.BuffDrone + 200);
            Assert.IsFalse(a.DroneEvolved);
            sim.ApplyBuff(1, Defs.BuffDroneDamage + 100);
            sim.ApplyBuff(1, Defs.BuffDroneDamage + 100);
            Assert.IsTrue(a.DroneEvolved);
        }

        [Test]
        public void ArmorBuffReducesIncomingDamage()
        {
            var sim = NewMission();
            var a = sim.Actors[1];
            sim.Enemies.Clear();
            sim.ApplyBuff(1, Defs.BuffArmor + 200);
            Assert.AreEqual(0.2f, a.Armor, 0.001f);
            float hp = a.Hp;
            sim.Bullets[1] = new Bullet { Id = 1, Position = a.Position + Vector3.up * 0.9f, Velocity = Vector3.zero, Damage = 30f, Kind = BulletKind.Enemy };   // under the per-hit cap
            sim.Enemies[999] = new Enemy { Id = 999, Position = a.Position + new Vector3(15, 0, 15), Hp = 9999, MaxHp = 9999, Shoots = false, MeleeCooldown = 99f };
            sim.Tick(0.01f);
            Assert.AreEqual(hp - 24f, a.Hp, 0.01f);
            for (int i = 0; i < 10; i++) sim.ApplyBuff(1, Defs.BuffArmor + 200);
            Assert.AreEqual(Defs.MaxArmor, a.Armor, 0.001f);
        }

        [Test]
        public void ArmorNeverLeavesALethalHitAtASliver()
        {
            // Armour used to scale the clamped hit (health → 0) back up to before × armour, so a player with armour
            // could sit at a fraction of a point forever and never go down.
            var sim = NewMission();
            var a = sim.Actors[1];
            sim.Enemies.Clear();
            for (int i = 0; i < 10; i++) sim.ApplyBuff(1, Defs.BuffArmor + 200);
            sim.Enemies[999] = new Enemy { Id = 999, Position = a.Position + new Vector3(15, 0, 15), Hp = 9999, MaxHp = 9999, Shoots = false, MeleeCooldown = 99f };
            a.Hp = 10f;
            sim.Bullets[1] = new Bullet { Id = 1, Position = a.Position + Vector3.up * 0.9f, Velocity = Vector3.zero, Damage = 50f, Kind = BulletKind.Enemy };
            sim.Tick(0.01f);
            Assert.IsFalse(a.Alive, "50 damage at 60% armour is 20, more than the 10 left");
            Assert.AreEqual(0f, a.Hp);
        }

        [Test]
        public void HeadHitsOnBigEnemiesCountAsHeadshots()
        {
            // An elite (scale × 1.35) up close: the shot climbs to the head, and the big body sphere's centre lies
            // nearer along the ray. The head must still win.
            var sim = NewMission();
            var e = PlaceEnemy(sim, new Vector3(0, 0, 3.5f), 5000f);
            e.Elite = true;
            e.Scale *= 1.35f;
            sim.Fire(1, e.Position + Vector3.up * Defs.HeadHeight * e.Scale);
            Assert.IsTrue(sim.Shots.Last().Head);
        }

        [Test]
        public void EndlessDamageOverflowIsLinearAndXpGrowsGently()
        {
            var sim = NewMission();
            sim.Endless = true;
            var a = sim.Actors[1];
            float start = a.DamageMult;
            for (int i = 0; i < Defs.BuffMaxStacks[0]; i++) sim.ApplyBuff(1, 0);
            Assert.AreEqual(start + 0.2f * Defs.BuffMaxStacks[0], a.DamageMult, 0.001f, "normal stacks add 20%");
            float capped = a.DamageMult;
            sim.ApplyBuff(1, 0);
            Assert.AreEqual(capped + Defs.EndlessDamagePick, a.DamageMult, 0.001f, "overflow adds a fixed step");
            Assert.AreEqual(Mathf.RoundToInt(Defs.XpForLevel(Defs.EndlessXpLevelCap) * 1.25f), Defs.XpNeed(Defs.EndlessXpLevelCap + 10, true));
            Assert.AreEqual(Defs.XpForLevel(30), Defs.XpNeed(30, false), "regular missions keep the full curve");
        }

        [Test]
        public void ShieldTalentScalesBaseAndEveryShieldPick()
        {
            var sim = NewMission();
            var a = sim.Actors[1];
            sim.ApplyProfileBonus(1, 20f, 0, 0, 0, 0, 30f);   // gear +20 flat, talent +30%
            Assert.AreEqual((100f + 20f) * 1.3f, a.MaxHp, 0.01f);
            float before = a.MaxHp;
            sim.ApplyBuff(1, 3);   // 強化護盾, common: +30 × 1.3
            Assert.AreEqual(before + Mathf.Round(30f * 1.3f), a.MaxHp, 0.01f);
        }

        [Test]
        public void GearDefenseIsItsOwnLayerOutsideTheArmourCap()
        {
            var sim = NewMission();
            var a = sim.Actors[1];
            sim.Enemies.Clear();
            sim.ApplyProfileBonus(1, 0, 0, 0, 0, 0, 0, 22f);
            Assert.AreEqual(0.22f, a.GearDefense, 1e-4f);
            for (int i = 0; i < 10; i++) sim.ApplyBuff(1, Defs.BuffArmor + 200);
            Assert.AreEqual(Defs.MaxArmor, a.Armor, 1e-4f);
            sim.Enemies[999] = new Enemy { Id = 999, Position = a.Position + new Vector3(15, 0, 15), Hp = 9999, MaxHp = 9999, Shoots = false, MeleeCooldown = 99f };
            a.Hp = a.MaxHp;
            float hp = a.Hp;
            sim.Bullets[1] = new Bullet { Id = 1, Position = a.Position + Vector3.up * 0.9f, Velocity = Vector3.zero, Damage = 100f, Kind = BulletKind.Enemy };
            sim.Tick(0.01f);
            Assert.AreEqual(hp - 100f * 0.4f * 0.78f, a.Hp, 0.05f, "60% armour, then 22% gear defense");
        }

        [Test]
        public void EndlessLifeOverflowIsFlat()
        {
            var sim = NewMission();
            sim.Endless = true;
            var a = sim.Actors[1];
            for (int i = 0; i < Defs.BuffMaxStacks[3]; i++) sim.ApplyBuff(1, 3);
            Assert.AreEqual(100f + 30f * Defs.BuffMaxStacks[3], a.MaxHp, 0.01f, "normal stacks add 30");
            float capped = a.MaxHp;
            sim.ApplyBuff(1, 3);
            Assert.AreEqual(capped + Defs.EndlessLifePick, a.MaxHp, 0.01f, "overflow grants flat life");
        }

        [Test]
        public void ASurroundingCrowdLandsAtMostThreeMeleeHitsAtOnce()
        {
            var sim = NewMission();
            var a = sim.Actors[1];
            sim.Enemies.Clear();
            a.MaxHp = a.Hp = 10000f;
            for (int i = 0; i < 8; i++)
            {
                float ang = i * Mathf.PI / 4f;
                var e = PlaceEnemy(sim, new Vector3(Mathf.Cos(ang) * 1.0f, 0, Mathf.Sin(ang) * 1.0f), 9999f);
                e.Damage = 10f; e.MeleeCooldown = 0f; e.Speed = 0f;
            }
            sim.Tick(0.02f);
            Assert.AreEqual(10000f - 30f, a.Hp, 0.01f, "three of the eight swings land");
            sim.Tick(0.02f);
            Assert.AreEqual(10000f - 30f, a.Hp, 0.01f, "the rest wait for a free slot");
            for (int i = 0; i < 45; i++) sim.Tick(0.02f);   // past one melee interval
            Assert.AreEqual(10000f - 60f, a.Hp, 0.01f, "then three more");
        }

        [Test]
        public void MinigunSpinsUpHeatsUpAndOverheats()
        {
            var kit = new Loadout { Owned = new List<string> { "minigun" }, Equipped = new List<string> { "minigun" } };
            var sim = new GameSim(5) { EnemyVariety = false };
            var a = sim.AddActor(1, kit);
            sim.StartMission(0);
            sim.WaveDelay = 999f;
            sim.Enemies.Clear();
            Assert.AreEqual("minigun", a.WeaponId);
            a.MagBonus = 1000; sim.RefreshWeapon(a); a.Ammo = a.Mag;   // no reloads in this test
            int start = a.Ammo;
            sim.Inputs[1] = new PlayerInput { Fire = true, Yaw = 0 };
            for (int i = 0; i < 5; i++) sim.Tick(0.02f);                 // 0.1 s: still spinning up
            Assert.AreEqual(start, a.Ammo, "no shot before the barrels reach the minimum spin");
            for (int i = 0; i < 40; i++) sim.Tick(0.02f);                // to full spin
            Assert.AreEqual(1f, a.Spin, 1e-3f);
            int fired = start - a.Ammo;
            Assert.Greater(fired, 0);
            Assert.Greater(a.Heat, 0f);
            // Hot barrels hit harder.
            var target = PlaceEnemy(sim, new Vector3(0, 0, 10), 100000f);
            a.Heat = 0f; sim.Fire(1, target.Position + Vector3.up * 0.8f); float cold = 100000f - target.Hp;
            a.Heat = 1f; float before = target.Hp; sim.Fire(1, target.Position + Vector3.up * 0.8f); float hot = before - target.Hp;
            Assert.AreEqual(cold * (1f + Defs.MinigunHeatBonus), hot, 0.01f);
            sim.Enemies.Clear();
            // Keep firing: it overheats, then the gun is locked.
            a.Heat = 0.99f;
            for (int i = 0; i < 10; i++) sim.Tick(0.02f);
            Assert.Greater(a.Overheat, 0f, "full heat locks the gun");
            int locked = a.Ammo;
            for (int i = 0; i < 20; i++) sim.Tick(0.02f);
            Assert.AreEqual(locked, a.Ammo, "no shots while overheated");
            for (int i = 0; i < 120; i++) sim.Tick(0.02f);
            Assert.AreEqual(0f, a.Overheat, 1e-4f);
            Assert.Less(a.Ammo, locked, "fires again once cooled");
            // Firing slows the walk.
            var walker = new Actor { Speed = 10f, Hp = 100f, MaxHp = 100f, Loadout = kit };
            var p0 = walker.Position;
            GameSim.MoveActor(walker, new PlayerInput { Move = Vector2.up, Fire = true }, 0.1f, 10f, false);
            var walked = (walker.Position - p0).magnitude;
            var free = new Actor { Speed = 10f, Hp = 100f, MaxHp = 100f, Loadout = kit };
            GameSim.MoveActor(free, new PlayerInput { Move = Vector2.up }, 0.1f, 10f, false);
            Assert.AreEqual(free.Position.magnitude * Defs.MinigunFireSpeed, walked, 0.01f);
        }

        [Test]
        public void BotFightsTheNearestEnemyAndFollowsTeammatesWhenClear()
        {
            var sim = new GameSim(7);
            var bot = sim.AddActor(1, new Loadout(), false);
            var mate = sim.AddActor(2, new Loadout(), false);
            sim.StartMission(0);
            sim.Enemies.Clear();
            bot.Position = Vector3.zero;
            mate.Position = new Vector3(20, 0, 0);
            var follow = sim.BotInput(bot, 5);
            Assert.AreEqual(5, follow.Seq, "keeps the player's own input sequence");
            Assert.IsFalse(follow.Fire);
            Assert.Greater(follow.Move.y, 0, "walks toward the teammate");
            Assert.AreEqual(Mathf.PI / 2, follow.Yaw, 0.01f);
            var e = new Enemy { Id = 900, Position = new Vector3(0, 0, 12), Hp = 50, MaxHp = 50, Scale = 1 };
            sim.Enemies[e.Id] = e;
            var fight = sim.BotInput(bot, 6);
            Assert.IsTrue(fight.Fire);
            Assert.AreEqual(0f, fight.Yaw, 0.01f, "faces the enemy");
            Assert.AreEqual(e.Position + Vector3.up, fight.Aim);
        }

        [Test]
        public void ConstellationAndSkillTalentsChangeCombatNumbers()
        {
            var lo = new Loadout { Owned = new List<string> { "rifle" }, Equipped = new List<string> { "rifle" } };
            var sim = new GameSim(3) { EnemyVariety = false };
            var a = sim.AddActor(1, lo, false);
            sim.StartMission(0);
            float baseDamage = a.Damage, baseInterval = a.FireInterval; int baseMag = a.Mag;
            lo.Constellation["rifle"] = 6;
            sim.RefreshWeapon(a);
            Assert.AreEqual(baseDamage * 1.24f, a.Damage, 0.01f);
            Assert.AreEqual(Mathf.RoundToInt(Defs.Weapons["rifle"].Mag * 1.15f), a.Mag);
            Assert.Greater(a.Mag, baseMag);
            Assert.AreEqual(baseInterval * 0.9f, a.FireInterval, 0.0001f);
            sim.ApplySkillBonus(1, 30, 15, 20);
            sim.ApplySkillBonus(1, 30, 15, 20);   // applying twice must not stack the cooldown cut
            Assert.AreEqual(1.3f, a.SkillDamageMult, 1e-4);
            Assert.AreEqual(0.85f, a.SkillMult, 1e-4);
            Assert.AreEqual(1.2f, a.SkillRadiusMult, 1e-4);
            sim.StartMission(0);   // a fresh mission keeps the account bonus
            Assert.AreEqual(0.85f, a.SkillMult, 1e-4);
        }

        [Test]
        public void ShieldPlateSoaksFrontalHitsThenShatters()
        {
            var e = new Enemy { Kind = Defs.KindShield, MaxHp = 100, Hp = 100, Yaw = 0 };   // facing +z
            var front = new Vector3(0, 0, -1);   // a round travelling toward its face
            Assert.AreEqual(0.25f, GameSim.DamageMultiplier(e, front, false), 1e-4);
            Assert.AreEqual(1f, GameSim.DamageMultiplier(e, -front, false), 1e-4, "from behind: no plate");
            GameSim.Chip(e, 40, -front, false);
            Assert.IsTrue(e.ShieldHp < 0, "hits from behind never touch the plate");
            GameSim.Chip(e, 40, front, false);
            Assert.AreEqual(100 * Defs.ShieldPlateShare - 40, e.ShieldHp, 1e-3);
            Assert.IsFalse(e.ShieldBroken);
            GameSim.Chip(e, 40, front, false);
            Assert.IsTrue(e.ShieldBroken);
            Assert.AreEqual(1f, GameSim.DamageMultiplier(e, front, false), 1e-4, "a shattered plate blocks nothing");
        }

        [Test]
        public void ClearingTheFinalWaveWinsAndLeftoverRoundsVanish()
        {
            // A dead boss's last volley used to land during the victory delay and could turn a win into a defeat.
            var sim = NewMission();
            var a = sim.Actors[1];
            sim.Wave = Defs.WavesPerMission;
            sim.Enemies.Clear();
            a.Hp = 5f;
            sim.Bullets[1] = new Bullet { Id = 1, Position = a.Position + Vector3.up * 0.9f, Velocity = Vector3.zero, Damage = 999f, Kind = BulletKind.Enemy };
            sim.Bullets[2] = new Bullet { Id = 2, Position = a.Position + new Vector3(3, 1, 0), Velocity = Vector3.zero, Damage = 999f, Kind = BulletKind.Slam };
            sim.Tick(0.02f);
            Assert.AreEqual(MissionState.Victory, sim.State);
            Assert.IsTrue(a.Alive);
            Assert.AreEqual(0, sim.Bullets.Count);
        }

        [Test]
        public void EmptyMagazineAutoReloads()
        {
            var sim = NewMission();
            var a = sim.Actors[1];
            a.Ammo = 1;
            sim.Inputs[1] = new PlayerInput { Fire = true, Aim = a.Position + new Vector3(0, 1, 30) };
            sim.Tick(0.01f);
            Assert.AreEqual(0, a.Ammo);
            Assert.Greater(a.Reload, 0f);
            sim.Inputs[1] = new PlayerInput();
            sim.Tick(2f);
            Assert.AreEqual(a.Mag, a.Ammo);
        }

        [Test]
        public void EveryUpgradeHasAStackLimitExceptEndlessDamageAndShield()
        {
            Assert.AreEqual(Defs.Buffs.Length, Defs.BuffMaxStacks.Length);
            var sim = NewMission();
            var a = sim.Actors[1];
            for (int i = 0; i < Defs.BuffMaxStacks[0]; i++) sim.ApplyBuff(1, 0);
            for (int n = 0; n < 300; n++)
            {
                sim.PendingLevels = 1;
                sim.Tick(0.02f);
                if (sim.BuffOffers.TryGetValue(1, out var offer))
                {
                    Assert.IsFalse(offer.Any(c => Defs.BuffIndex(c) == 0), "maxed 超載彈芯 is not offered again");
                    sim.ChooseBuff(1, offer[0]);
                }
            }
            for (int i = 0; i < Defs.Buffs.Length; i++)
                Assert.LessOrEqual(a.BuffStacks[i], Defs.BuffMaxStacks[i], Defs.Buffs[i].Name);
            // Endless: damage and shield keep stacking.
            Assert.AreEqual(int.MaxValue, Defs.BuffStackLimit(0, true));
            Assert.AreEqual(int.MaxValue, Defs.BuffStackLimit(3, true));
            Assert.AreEqual(Defs.BuffMaxStacks[1], Defs.BuffStackLimit(1, true));
            // Dropped supplies stay 45 s.
            sim.SpawnPickup(a.Position + Vector3.forward * 5, PickupKind.Health);
            Assert.AreEqual(GameSim.SupplyLife, sim.Pickups.Values.Last().Life, 0.01f);
        }

        [Test]
        public void LifestealIsCappedAndRateLimited()
        {
            var sim = NewMission();
            var a = sim.Actors[1];
            for (int i = 0; i < 20; i++) sim.ApplyBuff(1, 8 + 200);
            Assert.AreEqual(Defs.MaxLifesteal, a.Lifesteal, 0.01f);
            // Twenty kills in one instant heal at most the banked budget, not 20 x lifesteal.
            a.Hp = 10f;
            sim.Tick(5f);
            float before = a.Hp;
            for (int i = 0; i < 20; i++)
            {
                var e = PlaceEnemy(sim, new Vector3(0, 0, 6), 1f);
                sim.Fire(1, e.Position + Vector3.up);
            }
            Assert.LessOrEqual(a.Hp - before, a.MaxHp * Defs.HealBank + 0.01f);
            Assert.Greater(a.Hp, before);
        }

        [Test]
        public void BossWeakPointsAreChestAndLegs()
        {
            var sim = NewMission();
            // 重裝兵: the head is plain damage, the chest reactor is critical.
            var heavy = PlaceEnemy(sim, new Vector3(0, 0, 10), 50000f, Defs.KindHeavyBoss, Mathf.PI);
            heavy.Boss = true;
            sim.Fire(1, heavy.Position + Vector3.up * Defs.HeadHeight * heavy.Scale);
            Assert.IsFalse(sim.Shots.Last().Head, "heavy boss head is not its weak point");
            sim.Fire(1, heavy.Position + Vector3.up * 1.28f * heavy.Scale);
            Assert.IsTrue(sim.Shots.Last().Head, "chest core");
            sim.Enemies.Clear();
            // 指揮官: the knee joints.
            var commander = PlaceEnemy(sim, new Vector3(0, 0, 10), 50000f, Defs.KindCommander, Mathf.PI);
            commander.Boss = true;
            sim.Fire(1, commander.Position + Vector3.up * Defs.HeadHeight * commander.Scale);
            Assert.IsFalse(sim.Shots.Last().Head, "commander head is not its weak point");
            sim.Fire(1, commander.Position + new Vector3(0.19f * commander.Scale, 0.46f * commander.Scale, 0));
            Assert.IsTrue(sim.Shots.Last().Head, "knee joint");
            Assert.AreEqual("胸口核心", Defs.WeakPointName(Defs.KindHeavyBoss));
            Assert.AreEqual(Defs.WeakHead, Defs.WeakPoint(Defs.KindEmperor));
        }

        [Test]
        public void BossDropsSuperChestWithThreeTeamPicks()
        {
            var sim = NewMission();
            var boss = PlaceEnemy(sim, new Vector3(0, 0, 8), 1f, Defs.KindHeavyBoss);
            boss.Boss = true; boss.Wave = 10;
            sim.Fire(1, boss.Position + Vector3.up * 1.28f * boss.Scale);
            Assert.IsFalse(sim.Enemies.ContainsKey(boss.Id));
            Assert.IsFalse(sim.Pickups.Values.Any(p => p.Kind == PickupKind.SuperChest), "the chest opens at once, nothing on the ground");
            int picks = 0;
            for (int i = 0; i < 300 && picks < Defs.SuperChestPicks; i++)
            {
                sim.Tick(0.02f);   // the boss XP level-ups come first; the chest is opened once play resumes
                if (!sim.BuffPending) continue;
                var offer = sim.BuffOffers[1];
                if (sim.SuperPick > 0)
                {
                    picks++;
                    Assert.AreEqual(picks, sim.SuperPick);
                    Assert.IsTrue(offer.All(code => Defs.BuffTier(code) >= 1), "super picks are 稀有 or better");
                }
                sim.ChooseBuff(1, offer[0]);
            }
            Assert.AreEqual(Defs.SuperChestPicks, picks);
            Assert.IsFalse(sim.Pickups.Values.Any(p => p.Kind == PickupKind.SuperChest));
            // The last boss of a regular mission ends it: no chest there.
            var sim2 = NewMission();
            var last = PlaceEnemy(sim2, new Vector3(0, 0, 8), 1f, Defs.KindCommander);
            last.Boss = true; last.Wave = Defs.WavesPerMission;
            sim2.Fire(1, last.Position + new Vector3(0, 1.2f, 0));
            Assert.IsFalse(sim2.Pickups.Values.Any(p => p.Kind == PickupKind.SuperChest));
        }

        [Test]
        public void SlamSplitterAndMedic()
        {
            var sim = NewMission();
            var a = sim.Actors[1];
            PlaceEnemy(sim, new Vector3(20, 0, 20)).Speed = 0;   // an enemy alive somewhere: rounds only clear once none are left
            // A slam mark bursts after the wind-up and only hurts inside its circle.
            var mark = sim.SpawnBullet(new Vector3(a.Position.x, 0.05f, a.Position.z), Vector3.zero, 30f, BulletKind.Slam);
            mark.Life = Defs.SlamWindup;
            float hp = a.Hp;
            sim.Tick(Defs.SlamWindup * 0.5f);
            Assert.AreEqual(hp, a.Hp, 0.01f);
            for (int i = 0; i < 60 && sim.Bullets.ContainsKey(mark.Id); i++) sim.Tick(0.05f);
            Assert.Less(a.Hp, hp);
            // A splitter bursts into two splitlings.
            var splitter = PlaceEnemy(sim, new Vector3(0, 0, 9), 1f, Defs.KindSplitter);
            sim.Fire(1, splitter.Position + Vector3.up);
            Assert.AreEqual(2, sim.Enemies.Values.Count(e => e.Kind == Defs.KindSplitling));
            sim.Enemies.Clear();
            // A medic heals a wounded neighbour on its pulse.
            var medic = PlaceEnemy(sim, new Vector3(5, 0, 12), 500f, Defs.KindMedic);
            var hurt = PlaceEnemy(sim, new Vector3(6, 0, 12), 1000f);
            hurt.Hp = 100f;
            medic.RingCooldown = 0.01f;
            sim.Tick(0.05f);
            Assert.Greater(hurt.Hp, 100f);
        }

        [Test]
        public void EndlessKeepsGoingAndScales()
        {
            var sim = NewMission();
            sim.Endless = true;
            sim.Wave = Defs.WavesPerMission;
            sim.Enemies.Clear();
            sim.WaveDelay = 0f;
            sim.Tick(0.05f);
            Assert.AreEqual(MissionState.Playing, sim.State);
            Assert.AreEqual(Defs.WavesPerMission + 1, sim.Wave);
            Assert.GreaterOrEqual(Defs.EndlessHp(40), 2f);
            Assert.AreEqual(1f, Defs.EndlessHp(20), 1e-5f);
            Assert.AreEqual(0x103, Defs.PackTier(3, true));
            Assert.AreEqual(3, Defs.TierOf(0x103));
            Assert.IsTrue(Defs.IsEndless(0x103) && !Defs.IsEndless(3));
            Assert.IsFalse(Defs.EndlessOpen(new[] { -1 }, 0));
            Assert.IsTrue(Defs.EndlessOpen(new[] { 0 }, 0));
            // Wave 30 brings a boss in endless.
            sim.Enemies.Clear(); sim.Wave = 30; sim.SpawnWave();
            Assert.IsTrue(sim.Enemies.Values.Any(e => e.Boss));
        }

        [Test]
        public void HeadshotsDoubleBodyShotsNormal()
        {
            var sim = NewMission();
            var a = sim.Actors[1];
            var e = PlaceEnemy(sim, new Vector3(0, 0, 10), 500f);
            sim.Fire(1, e.Position + Vector3.up * Defs.HeadHeight);
            Assert.AreEqual(500f - 56f, e.Hp, 0.01f);
            Assert.IsTrue(sim.Shots.Last().Head);
            sim.Fire(1, e.Position + Vector3.up * 0.8f);
            Assert.AreEqual(500f - 84f, e.Hp, 0.01f);
        }

        [Test]
        public void ShieldBlocksFrontalBodyShots()
        {
            var sim = NewMission();
            var front = PlaceEnemy(sim, new Vector3(0, 0, 8), 500f, Defs.KindShield, Mathf.PI);
            sim.Fire(1, front.Position + Vector3.up * 0.9f);
            float frontDamage = 500f - front.Hp;
            front.Hp = 500f;
            front.Yaw = 0f;
            sim.Fire(1, front.Position + Vector3.up * 0.9f);
            Assert.Less(frontDamage, (500f - front.Hp) * 0.5f);
        }

        [Test]
        public void ShotgunPelletsAndWeaponSwitching()
        {
            var lo = new Loadout { Owned = { "shotgun" }, Equipped = { "pistol", "shotgun" } };
            var sim = NewMission(lo);
            var a = sim.Actors[1];
            sim.SwitchWeapon(a, 3);
            Assert.AreEqual(0, a.Slot);
            sim.SwitchWeapon(a, 1);
            Assert.AreEqual("shotgun", a.WeaponId);
            Assert.AreEqual(9, a.Pellets);
            var e = PlaceEnemy(sim, new Vector3(0, 0, 3));
            sim.Fire(1, e.Position + Vector3.up);
            Assert.AreEqual(9, sim.Shots.Count, "one shot emits nine pellet rays");
            Assert.Less(e.Hp, 1000f - 11f * 3);
        }

        [Test]
        public void XpLevelsUpAndOffersRarityBuffs()
        {
            var sim = NewMission();
            sim.Wave = 1;
            sim.WaveDelay = 99;
            sim.GainXp(sim.XpNeed);
            Assert.AreEqual(2, sim.Level);
            Assert.Greater(sim.XpNeed, 100);
            sim.Tick(0.01f);
            Assert.IsTrue(sim.BuffPending);
            Assert.AreEqual(3, sim.BuffOffers[1].Length);
            int wave = sim.Wave;
            sim.Tick(0.5f);
            Assert.AreEqual(wave, sim.Wave, "Buff selection pauses the mission");
            Assert.IsTrue(sim.ChooseBuff(1, sim.BuffOffers[1][0]));
            Assert.IsFalse(sim.BuffPending);
            var a = sim.Actors[1];
            float before = a.DamageMult;
            sim.ApplyBuff(1, 200);
            Assert.AreEqual(before + 0.5f, a.DamageMult, 0.001f);
        }

        [Test]
        public void DifficultyScalesWithWaveAndLevel()
        {
            var sim = NewMission();
            sim.Wave = 2;
            sim.Level = 2;
            sim.SpawnWave();
            var e = sim.Enemies.Values.First();
            Assert.Greater(e.Hp, 55f * 1.08f * 1.07f);
            Assert.Greater(e.Damage, 10f);
        }

        [Test]
        public void HigherTiersToughenEnemiesAndPayMore()
        {
            GameSim Wave(int tier)
            {
                var s = new GameSim(42) { EnemyVariety = false };
                s.AddActor(1, new Loadout());
                s.StartMission(0, tier);
                s.Wave = 10;
                s.SpawnWave();
                return s;
            }
            var normal = Wave(0);
            var nightmare = Wave(2);
            Assert.AreEqual(2, nightmare.Difficulty);
            Assert.AreEqual(normal.Enemies.Count + Defs.Difficulties[2].ExtraEnemies, nightmare.Enemies.Count);
            var bossN = normal.Enemies.Values.Single(e => e.Boss);
            var bossX = nightmare.Enemies.Values.Single(e => e.Boss);
            Assert.AreEqual(bossN.MaxHp * Defs.Difficulties[2].Hp, bossX.MaxHp, 0.5f);
            Assert.AreEqual(bossN.Damage * Defs.Difficulties[2].Damage, bossX.Damage, 0.01f);
            Assert.AreEqual(2.2f, Defs.Difficulties[2].Damage, 0.001f);
            normal.Kills = nightmare.Kills = 100;
            Assert.AreEqual(Mathf.RoundToInt(normal.MissionGold() * 2.4f), nightmare.MissionGold(), 1);
            // Tier unlocks: 普通 always, each tier after the one below is cleared in that region.
            var cleared = new List<int> { 1, -1, -1 };
            Assert.IsTrue(Defs.DifficultyOpen(cleared, 0, 2));
            Assert.IsFalse(Defs.DifficultyOpen(cleared, 1, 1));
            Assert.IsTrue(Defs.DifficultyOpen(cleared, 2, 0));
            Assert.IsFalse(Defs.DifficultyOpen(null, 0, 1));
            // XP mirrors mission_xp() in service.py.
            Assert.AreEqual(240, Defs.MissionXp(40, 20, true));
            Assert.AreEqual(300, Defs.MissionXp(40, 20, true, 1));
            Assert.AreEqual(360, Defs.MissionXp(40, 20, true, 2));
        }

        [Test]
        public void DownsRevivesAndTeamKillsAreRecorded()
        {
            var sim = new GameSim(7);
            sim.AddActor(1, new Loadout());
            sim.AddActor(2, new Loadout());
            sim.StartMission(0);
            sim.WaveDelay = 999f;
            var downed = sim.Actors[1];
            var medic = sim.Actors[2];
            medic.Position = downed.Position + Vector3.right;
            downed.Hp = 0;
            sim.Tick(0.02f);
            Assert.AreEqual(1, downed.StatDowns);
            sim.Inputs[2] = new PlayerInput { Interact = true, Yaw = medic.Yaw };
            for (int i = 0; i < 200 && !downed.Alive; i++) sim.Tick(0.02f);
            Assert.IsTrue(downed.Alive);
            Assert.AreEqual(1, medic.StatRevives);
            Assert.AreEqual(1, downed.StatDowns);   // being revived is not another down
            sim.Inputs.Clear();
            sim.Wave = 10;
            sim.SpawnWave();
            var boss = sim.Enemies.Values.Single(e => e.Boss);
            boss.Hp = 1;
            sim.Fire(1, boss.Position + Vector3.up * 1.5f);
            for (int i = 0; i < 5 && sim.Enemies.ContainsKey(boss.Id); i++) { boss.Hp = 0.01f; sim.Fire(1, boss.Position + Vector3.up * 1.5f); }
            if (sim.Enemies.ContainsKey(boss.Id)) Assert.Inconclusive("shot missed the boss");
            Assert.AreEqual(1, downed.StatBossKills);
            Assert.AreEqual(1, medic.StatBossKills);   // the whole team gets boss and elite records
        }

        [Test]
        public void AimPointMustMatchTheView()
        {
            var sim = new GameSim(3);
            var a = sim.AddActor(1, new Loadout());
            sim.StartMission(0);
            sim.WaveDelay = 999f;
            Vector3 eye = a.Position + Vector3.up * Defs.EyeHeight;
            // Looking straight ahead (+z) and aiming there: accepted.
            sim.Inputs[1] = new PlayerInput { Fire = true, Yaw = 0, Pitch = 0, Aim = eye + Vector3.forward * Defs.ShotRange };
            sim.Tick(0.02f);
            Assert.AreEqual(0, a.AimFlags);
            // Third-person: aim point taken from a camera behind the shoulder is still accepted.
            a.FireCooldown = 0;
            sim.Inputs[1] = new PlayerInput { Fire = true, Yaw = 0, Pitch = 0, Aim = eye + new Vector3(0.8f, 0.6f, -3.5f) + Vector3.forward * Defs.ShotRange, Seq = 2 };
            sim.Tick(0.02f);
            Assert.AreEqual(0, a.AimFlags);
            // Aiming behind the player while looking forward (an aim hack sending a target directly): corrected and counted.
            a.FireCooldown = 0;
            sim.Inputs[1] = new PlayerInput { Fire = true, Yaw = 0, Pitch = 0, Aim = eye + Vector3.back * 20f, Seq = 3 };
            sim.Tick(0.02f);
            Assert.AreEqual(1, a.AimFlags);
        }

        [Test]
        public void ChapterTwoBringsImperialGuards()
        {
            var sim = new GameSim(11);
            var a = sim.AddActor(1, new Loadout());
            sim.StartMission(Defs.RegionsPerChapter);
            sim.WaveDelay = 999f;
            sim.Wave = 9;
            bool seen = false;
            for (int i = 0; i < 6 && !seen; i++) { sim.Enemies.Clear(); sim.SpawnWave(); seen = sim.Enemies.Values.Any(e => e.Kind == Defs.KindGuard); }
            Assert.IsTrue(seen, "guards join chapter 2 waves");
            var chapterOne = new GameSim(11);
            chapterOne.AddActor(1, new Loadout());
            chapterOne.StartMission(2);
            chapterOne.Wave = 9;
            for (int i = 0; i < 6; i++) { chapterOne.Enemies.Clear(); chapterOne.SpawnWave(); Assert.IsFalse(chapterOne.Enemies.Values.Any(e => e.Kind == Defs.KindGuard)); }

            // Wind-up, rush, one hit on contact.
            sim.Enemies.Clear();
            var guard = new Enemy { Id = 900, Position = a.Position + new Vector3(0, 0, 8f), Speed = 2.5f, Damage = 20f, Hp = 100, MaxHp = 100 };
            sim.ApplyKind(guard, Defs.KindGuard);
            guard.ChargeCooldown = 0f;
            sim.Enemies[guard.Id] = guard;
            sim.Inputs[1] = new PlayerInput();
            sim.Tick(0.02f);
            Assert.AreEqual(1, guard.State, "wind-up starts within range");
            float hp = a.Hp;
            for (int i = 0; i < 120 && guard.State != 0; i++) sim.Tick(0.02f);
            Assert.Less(a.Hp, hp, "the rush hits");
            Assert.AreEqual(0, guard.State);
            // The shield halves front hits, not shots from behind or to the head.
            guard.Yaw = 0f;
            Assert.AreEqual(0.4f, GameSim.DamageMultiplier(guard, Vector3.back, false), 1e-4f);
            Assert.AreEqual(1f, GameSim.DamageMultiplier(guard, Vector3.forward, false), 1e-4f);
            Assert.AreEqual(1f, GameSim.DamageMultiplier(guard, Vector3.back, true), 1e-4f);
        }

        [Test]
        public void EmperorIsTheFinalBossWithThreePhases()
        {
            var sim = new GameSim(5);
            var a = sim.AddActor(1, new Loadout(), true);
            sim.SetAdmin(1, new AdminSettings { God = true });
            sim.StartMission(Defs.Regions.Length - 1);
            sim.WaveDelay = 999f;
            sim.Wave = 20;
            sim.SpawnWave();
            var emperor = sim.Enemies.Values.Single(e => e.Boss);
            Assert.AreEqual(Defs.KindEmperor, emperor.Kind);
            Assert.IsTrue(sim.Announcements.Contains("赫利奧皇帝 降臨"));
            // Other regions keep the commander on wave 20.
            var other = new GameSim(5);
            other.AddActor(1, new Loadout());
            other.StartMission(Defs.Regions.Length - 2);
            other.Wave = 20;
            other.SpawnWave();
            Assert.AreEqual(Defs.KindCommander, other.Enemies.Values.Single(e => e.Boss).Kind);

            sim.Inputs[1] = new PlayerInput();
            emperor.Hp = emperor.MaxHp * 0.6f;
            sim.Tick(0.02f);
            Assert.AreEqual(2, emperor.Phase);
            Vector3 before = emperor.Position;
            emperor.BlinkCooldown = 0f;
            sim.Tick(0.02f);
            Assert.Greater(Vector3.Distance(before, emperor.Position), 1f, "phase 2 blinks");
            emperor.Hp = emperor.MaxHp * 0.3f;
            sim.Bullets.Clear();
            for (int i = 0; i < 30; i++) sim.Tick(0.02f);
            Assert.AreEqual(3, emperor.Phase);
            Assert.Greater(sim.Bullets.Count, 4, "phase 3 fires the spiral");
            emperor.SummonCooldown = 0f;
            int guards = sim.Enemies.Values.Count(e => e.Kind == Defs.KindGuard);
            sim.Tick(0.02f);
            Assert.AreEqual(guards + 2, sim.Enemies.Values.Count(e => e.Kind == Defs.KindGuard), "summons two guards");
        }

        [Test]
        public void SaberSwingsCleaveAndBlocks()
        {
            var kit = new Loadout { Owned = new List<string> { "saber" }, Equipped = new List<string> { "saber" } };
            var sim = new GameSim(9);
            var a = sim.AddActor(1, kit);
            sim.StartMission(0);
            sim.WaveDelay = 999f;
            Assert.IsTrue(a.Melee);
            Assert.AreEqual(2.8f, a.Range, 1e-4f);
            Enemy Put(int id, Vector3 offset)
            {
                var e = new Enemy { Id = id, Position = a.Position + offset, Hp = 9999, MaxHp = 9999, Speed = 0, Damage = 0 };
                sim.ApplyKind(e, Defs.KindGrunt);
                e.Hp = e.MaxHp = 9999;
                sim.Enemies[id] = e;
                return e;
            }
            // Four in front within reach, one behind, one out of reach.
            var front = new[] { Put(1, new Vector3(0, 0, 1.2f)), Put(2, new Vector3(0.8f, 0, 1.8f)), Put(3, new Vector3(-0.8f, 0, 2f)), Put(4, new Vector3(0, 0, 2.6f)) };
            var behind = Put(5, new Vector3(0, 0, -1.5f));
            var far = Put(6, new Vector3(0, 0, 6f));
            sim.Inputs[1] = new PlayerInput { Fire = true, Yaw = 0 };
            sim.Tick(0.02f);
            Assert.AreEqual(3, front.Count(e => e.Hp < 9999), "cleaves the three nearest in the arc");
            Assert.Less(front[0].Hp, 9999f);
            Assert.AreEqual(9999f, front[3].Hp, "the fourth is left for the next swing");
            float full = 9999f - front[0].Hp;
            Assert.AreEqual(full * Defs.MeleeCleaveFalloff, 9999f - front[1].Hp, 0.01f, "the second takes 60%");
            Assert.AreEqual(full * Defs.MeleeCleaveFalloff, 9999f - front[2].Hp, 0.01f, "the third takes 60%");
            Assert.AreEqual(9999f, behind.Hp);
            Assert.AreEqual(9999f, far.Hp);
            Assert.AreEqual(a.Mag, a.Ammo, "a blade never uses ammo");
            Assert.AreEqual(0f, a.Reload);
            Assert.IsTrue(sim.Shots.All(s => s.Hit));

            // Guarding: bullets from the front deal 30%, from behind full damage; guarding slows movement.
            sim.Inputs[1] = new PlayerInput { Block = true, Yaw = 0 };
            sim.Tick(0.02f);
            Assert.IsTrue(a.Blocking);
            Assert.AreEqual(Defs.BlockFactor, GameSim.BlockedBy(a, new Vector3(0, 0, -5f)), 1e-4f);
            Assert.AreEqual(1f, GameSim.BlockedBy(a, new Vector3(0, 0, 5f)), 1e-4f);
            Assert.AreEqual(Defs.BaseSpeed * Defs.BlockSpeed, GameSim.EffectiveSpeed(a), 1e-3f);
            // Attacking drops the guard.
            sim.Inputs[1] = new PlayerInput { Block = true, Fire = true, Yaw = 0 };
            sim.Tick(0.02f);
            Assert.IsFalse(a.Blocking);
            // The 彈匣 upgrade lengthens the reach.
            Assert.AreEqual(2.8f * 1.4f, Defs.Weapons["saber"].StatsAt(0, 5, 0).Range, 1e-4f);
        }

        [Test]
        public void EnhancementPastFiveIsHalfStepAndOverloadAddsFivePercent()
        {
            var w = Defs.Weapons["rifle"];
            // Levels 6-10 count half: level 10 = 7.5 effective steps.
            Assert.AreEqual(w.Damage * (1f + 0.12f * 7.5f), w.StatsAt(10, 0, 0).Damage, 1e-3f);
            var over = w.StatsAt(10, 10, 10, 3);
            Assert.AreEqual(w.Damage * (1f + 0.12f * 7.5f) * 1.15f, over.Damage, 1e-3f);
            Assert.AreEqual(w.Interval * 0.85f, over.Interval, 1e-4f);
            Assert.AreEqual(w.Reload * (1f - 0.08f * 7.5f) * 0.85f, over.Reload, 1e-4f);
            // Prices mirror upgrade_cost in server/service.py.
            int[] expected = { 72, 360, 360, 1800, 648, 3240, 5468, 27338, 115, 576, 576, 2880, 1037, 5184, 8748, 43740, 108, 540, 540, 2700, 972, 4860, 8201, 41006 };
            int i = 0;
            foreach (var id in new[] { "pistol", "sniper", "saber" })
                foreach (var level in new[] { 0, 4, 5, 9 })
                    foreach (var rarity in new[] { 0, 4 })
                        Assert.AreEqual(expected[i++], Defs.UpgradeCost(id, level, rarity), $"{id} lv{level} r{rarity}");
        }

        [Test]
        public void CompactSnapshotsRoundTripAndShrink()
        {
            var sim = new GameSim(21);
            for (int i = 1; i <= 4; i++)
            {
                var kit = new Loadout { Owned = new List<string> { "rifle", "sniper" }, Equipped = new List<string> { "rifle", "sniper" } };
                sim.AddActor(i, kit).Name = "Player_" + i;
            }
            sim.StartMission(3);
            sim.WaveDelay = 999f;
            for (int i = 0; i < 30; i++)
            {
                var e = new Enemy { Id = 100 + i, Position = new Vector3(-20 + i * 1.37f, 0, 18.25f - i), Yaw = -3f + i * 0.2f, Hp = 80, MaxHp = 120, Kind = i % 8, Elite = i == 3, Boss = i == 7 };
                sim.Enemies[e.Id] = e;
            }
            for (int i = 0; i < 60; i++) sim.SpawnBullet(new Vector3(i * 0.3f, 1.2f, -i * 0.3f), new Vector3(4.5f, 0, -6.1f), 10, BulletKind.Enemy);
            for (int i = 0; i < 10; i++) sim.SpawnPickup(new Vector3(i, 0, i), PickupKind.Xp);
            byte[] Encode(bool full)
            {
                using var ms = new System.IO.MemoryStream();
                using (var w = new System.IO.BinaryWriter(ms)) Protocol.WriteSnapshot(w, sim, full);
                return ms.ToArray();
            }
            var fullBytes = Encode(true);
            var partBytes = Encode(false);
            // The float-based format this replaced came to about 3440 bytes for this scene.
            TestContext.WriteLine($"snapshot bytes: full {fullBytes.Length}, partial {partBytes.Length} (previous format about 3440)");
            Assert.Less(partBytes.Length, 2300);
            Assert.Less(partBytes.Length, fullBytes.Length);

            var mirror = new GameSim();
            Protocol.ReadSnapshot(new System.IO.BinaryReader(new System.IO.MemoryStream(fullBytes)), mirror, 1, out _, out _);
            Protocol.ReadSnapshot(new System.IO.BinaryReader(new System.IO.MemoryStream(partBytes)), mirror, 1, out _, out _);
            Assert.AreEqual("Player_2", mirror.Actors[2].Name, "a partial snapshot keeps the names from the last full one");
            CollectionAssert.AreEqual(new[] { "rifle", "sniper" }, mirror.Actors[2].Loadout.Equipped);
            foreach (var e in sim.Enemies.Values)
            {
                var m = mirror.Enemies[e.Id];
                Assert.Less(Vector3.Distance(e.Position, m.Position), 0.01f);
                Assert.AreEqual(Mathf.DeltaAngle(e.Yaw * Mathf.Rad2Deg, m.Yaw * Mathf.Rad2Deg), 0f, 0.01f);
                Assert.AreEqual((e.Boss, e.Elite, e.Kind), (m.Boss, m.Elite, m.Kind));
            }
            var b0 = sim.Bullets.Values.First();
            Assert.Less(Vector3.Distance(b0.Velocity, mirror.Bullets[b0.Id].Velocity), 0.01f);
            Assert.Less(Vector3.Distance(sim.Actors[3].Position, mirror.Actors[3].Position), 0.01f);
        }

        [Test]
        public void BossesAreGiantSoldiersWithEnragePhase()
        {
            var sim = NewMission();
            sim.Wave = 10;
            sim.SpawnWave();
            var boss = sim.Enemies.Values.Single(e => e.Boss);
            Assert.AreEqual(Defs.KindHeavyBoss, boss.Kind);
            Assert.AreEqual(3000f, boss.MaxHp, 0.01f);
            int count = sim.Enemies.Count;
            boss.Hp = boss.MaxHp * 0.4f;
            sim.Tick(0.01f);
            Assert.AreEqual(2, boss.Phase);
            Assert.GreaterOrEqual(sim.Enemies.Count, count + 4 - 1);
            sim.Enemies.Clear();
            sim.Wave = 20;
            sim.SpawnWave();
            Assert.AreEqual(Defs.KindCommander, sim.Enemies.Values.Single(e => e.Boss).Kind);
        }

        [Test]
        public void EnemyBulletsAreSlowAndHurt()
        {
            var sim = NewMission();
            var a = sim.Actors[1];
            var e = PlaceEnemy(sim, new Vector3(0, 0, 10));
            e.Shoots = true;
            e.ShotCooldown = 0;
            e.Damage = 10;
            sim.Tick(0.01f);
            Assert.AreEqual(1, sim.Bullets.Count);
            Assert.LessOrEqual(sim.Bullets.Values.First().Velocity.magnitude, Defs.BulletSpeed + 0.01f);
            // The shooter stays (a round fired by a live enemy keeps flying) but stops shooting and walks off.
            e.Shoots = false; e.Speed = 0; e.Position = a.Position + new Vector3(20, 0, 20);
            sim.WaveDelay = 99;
            float hp = a.Hp;
            for (int i = 0; i < 200; i++) sim.Tick(0.02f);
            Assert.Less(a.Hp, hp);
            Assert.AreEqual(0, sim.Bullets.Count);
        }

        [Test]
        public void GrenadeExplodesAndDamages()
        {
            var sim = NewMission();
            var a = sim.Actors[1];
            var e = PlaceEnemy(sim, new Vector3(0, 0, 8), 100f);
            sim.Inputs[1] = new PlayerInput { Skill = true, Aim = a.Position + new Vector3(0, 0, 8) };
            sim.Tick(0.01f);
            Assert.Greater(a.SkillCooldown, 0f);
            sim.Inputs[1] = new PlayerInput();
            for (int i = 0; i < 100; i++) sim.Tick(0.02f);
            Assert.IsTrue(!sim.Enemies.ContainsKey(e.Id) || e.Hp < 100f);
            Assert.IsNotEmpty(sim.Explosions);
        }

        [Test]
        public void PickupsHeal()
        {
            var sim = NewMission();
            var a = sim.Actors[1];
            sim.WaveDelay = 99;
            PlaceEnemy(sim, new Vector3(20, 0, 20));
            a.Hp = 40;
            sim.SpawnPickup(a.Position, PickupKind.Health);
            sim.Tick(0.01f);
            Assert.AreEqual(70f, a.Hp, 0.01f);
            Assert.IsEmpty(sim.Pickups);
        }

        [Test]
        public void DownedTeammatesCanBeRevived()
        {
            var sim = NewMission();
            var other = sim.AddActor(2, null);
            other.Position = sim.Actors[1].Position + Vector3.right;
            other.Hp = 0;
            PlaceEnemy(sim, new Vector3(20, 0, 20));
            sim.WaveDelay = 99;
            sim.Inputs[1] = new PlayerInput { Interact = true };
            for (int i = 0; i < 40; i++) sim.Tick(0.1f);
            Assert.Greater(other.Hp, 0f);
        }

        [Test]
        public void AdminPowersOnlyForAdmins()
        {
            var sim = NewMission();
            var a = sim.Actors[1];
            float baseDamage = a.Damage;
            sim.SetAdmin(1, new AdminSettings { Damage = 5, God = true });
            Assert.AreEqual(baseDamage, a.Damage, 0.001f);

            var admin = NewMission(null, true);
            var b = admin.Actors[1];
            admin.SetAdmin(1, new AdminSettings { Damage = 5, Speed = 2, God = true, InfiniteAmmo = true, Fly = true });
            Assert.AreEqual(baseDamage * 5, b.Damage, 0.001f);
            PlaceEnemy(admin, new Vector3(20, 0, 20));
            admin.WaveDelay = 99;
            b.Hp = 10; b.Ammo = 0;
            admin.Tick(0.01f);
            Assert.AreEqual(b.MaxHp, b.Hp);
            Assert.AreEqual(b.Mag, b.Ammo);
            for (int i = 0; i < 30; i++) admin.MoveActor(b, new PlayerInput { Jump = true }, 0.05f);
            float high = b.Position.y;
            Assert.Greater(high, 5f);
            for (int i = 0; i < 10; i++) admin.MoveActor(b, new PlayerInput(), 0.05f);
            Assert.AreEqual(high, b.Position.y, 0.001f);
        }

        [Test]
        public void DashCooldownAndCoverCollision()
        {
            var sim = NewMission();
            var a = sim.Actors[1];
            a.Position = new Vector3(0, 0, 0);
            GameSim.MoveActor(a, new PlayerInput { Move = Vector2.right, Dash = true }, 0.02f, Defs.BaseSpeed, false);
            Assert.AreEqual(Defs.DashCooldown, a.DashCooldown, 0.03f);
            Assert.IsTrue(GameSim.InCover(new Vector3(-6, 1, 4)));
            Assert.IsFalse(GameSim.InCover(GameSim.PushOutOfCover(new Vector3(-6, 0, 4), 0.4f) + Vector3.up));
        }

        [Test]
        public void TwentyWavesThenVictoryAndGold()
        {
            var sim = NewMission();
            sim.Wave = 20;
            sim.Enemies.Clear();
            sim.WaveDelay = 0;
            sim.Tick(0.01f);
            Assert.AreEqual(MissionState.Victory, sim.State);
            Assert.Greater(sim.MissionGold(), 200);
        }

        [Test]
        public void WavesAreGradedAndStatsTracked()
        {
            var sim = NewMission();
            sim.Wave = 1;
            sim.SpawnWave();
            var me = sim.Actors[1];
            var target = sim.Enemies.Values.First();
            target.Hp = 1;
            target.Position = me.Position + new Vector3(0, 0, 6);
            sim.Fire(1, target.Position + Vector3.up * Defs.HeadHeight);
            Assert.AreEqual(1, me.StatShots);
            Assert.AreEqual(1, me.StatHits);
            Assert.AreEqual(1, me.StatHeadshots);
            Assert.AreEqual(1, me.StatKills);
            sim.Fire(1, me.Position + new Vector3(0, 30, 1));
            Assert.AreEqual(2, me.StatShots);
            Assert.AreEqual(1, me.StatHits);
            sim.Enemies.Clear();
            sim.WaveDelay = 99;
            sim.Tick(0.01f);
            Assert.AreEqual(1, sim.WaveGrades.Count);
            Assert.AreEqual("S", sim.WaveGrades[0]);
            Assert.IsTrue(sim.Announcements.Any(a => a.Contains("評分 S")));
            Assert.AreEqual("S", sim.MissionGrade());
            Assert.AreEqual("C", GameSim.Grade(10));
        }

        [Test]
        public void AllPlayersDownMeansDefeat()
        {
            var sim = NewMission();
            sim.Actors[1].Hp = 0;
            sim.Tick(0.01f);
            Assert.AreEqual(MissionState.Defeat, sim.State);
        }

        [Test]
        public void DroneSupportImprovesSingleTargetWithoutFullFleetMultiplication()
        {
            float DamageFor(int drones)
            {
                var sim = NewMission(); sim.Enemies.Clear(); sim.WaveDelay = 999;
                var e = PlaceEnemy(sim, new Vector3(0, 0, 8), 10000);
                sim.Actors[1].Drones = drones;
                sim.Tick(1f / 60);
                return 10000 - e.Hp;
            }
            float single = DamageFor(1), fleet = DamageFor(4);
            Assert.AreEqual(single * (1 + 3 * Defs.GadgetSupport), fleet, 0.01f);
            Assert.Less(fleet, single * 3);
        }

        [Test]
        public void MissilesSupportSingleTargetAtReducedDamage()
        {
            var sim = NewMission(); sim.Enemies.Clear(); sim.WaveDelay = 999;
            var e = PlaceEnemy(sim, new Vector3(0, 0, 10), 10000);
            sim.Actors[1].MissileLevel = 5;
            sim.Tick(1f / 60);
            var missiles = sim.Bullets.Values.Where(b => b.Kind == BulletKind.Missile).ToArray();
            Assert.AreEqual(5, missiles.Length);
            Assert.IsTrue(missiles.All(b => b.Target == e.Id));
            Assert.AreEqual(Defs.MissileDamage(5) * (1 + 4 * Defs.GadgetSupport), missiles.Sum(b => b.Damage), 0.01f);
        }

        [Test]
        public void BladeOverlapOnLargeTargetIsCapped()
        {
            var sim = NewMission(); sim.Enemies.Clear(); sim.WaveDelay = 999;
            var e = PlaceEnemy(sim, new Vector3(0, 0, 0.1f), 10000);
            e.Scale = 10; e.Speed = 0;
            sim.Actors[1].OrbitLevel = 5;
            sim.Tick(1f / 60);
            Assert.AreEqual(Defs.OrbitDamage(5) * 1.25f, 10000 - e.Hp, 0.01f, "one full blade + a quarter from the overlapping one");
        }

        [Test]
        public void EndlessOverflowDoesNotExponentiallyIncreaseLifeOrDamage()
        {
            var sim = NewMission(); sim.Endless = true;
            var a = sim.Actors[1];
            for (int i = 0; i < 6; i++) { sim.ApplyBuff(1, 0); sim.ApplyBuff(1, 3); }
            float damage = a.DamageMult, hp = a.MaxHp;
            for (int i = 0; i < 100; i++) { sim.ApplyBuff(1, 0); sim.ApplyBuff(1, 3); }
            Assert.AreEqual(damage + 100 * Defs.EndlessDamagePick, a.DamageMult, 0.001f);
            Assert.AreEqual(hp + 100 * Defs.EndlessLifePick, a.MaxHp, 0.01f);
            sim.Level = 14; float scale = sim.LevelScale;
            sim.Level = 100; Assert.AreEqual(scale, sim.LevelScale);
        }

        [Test]
        public void MinigunRatePicksRemainUsefulAndShotgunRewardsCloseRange()
        {
            var lo = new Loadout { Owned = new List<string> { "minigun" }, Equipped = new List<string> { "minigun" } };
            var sim = NewMission(lo);
            float initial = sim.Actors[1].FireInterval;
            sim.ApplyBuff(1, 201);
            Assert.Less(sim.Actors[1].FireInterval, initial * 0.8f);
            for (int i = 0; i < 4; i++) sim.ApplyBuff(1, 201);
            Assert.AreEqual(0.025f, sim.Actors[1].FireInterval, 0.0001f);
            var shotgun = Defs.Weapons["shotgun"];
            Assert.AreEqual(16f * 1.05f, shotgun.Damage, 0.001f);
            Assert.AreEqual(151.2f, shotgun.Damage * shotgun.Pellets, 0.001f);
            Assert.Greater(shotgun.Damage * shotgun.Pellets / shotgun.Interval, Defs.Weapons["pistol"].Damage / Defs.Weapons["pistol"].Interval);
        }

        [Test]
        public void BossWavesCannotBeHiddenByEasyWaveGrades()
        {
            var sim = NewMission();
            for (int wave = 1; wave <= 20; wave++)
            {
                sim.Wave = wave; sim.SpawnWave();
                float par = 12 + 1.2f * wave + (wave % 10 == 0 ? 35 : 0);
                float score = wave % 10 == 0 ? 0 : 90;
                sim.Elapsed += par + (100 - score) / 1.5f;
                sim.Enemies.Clear(); sim.Bullets.Clear(); sim.Pickups.Clear(); sim.WaveDelay = 999;
                sim.Tick(0.001f);
            }
            Assert.AreEqual("B", sim.MissionGrade());
            Assert.AreEqual(20, sim.WaveGrades.Count);
        }

        [Test]
        public void SixtyHzSimulationDeliversMinigunRateUpgrades()
        {
            var lo = new Loadout { Owned = new List<string> { "minigun" }, Equipped = new List<string> { "minigun" } };
            var sim = NewMission(lo); var a = sim.Actors[1];
            sim.WaveDelay = 999; sim.Enemies.Clear();
            a.RateMult = 0.3f; a.MagBonus = 1000; sim.RefreshWeapon(a);
            a.Ammo = a.Mag; a.Spin = 1;
            sim.Inputs[1] = new PlayerInput { Fire = true };
            int ammo = a.Ammo;
            for (int i = 0; i < 60; i++) sim.Tick(1f / 60);
            Assert.That(ammo - a.Ammo, Is.InRange(39, 41));
        }

        [Test]
        public void PlasmaEvolutionOverlapHasDiminishingDamage()
        {
            var sim = NewMission(); sim.Enemies.Clear(); sim.WaveDelay = 999;
            var first = PlaceEnemy(sim, new Vector3(0, 0, 8), 10000);
            PlaceEnemy(sim, new Vector3(0.01f, 0, 8), 10000);
            var a = sim.Actors[1]; a.Plasma = true; a.PlasmaEvolved = true; a.PlasmaTimer = 0;
            sim.Tick(1f / 60);
            float hit = Defs.PlasmaDamage;
            Assert.That(10000 - first.Hp, Is.InRange(hit * 1.5f - 1f, hit * 1.5f + 1f), "a full primary hit and about half of it from the overlap");
        }

        [Test]
        public void AllRegionsAndPartySizesRetainDifficultyAndEndlessProgression()
        {
            foreach (int players in new[] { 1, 4 })
                for (int region = 0; region < 6; region++)
                {
                    float normalBoss = 0;
                    for (int tier = 0; tier < 4; tier++)
                    {
                        var sim = new GameSim(7) { EnemyVariety = false, Endless = true };
                        for (int player = 1; player <= players; player++) sim.AddActor(player, new Loadout());
                        sim.StartMission(region, tier); sim.Level = 14;
                        float previous = 0;
                        foreach (int wave in new[] { 20, 40, 60 })
                        {
                            sim.Enemies.Clear(); sim.Wave = wave; sim.SpawnWave();
                            float hp = sim.Enemies.Values.Single(e => e.Boss).MaxHp;
                            Assert.Greater(hp, previous); previous = hp;
                            Assert.IsTrue(sim.Enemies.Values.All(e => e.Hp > 0 && e.Damage > 0));
                            if (wave == 20)
                            {
                                if (tier == 0) normalBoss = hp;
                                Assert.AreEqual(normalBoss * Defs.Difficulty(tier).Hp, hp, 0.1f);
                            }
                        }
                    }
                }
        }
    }
}
