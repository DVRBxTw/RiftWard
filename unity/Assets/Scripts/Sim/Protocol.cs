using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace RiftWard.Sim
{
    /// Wire format: [int32 length][byte type][payload]. Little-endian BinaryWriter encoding.
    public enum Msg : byte
    {
        // client → server
        Hello = 1, Input = 2, ChooseBuff = 3, Admin = 4, RequestDeploy = 5, SelectRegion = 6, LeaveMission = 7, RefreshProfile = 8, RequestHome = 9, CreateRoom = 10, JoinRoom = 11, LeaveRoom = 12,
        /// [byte channel][string text]
        Chat = 13,
        /// Re-read the friend list from the account service (after adding / removing a friend).
        RefreshFriends = 14,
        /// [int account id]: invite a friend into this player's room (one is opened if needed).
        InviteFriend = 15,
        /// [int account id]: decline that friend's invitation.
        DeclineInvite = 16,
        /// [bool paused]: the pause menu opened or closed. Honoured only while the player is alone in the room.
        SetPause = 17,
        /// [vector3 position][byte kind][int enemy id]: middle-mouse ping (kinds in PingMarkers; the id only for an enemy ping).
        Ping = 18,
        /// [string username]: you just sent this account a friend request; the server tells them if they are online.
        FriendRequest = 19,
        /// [int account id]: ask a friend who is in a room to let you in.
        JoinRequest = 20,
        /// [int account id][bool accept]: answer a friend's join request (accept pulls them into your room).
        JoinAnswer = 21,
        /// [int member id]: the leader hands the room to another member.
        TransferLeader = 22,
        /// [byte count, count × byte]: the player's own upgrade stacks this mission (sent when they change),
        /// for the hold-Tab upgrade panel.
        BuffStacks = 23,
        /// [long stamp]: latency probe, sent about once a second; the server echoes it back as NetPong.
        NetPing = 24,
        /// [bool join]: enter or leave the PvP (Free For All) matchmaking queue.
        PvpQueue = 25,
        // server → client
        Welcome = 50, Error = 51, Lobby = 52, Deploy = 53, Snapshot = 54, Events = 55, BuffOffer = 56, Finish = 57, ProfileSync = 58, Notice = 59, BackHome = 60,
        /// [byte channel][int account id][string name][string title][string text][bool admin]
        ChatLine = 61,
        /// [int count] × ([int account id][byte state][string room code][string name, empty when offline]): friends' presence.
        Presence = 62,
        /// [int account id][string name][string room code]
        Invite = 63,
        /// [int actor id][string name][vector3 position][byte kind][int enemy id]: a ping from someone in the room.
        PingMark = 64,
        /// [string name]: someone just sent you a friend request.
        FriendRequestIn = 65,
        /// [int account id][string name]: a friend asks to join your room.
        JoinRequestIn = 66,
        /// [long stamp]: echo of the client's NetPing, for the round-trip time shown in the corner.
        NetPong = 67,
        /// [bool queued][int waiting][float seconds waited]: this player's PvP queue status (about once a second).
        PvpStatus = 68,
    }

    /// Chat channels: everyone on the server, the player's room, server notices, and private messages.
    /// Whisper (client → server): text is "收件人帳號 訊息". Server → client: Whisper = received (name = sender),
    /// WhisperSent = your own copy (name = recipient).
    public static class ChatChannel
    {
        public const byte World = 0, Room = 1, System = 2, Whisper = 3, WhisperSent = 4;
        public const int MaxLength = 100;
    }

    /// What a friend is doing, as reported in Msg.Presence.
    public static class PresenceState
    {
        public const byte Offline = 0, Lobby = 1, InRoom = 2, InMission = 3;
    }

    public static class Protocol
    {
        public const int DefaultPort = 24770;
        public const int MaxMessage = 1 << 20;

        public static byte[] Frame(Msg type, Action<BinaryWriter> body = null)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
            {
                w.Write(0);
                w.Write((byte)type);
                body?.Invoke(w);
            }
            var bytes = ms.ToArray();
            BitConverter.GetBytes(bytes.Length - 4).CopyTo(bytes, 0);
            return bytes;
        }

        public static void Write(this BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        public static Vector3 ReadVector3(this BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        // ------------------------------------------------------------ input

        public static void Write(this BinaryWriter w, PlayerInput i)
        {
            w.Write(i.Seq); w.Write(i.Move.x); w.Write(i.Move.y); w.Write(i.Yaw); w.Write(i.Pitch); w.Write(i.Aim);
            int flags = (i.Fire ? 1 : 0) | (i.Dash ? 2 : 0) | (i.Jump ? 4 : 0) | (i.Descend ? 8 : 0) | (i.Reload ? 16 : 0) | (i.Skill ? 32 : 0) | (i.Interact ? 64 : 0) | (i.Block ? 128 : 0);
            w.Write((byte)flags);
            w.Write((byte)Mathf.Clamp(i.Slot, 0, 255));
            w.Write((byte)(i.Ult ? 1 : 0));
            w.Write((ushort)Mathf.Clamp(i.RewindMs, 0, 1000));
        }

        public static PlayerInput ReadInput(this BinaryReader r)
        {
            var i = new PlayerInput { Seq = r.ReadInt32(), Move = new Vector2(r.ReadSingle(), r.ReadSingle()), Yaw = r.ReadSingle(), Pitch = r.ReadSingle(), Aim = r.ReadVector3() };
            int f = r.ReadByte();
            i.Fire = (f & 1) != 0; i.Dash = (f & 2) != 0; i.Jump = (f & 4) != 0; i.Descend = (f & 8) != 0;
            i.Reload = (f & 16) != 0; i.Skill = (f & 32) != 0; i.Interact = (f & 64) != 0; i.Block = (f & 128) != 0;
            i.Slot = r.ReadByte();
            i.Ult = (r.ReadByte() & 1) != 0;
            i.RewindMs = r.ReadUInt16();
            if (!float.IsFinite(i.Yaw) || !float.IsFinite(i.Pitch) || !float.IsFinite(i.Move.x) || !float.IsFinite(i.Move.y) ||
                !float.IsFinite(i.Aim.x) || !float.IsFinite(i.Aim.y) || !float.IsFinite(i.Aim.z)) i = new PlayerInput { Seq = i.Seq };
            i.Move = Vector2.ClampMagnitude(i.Move, 1f);
            return i;
        }

        public static void Write(this BinaryWriter w, AdminSettings s) { w.Write(s.Damage); w.Write(s.Speed); w.Write(s.God); w.Write(s.InfiniteAmmo); w.Write(s.Fly); w.Write(s.AutoAim); }
        public static AdminSettings ReadAdmin(this BinaryReader r) =>
            new AdminSettings { Damage = r.ReadSingle(), Speed = r.ReadSingle(), God = r.ReadBoolean(), InfiniteAmmo = r.ReadBoolean(), Fly = r.ReadBoolean(), AutoAim = r.ReadBoolean() }.Clamped();

        // ------------------------------------------------------------ snapshot

        // Compact encodings for snapshots: positions and velocities to the centimetre in 16-bit integers (±327 m,
        // far beyond the 46 m arena), angles to 1e-4 rad. Halves the bytes per vector compared with floats.
        const float PosScale = 100f, AngleScale = 10000f;

        static void WriteQ(this BinaryWriter w, Vector3 v)
        {
            w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v.x * PosScale), short.MinValue, short.MaxValue));
            w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v.y * PosScale), short.MinValue, short.MaxValue));
            w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v.z * PosScale), short.MinValue, short.MaxValue));
        }

        static Vector3 ReadQ(this BinaryReader r) => new Vector3(r.ReadInt16() / PosScale, r.ReadInt16() / PosScale, r.ReadInt16() / PosScale);
        /// Angle wrapped to [-pi, pi) in 1e-4 rad steps (fits a short: at most 31416).
        static void WriteAngle(this BinaryWriter w, float a) => w.Write((short)Mathf.RoundToInt((Mathf.Repeat(a + Mathf.PI, Mathf.PI * 2f) - Mathf.PI) * AngleScale));
        static float ReadAngle(this BinaryReader r) => r.ReadInt16() / AngleScale;

        /// One mission snapshot. Names and weapon kits change rarely, so they are only included when full is set
        /// (about once a second, and whenever someone joins); in between, clients keep the last ones they got.
        public static void WriteSnapshot(BinaryWriter w, GameSim sim, bool full = true)
        {
            w.Write((byte)sim.State); w.Write(sim.Region); w.Write(sim.Wave); w.Write(sim.Kills); w.Write(sim.Elapsed);
            w.Write(sim.Level); w.Write(sim.Xp); w.Write(sim.XpNeed); w.Write(sim.BuffPending); w.Write((byte)sim.SuperPick);
            w.Write(sim.Pvp); w.Write(sim.PvpTimeLeft);
            w.Write(full);
            w.Write((ushort)sim.Actors.Count);
            foreach (var a in sim.Actors.Values)
            {
                w.Write(a.Id); w.WriteQ(a.Position); w.Write(a.Hp); w.Write(a.MaxHp); w.WriteAngle(a.Yaw); w.Write(a.VerticalVelocity);
                w.Write(a.DashCooldown); w.Write(a.DashLeft); w.WriteQ(a.DashDirection); w.Write(a.Ack);
                w.Write((short)a.Ammo); w.Write((short)a.Mag); w.Write(a.Reload); w.Write(a.ReloadTime); w.Write((byte)a.Slot);
                w.Write((byte)Mathf.RoundToInt(Mathf.Clamp01(a.Spin) * 255)); w.Write((byte)Mathf.RoundToInt(Mathf.Clamp01(a.Heat) * 255)); w.Write(a.Overheat > 0f);
                w.Write(a.SkillCooldown); w.Write(a.UltCooldown); w.Write(a.RespawnTimer); w.Write((short)a.StatKills); w.Write((short)a.StatDowns); w.Write(a.SkillMult); w.Write(a.Revive); w.Write(GameSim.EffectiveSpeed(a));
                w.Write(a.IsAdmin && a.Admin.Fly); w.Write(a.Zoom);
                if (full)
                {
                    w.Write((byte)a.Loadout.Equipped.Count);
                    foreach (var weapon in a.Loadout.Equipped) w.Write(weapon);
                    w.Write(a.Name ?? "");
                }
                w.Write((byte)a.Drones); w.Write(a.Plasma);
                w.Write((byte)a.OrbitLevel); w.Write(a.DroneEvolved); w.Write(a.PlasmaEvolved);
                w.Write((byte)a.Loadout.RarityOf(a.WeaponId));
                w.WriteAngle(a.Pitch);                                 // aim pitch so teammates' rigs aim up / down
                w.Write(a.Damage);                                     // current weapon damage, shown on the HUD
            }
            w.Write((ushort)sim.Enemies.Count);
            foreach (var e in sim.Enemies.Values)
            {
                w.Write(e.Id); w.WriteQ(e.Position); w.Write(e.Hp); w.Write(e.MaxHp); w.WriteAngle(e.Yaw);
                w.Write((byte)((e.Boss ? 1 : 0) | (e.Elite ? 2 : 0) | (e.ShieldBroken ? 4 : 0))); w.Write((byte)e.Kind); w.Write((byte)e.State);
            }
            w.Write((ushort)sim.Bullets.Count);
            foreach (var b in sim.Bullets.Values) { w.Write(b.Id); w.WriteQ(b.Position); w.WriteQ(b.Velocity); w.Write((byte)b.Kind); }
            w.Write((ushort)sim.Pickups.Count);
            foreach (var p in sim.Pickups.Values) { w.Write(p.Id); w.WriteQ(p.Position); w.Write((byte)p.Kind); }
        }

        /// Applies a server snapshot onto a client-side mirror simulation. Returns the local actor's acked input sequence.
        public static int ReadSnapshot(BinaryReader r, GameSim sim, int localId, out float localSpeed, out bool localFly)
        {
            localSpeed = Defs.BaseSpeed; localFly = false;
            sim.State = (MissionState)r.ReadByte(); sim.Region = r.ReadInt32(); sim.Wave = r.ReadInt32(); sim.Kills = r.ReadInt32(); sim.Elapsed = r.ReadSingle();
            sim.Level = r.ReadInt32(); sim.Xp = r.ReadInt32(); sim.XpNeed = r.ReadInt32(); sim.BuffPending = r.ReadBoolean(); sim.SuperPick = r.ReadByte();
            sim.Pvp = r.ReadBoolean(); sim.PvpTimeLeft = r.ReadSingle();
            bool full = r.ReadBoolean();
            int ack = 0;
            var seen = new HashSet<int>();
            int count = r.ReadUInt16();
            for (int i = 0; i < count; i++)
            {
                int id = r.ReadInt32();
                seen.Add(id);
                if (!sim.Actors.TryGetValue(id, out var a)) sim.Actors[id] = a = new Actor { Id = id };
                a.Position = r.ReadQ(); a.Hp = r.ReadSingle(); a.MaxHp = r.ReadSingle(); a.Yaw = r.ReadAngle(); a.VerticalVelocity = r.ReadSingle();
                a.DashCooldown = r.ReadSingle(); a.DashLeft = r.ReadSingle(); a.DashDirection = r.ReadQ(); a.Ack = r.ReadInt32();
                a.Ammo = r.ReadInt16(); a.Mag = r.ReadInt16(); a.Reload = r.ReadSingle(); a.ReloadTime = r.ReadSingle(); a.Slot = r.ReadByte();
                a.Spin = r.ReadByte() / 255f; a.Heat = r.ReadByte() / 255f; a.Overheat = r.ReadBoolean() ? 1f : 0f;
                a.SkillCooldown = r.ReadSingle(); a.UltCooldown = r.ReadSingle(); a.RespawnTimer = r.ReadSingle(); a.StatKills = r.ReadInt16(); a.StatDowns = r.ReadInt16(); a.SkillMult = r.ReadSingle(); a.Revive = r.ReadSingle();
                float speed = r.ReadSingle();
                bool fly = r.ReadBoolean();
                a.Zoom = r.ReadSingle();
                if (full)
                {
                    int kit = r.ReadByte();
                    var equipped = new List<string>();
                    for (int k = 0; k < kit; k++) equipped.Add(r.ReadString());
                    a.Loadout.Equipped = equipped.Where(Defs.Weapons.ContainsKey).DefaultIfEmpty("pistol").ToList();
                    a.Name = r.ReadString();
                }
                a.Drones = r.ReadByte(); a.Plasma = r.ReadBoolean();
                a.OrbitLevel = r.ReadByte(); a.DroneEvolved = r.ReadBoolean(); a.PlasmaEvolved = r.ReadBoolean();
                a.Loadout.Rarity[a.WeaponId] = r.ReadByte();
                a.Pitch = r.ReadAngle();
                a.Damage = r.ReadSingle();
                a.Speed = speed;
                if (id == localId) { ack = a.Ack; localSpeed = speed; localFly = fly; }
            }
            foreach (var id in sim.Actors.Keys.Where(id => !seen.Contains(id)).ToList()) sim.Actors.Remove(id);

            seen.Clear();
            count = r.ReadUInt16();
            for (int i = 0; i < count; i++)
            {
                int id = r.ReadInt32();
                seen.Add(id);
                if (!sim.Enemies.TryGetValue(id, out var e)) sim.Enemies[id] = e = new Enemy { Id = id };
                e.Position = r.ReadQ(); e.Hp = r.ReadSingle(); e.MaxHp = r.ReadSingle(); e.Yaw = r.ReadAngle();
                int flags = r.ReadByte();
                e.Boss = (flags & 1) != 0; e.Elite = (flags & 2) != 0; e.ShieldBroken = (flags & 4) != 0; e.Kind = r.ReadByte(); e.State = r.ReadByte();
                e.Scale = Defs.Kinds[Mathf.Clamp(e.Kind, 0, Defs.Kinds.Length - 1)].Scale * (e.Elite ? 1.35f : 1f);
            }
            foreach (var id in sim.Enemies.Keys.Where(id => !seen.Contains(id)).ToList()) sim.Enemies.Remove(id);

            seen.Clear();
            count = r.ReadUInt16();
            for (int i = 0; i < count; i++)
            {
                int id = r.ReadInt32();
                seen.Add(id);
                if (!sim.Bullets.TryGetValue(id, out var b)) sim.Bullets[id] = b = new Bullet { Id = id };
                var p = r.ReadQ();
                b.Position = b.Position == Vector3.zero ? p : Vector3.Lerp(b.Position, p, 0.5f);
                b.Velocity = r.ReadQ();
                b.Kind = (BulletKind)r.ReadByte();
            }
            foreach (var id in sim.Bullets.Keys.Where(id => !seen.Contains(id)).ToList()) sim.Bullets.Remove(id);

            seen.Clear();
            count = r.ReadUInt16();
            for (int i = 0; i < count; i++)
            {
                int id = r.ReadInt32();
                seen.Add(id);
                if (!sim.Pickups.TryGetValue(id, out var p)) sim.Pickups[id] = p = new Pickup { Id = id };
                p.Position = r.ReadQ();
                p.Kind = (PickupKind)r.ReadByte();
            }
            foreach (var id in sim.Pickups.Keys.Where(id => !seen.Contains(id)).ToList()) sim.Pickups.Remove(id);
            return ack;
        }

        // ------------------------------------------------------------ events

        public static void WriteEvents(BinaryWriter w, GameSim sim)
        {
            w.Write(sim.Shots.Count);
            foreach (var s in sim.Shots) { w.Write(s.Shooter); w.Write(s.Origin); w.Write(s.End); w.Write(s.Hit); w.Write(s.Head); w.Write(s.Killed); w.Write(s.Dealt); w.Write(s.Pierced); }
            w.Write(sim.Explosions.Count);
            foreach (var e in sim.Explosions) { w.Write(e.Position); w.Write(e.Radius); w.Write(e.Color.r); w.Write(e.Color.g); w.Write(e.Color.b); w.Write(e.Heal); }
            w.Write(sim.Announcements.Count);
            foreach (var a in sim.Announcements) w.Write(a);
            w.Write(sim.Damages.Count);
            foreach (var d in sim.Damages) { w.Write(d.Owner); w.Write((byte)d.Source); w.Write(d.Position); w.Write(d.Dealt); w.Write(d.Killed); }
        }

        public static void ReadEvents(BinaryReader r, GameSim sim)
        {
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
                sim.Shots.Add(new ShotEvent { Shooter = r.ReadInt32(), Origin = r.ReadVector3(), End = r.ReadVector3(), Hit = r.ReadBoolean(), Head = r.ReadBoolean(), Killed = r.ReadBoolean(), Dealt = r.ReadSingle(), Pierced = r.ReadBoolean() });
            n = r.ReadInt32();
            for (int i = 0; i < n; i++)
                sim.Explosions.Add(new ExplosionEvent { Position = r.ReadVector3(), Radius = r.ReadSingle(), Color = new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), 255), Heal = r.ReadBoolean() });
            n = r.ReadInt32();
            for (int i = 0; i < n; i++) sim.Announcements.Add(r.ReadString());
            n = r.ReadInt32();
            for (int i = 0; i < n; i++)
                sim.Damages.Add(new DamageEvent { Owner = r.ReadInt32(), Source = r.ReadByte(), Position = r.ReadVector3(), Dealt = r.ReadSingle(), Killed = r.ReadBoolean() });
        }
    }
}
