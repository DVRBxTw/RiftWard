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
            sim.ApplyBuff(1, 20);
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
