using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using NUnit.Framework;
using RiftWard.Client;
using RiftWard.Sim;
using UnityEngine;

namespace RiftWard.Tests
{
    public class NetworkTests
    {
        static void Set(object target, string name, object value) => target.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);
        static object Get(object target, string name) => target.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(target);

        [Test]
        public void LoadingPlayerSurvivesOldTimeoutAndCombatWaitsForFirstInput()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using var client = new TcpClient();
            client.Connect((IPEndPoint)listener.LocalEndpoint);
            var connection = new Connection(1, listener.AcceptTcpClient());
            listener.Stop();
            var go = new GameObject("Loading regression");
            try
            {
                var server = go.AddComponent<GameServer>();
                var playerType = typeof(GameServer).GetNestedType("Player", BindingFlags.NonPublic);
                var roomType = typeof(GameServer).GetNestedType("Room", BindingFlags.NonPublic);
                var player = Activator.CreateInstance(playerType, true);
                var room = Activator.CreateInstance(roomType, true);
                var sim = new GameSim(42);
                sim.AddActor(1, new Loadout());
                sim.StartMission(0);
                Set(player, "Conn", connection);
                Set(player, "Profile", new Profile { Id = 1, Username = "Loading" });
                Set(player, "Room", room);
                Set(room, "Sim", sim);
                Set(room, "Code", "TEST");
                Set(room, "Mode", "playing");
                Set(room, "Leader", 1);
                ((IList)Get(room, "Members")).Add(1);
                ((IDictionary)Get(server, "players")).Add(1, player);
                ((IDictionary)Get(server, "rooms")).Add("TEST", room);
                Set(server, "clock", 12f);
                var tick = typeof(GameServer).GetMethod("TickRoom", BindingFlags.Instance | BindingFlags.NonPublic);
                tick.Invoke(server, new[] { room });
                Assert.IsTrue(sim.Actors.ContainsKey(1), "Loading is not an in-game disconnect after ten seconds");
                Assert.AreEqual(0f, sim.Elapsed, "Combat waits for the initial input, while snapshots can still be sent");
                Set(player, "ReceivedMissionInput", true);
                Set(player, "LastInput", 12f);
                tick.Invoke(server, new[] { room });
                Assert.Greater(sim.Elapsed, 0f);
                // A finished mission may take multiple API round trips to settle all four rewards.
                Set(room, "Finishing", true);
                Set(server, "clock", 30f);
                tick.Invoke(server, new[] { room });
                Assert.IsTrue(sim.Actors.ContainsKey(1), "Result processing must not eject a player for not sending input");
                Set(room, "Finishing", false);
                Set(player, "ReceivedMissionInput", false);
                Set(server, "clock", 60f);
                tick.Invoke(server, new[] { room });
                Assert.IsFalse(((IDictionary)Get(server, "players")).Contains(1), "A player who never finishes loading still has a bounded timeout");
                var ghosts = (IDictionary)Get(server, "ghosts");
                Assert.IsTrue(ghosts.Contains(1), "The timed-out player's seat is held for a reconnect");
                Assert.IsTrue(sim.Actors.ContainsKey(1), "The held soldier stays in the mission");
                // Reconnecting: a fresh connection with the same account takes the seat back under the old id.
                var fresh = Activator.CreateInstance(playerType, true);
                var freshConn = new Connection(7, listenerPair(out var freshClient));
                Set(fresh, "Conn", freshConn);
                ((IDictionary)Get(server, "players")).Add(7, fresh);
                var resume = typeof(GameServer).GetMethod("Resume", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsTrue((bool)resume.Invoke(server, new object[] { fresh, new Profile { Id = 1, Username = "Loading" } }));
                Assert.AreEqual(1, freshConn.Id, "The new connection takes the old player id");
                Assert.AreSame(player, ((IDictionary)Get(server, "players"))[1]);
                Assert.IsFalse(((IDictionary)Get(server, "players")).Contains(7));
                Assert.IsFalse(ghosts.Contains(1));
                Assert.IsTrue(sim.Actors.ContainsKey(1), "Back in the same run with the same soldier");
                // Dropping again and never returning: the seat is released.
                freshConn.Close();
                typeof(GameServer).GetMethod("Drop", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(server, new object[] { 1 });
                Assert.IsTrue(ghosts.Contains(1));
                ghosts.Remove(1);
                typeof(GameServer).GetMethod("ForgetGhost", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(server, new[] { player });
                Assert.IsFalse(sim.Actors.ContainsKey(1), "An expired seat removes the soldier");
                freshClient.Dispose();
            }
            finally { connection.Close(); UnityEngine.Object.DestroyImmediate(go); }
        }

        static TcpClient listenerPair(out TcpClient client)
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            client = new TcpClient();
            client.Connect((IPEndPoint)l.LocalEndpoint);
            var server = l.AcceptTcpClient();
            l.Stop();
            return server;
        }

        [Test]
        public void QueuedWritesPreserveFrameOrder()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using var client = new TcpClient();
            client.Connect((IPEndPoint)listener.LocalEndpoint);
            client.ReceiveTimeout = 3000;
            var connection = new Connection(1, listener.AcceptTcpClient());
            listener.Stop();
            try
            {
                for (int i = 0; i < 100; i++)
                {
                    int value = i;
                    connection.Send(Protocol.Frame(Msg.Notice, w => w.Write(value)));
                }
                using var reader = new System.IO.BinaryReader(client.GetStream());
                for (int i = 0; i < 100; i++)
                {
                    Assert.AreEqual(5, reader.ReadInt32());
                    Assert.AreEqual((byte)Msg.Notice, reader.ReadByte());
                    Assert.AreEqual(i, reader.ReadInt32());
                }
            }
            finally { connection.Close(); }
        }

        [Test]
        public void StalledReceiverCannotBlockTheGameThread()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using var client = new TcpClient { ReceiveBufferSize = 1024 };
            client.Connect((IPEndPoint)listener.LocalEndpoint);
            var connection = new Connection(1, listener.AcceptTcpClient());
            listener.Stop();
            try
            {
                var frame = new byte[65536];
                var timer = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 512 && !connection.Closed; i++) connection.Send(frame);
                Assert.Less(timer.Elapsed.TotalSeconds, 2, "Send only queues data; socket writes cannot hold Unity's main thread");
                Assert.IsTrue(connection.Closed, "A stalled peer's send queue must be bounded");
            }
            finally { connection.Close(); }
        }
    }
}
