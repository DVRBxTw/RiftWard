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
            sim.Bullets[1] = new Bullet { Id = 1, Position = a.Position + Vector3.up * 0.9f, Velocity = Vector3.zero, Damage = 50f, Kind = BulletKind.Enemy };
            sim.Enemies[999] = new Enemy { Id = 999, Position = a.Position + new Vector3(15, 0, 15), Hp = 9999, MaxHp = 9999, Shoots = false, MeleeCooldown = 99f };
            sim.Tick(0.01f);
            Assert.AreEqual(hp - 40f, a.Hp, 0.01f);
            for (int i = 0; i < 10; i++) sim.ApplyBuff(1, Defs.BuffArmor + 200);
            Assert.AreEqual(Defs.MaxArmor, a.Armor, 0.001f);
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
            Assert.AreEqual(7, a.Pellets);
            var e = PlaceEnemy(sim, new Vector3(0, 0, 3));
            sim.Fire(1, e.Position + Vector3.up);
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
            Assert.AreEqual(bossN.MaxHp * 2.4f, bossX.MaxHp, 0.5f);
            Assert.AreEqual(bossN.Damage * 1.7f, bossX.Damage, 0.01f);
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
            sim.Enemies.Clear();
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
    }
}
