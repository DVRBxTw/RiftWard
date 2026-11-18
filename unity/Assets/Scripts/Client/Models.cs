using System.Collections.Generic;
using RiftWard.Sim;
using UnityEngine;

namespace RiftWard.Client
{
    /// Blocky character, enemy and weapon models. Everything faces +Z.
    public static class Models
    {
        static Transform Joint(Transform parent, string name, Vector3 local)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = local;
            return t;
        }

        /// Jointed soldier (faces +Z, feet at the origin). Animated by CharacterRig; weapon goes on GunMount.
        public static GameObject Soldier(Color color, Transform parent = null)
        {
            var root = new GameObject("Soldier").transform;
            root.SetParent(parent, false);
            Color body = Palette.Hex("3a3f46"), dark = Palette.Hex("22262b"), boot = Palette.Hex("1a1c1f"), pouch = Palette.Hex("4a4f46");
            var hips = Joint(root, "Hips", new Vector3(0, 0.78f, 0));
            foreach (var (side, x) in new[] { ("L", -0.17f), ("R", 0.17f) })
            {
                var leg = Joint(hips, "Leg" + side, new Vector3(x, 0, 0));
                Build.Cube(leg, new Vector3(0, -0.19f, 0), new Vector3(0.22f, 0.4f, 0.26f), dark);
                Build.Cube(leg, new Vector3(x > 0 ? 0.12f : -0.12f, -0.16f, 0.02f), new Vector3(0.05f, 0.14f, 0.16f), pouch);
                var knee = Joint(leg, "Knee" + side, new Vector3(0, -0.38f, 0));
                Build.Cube(knee, new Vector3(0, -0.17f, 0), new Vector3(0.2f, 0.36f, 0.24f), dark);
                Build.Cube(knee, new Vector3(0, -0.02f, 0.13f), new Vector3(0.2f, 0.14f, 0.04f), body);
                Build.Cube(knee, new Vector3(0, -0.34f, 0.05f), new Vector3(0.26f, 0.12f, 0.36f), boot);
            }
            var torso = Joint(hips, "Torso", Vector3.zero);
            Build.Cube(torso, new Vector3(0, 0.3f, 0), new Vector3(0.62f, 0.62f, 0.36f), body);
            Build.Cube(torso, new Vector3(0, 0.34f, 0.19f), new Vector3(0.48f, 0.4f, 0.04f), color);
            Build.Cube(torso, new Vector3(0, 0.02f, 0), new Vector3(0.64f, 0.08f, 0.38f), boot);
            foreach (float x in new[] { -0.2f, 0f, 0.2f }) Build.Cube(torso, new Vector3(x, 0.04f, 0.2f), new Vector3(0.13f, 0.14f, 0.06f), pouch);
            Build.Cube(torso, new Vector3(0, 0.32f, -0.27f), new Vector3(0.46f, 0.5f, 0.2f), dark);
            Build.Cube(torso, new Vector3(0, 0.5f, -0.38f), new Vector3(0.3f, 0.06f, 0.02f), color, 1.5f);
            Build.Cube(torso, new Vector3(0.16f, 0.82f, -0.3f), new Vector3(0.025f, 0.5f, 0.025f), Palette.Hex("15171a"));
            foreach (float x in new[] { -0.42f, 0.42f }) Build.Cube(torso, new Vector3(x, 0.54f, 0), new Vector3(0.24f, 0.16f, 0.36f), color);
            foreach (var (side, x) in new[] { ("L", -0.42f), ("R", 0.42f) })
            {
                var arm = Joint(torso, "Arm" + side, new Vector3(x, 0.5f, 0));
                Build.Cube(arm, new Vector3(0, -0.14f, 0), new Vector3(0.17f, 0.3f, 0.19f), body);
                var elbow = Joint(arm, "Elbow" + side, new Vector3(0, -0.28f, 0));
                Build.Cube(elbow, new Vector3(0, -0.12f, 0), new Vector3(0.15f, 0.26f, 0.17f), body);
                Build.Cube(elbow, new Vector3(0, -0.27f, 0), new Vector3(0.14f, 0.08f, 0.15f), boot);
            }
            var head = Joint(torso, "Head", new Vector3(0, 0.66f, 0));
            Build.Cube(head, new Vector3(0, 0.02f, 0), new Vector3(0.16f, 0.08f, 0.16f), dark);
            Build.Cube(head, new Vector3(0, 0.18f, 0), new Vector3(0.36f, 0.34f, 0.38f), body);
            Build.Cube(head, new Vector3(0, 0.36f, 0.02f), new Vector3(0.42f, 0.06f, 0.46f), dark);
            Build.Cube(head, new Vector3(0, 0.18f, 0.2f), new Vector3(0.32f, 0.12f, 0.02f), Palette.Hex("1a2a33"));
            Build.Cube(head, new Vector3(0, 0.2f, 0.21f), new Vector3(0.3f, 0.06f, 0.02f), color, 2f);
            // Detail: shoulder lamps and a helmet antenna in the soldier colour, backpack vents and a belt light.
            foreach (float x in new[] { -0.42f, 0.42f }) Build.Cube(torso, new Vector3(x, 0.63f, 0.12f), new Vector3(0.08f, 0.04f, 0.06f), color, 3f, false);
            Build.Cube(head, new Vector3(-0.17f, 0.42f, -0.08f), new Vector3(0.02f, 0.2f, 0.02f), dark, 0f, false);
            Build.Cube(head, new Vector3(-0.17f, 0.53f, -0.08f), new Vector3(0.04f, 0.04f, 0.04f), color, 4f, false).AddComponent<Spinner>().Blink = 1.2f;
            for (int i = 0; i < 3; i++) Build.Cube(torso, new Vector3(0, 0.2f + i * 0.09f, -0.375f), new Vector3(0.3f, 0.025f, 0.02f), Palette.Hex("15171a"), 0f, false);
            Build.Cube(torso, new Vector3(0.24f, 0.03f, 0.195f), new Vector3(0.06f, 0.04f, 0.02f), color, 2.5f, false);
            Joint(torso, "GunMount", new Vector3(0.1f, 0.36f, 0.3f)).localScale = Vector3.one * 1.35f;
            CharacterRig.Attach(root.gameObject, CharacterRig.Style.Soldier);
            return root.gameObject;
        }

        public static GameObject Enemy(int kind)
        {
            if (kind == Defs.KindEmperor) return Emperor();
            if (Defs.IsBossKind(kind)) return Boss(kind);
            var def = Defs.Kinds[kind];
            Color color = def.Color, shell = kind == Defs.KindGuard ? Palette.Hex("5a4522") : Palette.Hex("4a2c26"), dark = kind == Defs.KindGuard ? Palette.Hex("2a2116") : Palette.Hex("231a19");
            var root = new GameObject("Enemy").transform;
            var hips = Joint(root, "Hips", new Vector3(0, 0.6f, 0));
            foreach (var (side, x) in new[] { ("L", -0.2f), ("R", 0.2f) })
            {
                var leg = Joint(hips, "Leg" + side, new Vector3(x, 0, 0));
                Build.Cube(leg, new Vector3(0, -0.16f, 0), new Vector3(0.25f, 0.32f, 0.28f), dark);
                var knee = Joint(leg, "Knee" + side, new Vector3(0, -0.3f, 0));
                Build.Cube(knee, new Vector3(0, -0.13f, 0), new Vector3(0.22f, 0.28f, 0.26f), dark);
                Build.Cube(knee, new Vector3(0, -0.27f, 0.05f), new Vector3(0.26f, 0.08f, 0.36f), Palette.Hex("1a1414"));
            }
            var torso = Joint(hips, "Torso", Vector3.zero);
            Build.Prim(PrimitiveType.Capsule, torso, new Vector3(0, 0.4f, 0), new Vector3(0.7f, 0.6f, 0.7f), shell);
            // Chest core that pulses; a back vent for a heavier silhouette.
            var core = Build.Cube(torso, new Vector3(0, 0.6f, 0.36f), new Vector3(0.16f, 0.16f, 0.04f), color, 3.5f, false);
            core.AddComponent<Spinner>().Pulse = 0.2f;
            Build.Cube(torso, new Vector3(0, 0.6f, 0.345f), new Vector3(0.26f, 0.26f, 0.02f), dark, 0f, false);
            Build.Cube(torso, new Vector3(0, 0.55f, -0.37f), new Vector3(0.3f, 0.2f, 0.05f), dark, 0f, false);
            Build.Cube(torso, new Vector3(0, 0.55f, -0.4f), new Vector3(0.2f, 0.04f, 0.02f), color, 2.5f, false);
            foreach (float x in new[] { -0.3f, 0.3f }) Build.Cube(torso, new Vector3(x, 0.5f, -0.3f), new Vector3(0.08f, 0.5f, 0.08f), Palette.Hex("2b2321"));
            var head = Joint(torso, "Head", new Vector3(0, 0.95f, 0));
            Build.Cube(head, new Vector3(0, 0.1f, 0.12f), new Vector3(0.56f, 0.38f, 0.5f), color);
            Build.Cube(head, new Vector3(0, 0.12f, 0.39f), new Vector3(0.42f, 0.09f, 0.03f), Palette.Hex("fff1d6"), 2f);
            Build.Cube(head, new Vector3(0.18f, 0.4f, 0), new Vector3(0.03f, 0.3f, 0.03f), dark);
            Build.Cube(head, new Vector3(0.18f, 0.56f, 0), new Vector3(0.06f, 0.06f, 0.06f), color, 2f);
            var armL = Joint(torso, "ArmL", new Vector3(-0.48f, 0.85f, 0));
            Build.Cube(armL, new Vector3(0, 0.03f, 0), new Vector3(0.34f, 0.1f, 0.44f), dark);
            Build.Cube(armL, new Vector3(0, -0.16f, 0), new Vector3(0.25f, 0.32f, 0.35f), color);
            var elbowL = Joint(armL, "ElbowL", new Vector3(0, -0.32f, 0));
            Build.Cube(elbowL, new Vector3(0, -0.13f, 0), new Vector3(0.22f, 0.28f, 0.3f), color);
            var armR = Joint(torso, "ArmR", new Vector3(0.48f, 0.85f, 0));
            Build.Cube(armR, new Vector3(0, -0.3f, 0.2f), new Vector3(0.25f, 0.38f, 0.8f), dark);
            Build.Cube(armR, new Vector3(0, -0.3f, 0.61f), new Vector3(0.12f, 0.12f, 0.04f), color, 2.5f);
            switch (kind)
            {
                case Defs.KindCharger:
                    foreach (float x in new[] { -0.22f, 0.22f })
                        Build.Cube(head, new Vector3(x, 0.4f, 0.1f), new Vector3(0.08f, 0.28f, 0.08f), color, 1.5f).transform.localRotation = Quaternion.Euler(25, 0, 0);
                    // Spiked shoulders and a ram plate: built to run you down.
                    foreach (float x in new[] { -0.5f, 0.5f })
                        for (int i = 0; i < 3; i++)
                            Build.Cube(torso, new Vector3(x, 0.98f, -0.12f + i * 0.12f), new Vector3(0.06f, 0.2f, 0.06f), Palette.Hex("e8d0c0"), 0f, false).transform.localRotation = Quaternion.Euler(-20, 0, x > 0 ? -25 : 25);
                    Build.Cube(head, new Vector3(0, -0.02f, 0.4f), new Vector3(0.5f, 0.16f, 0.06f), Palette.Hex("3a2420"), 0f, false);
                    break;
                case Defs.KindSniper:
                    Build.Cube(armR, new Vector3(0, -0.3f, 1.05f), new Vector3(0.07f, 0.07f, 0.9f), Palette.Hex("2a2140"));
                    // Hooded head, a single glowing scope eye and a long rifle scope.
                    Build.Cube(head, new Vector3(0, 0.22f, 0.05f), new Vector3(0.62f, 0.22f, 0.58f), Palette.Hex("2a2140"), 0f, false);
                    Build.Cube(head, new Vector3(0.12f, 0.12f, 0.4f), new Vector3(0.1f, 0.1f, 0.04f), Palette.Hex("ff2244"), 5f, false).AddComponent<Spinner>().Pulse = 0.3f;
                    Build.Cube(armR, new Vector3(0, -0.2f, 0.9f), new Vector3(0.09f, 0.09f, 0.3f), Palette.Hex("15121f"), 0f, false);
                    var laser = Build.Cube(armR, new Vector3(0, -0.3f, 13.5f), new Vector3(0.025f, 0.025f, 24f), Palette.Hex("ff2244"), 4f, false);
                    laser.name = "Laser";
                    laser.SetActive(false);
                    break;
                case Defs.KindBomber:
                    Build.Sphere(torso, new Vector3(0, 0.65f, -0.35f), 0.64f, color, 1.5f);
                    // Unstable core: a hot inner glow that flickers fast, wrapped in hazard bands.
                    Build.Sphere(torso, new Vector3(0, 0.65f, -0.35f), 0.4f, Palette.Hex("fff1a0"), 6f, false).AddComponent<Spinner>().Blink = 5f;
                    foreach (float y in new[] { 0.48f, 0.82f }) Build.Cube(torso, new Vector3(0, y, -0.35f), new Vector3(0.68f, 0.05f, 0.68f), Palette.Hex("1b1d20"), 0f, false);
                    break;
                case Defs.KindSplitter:
                case Defs.KindSplitling:
                    // Swollen, segmented bio-mass on the back that bursts into smaller copies.
                    for (int i = 0; i < 3; i++)
                        Build.Sphere(torso, new Vector3((i - 1) * 0.22f, 0.55f + (i % 2) * 0.14f, -0.32f), 0.3f, color, 1.4f);
                    Build.Cube(torso, new Vector3(0, 0.6f, -0.02f), new Vector3(0.72f, 0.05f, 0.72f), Palette.Hex("2a3a1e"), 0f, false);
                    Build.Cube(head, new Vector3(0, 0.12f, 0.39f), new Vector3(0.42f, 0.09f, 0.04f), Palette.Hex("e9ff9a"), 3f, false);
                    break;
                case Defs.KindMedic:
                {
                    // White cross on the chest and a backpack emitter with a turning ring.
                    Color white = Palette.Hex("eef7f5");
                    Build.Cube(torso, new Vector3(0, 0.6f, 0.37f), new Vector3(0.26f, 0.08f, 0.02f), white, 1.5f, false);
                    Build.Cube(torso, new Vector3(0, 0.6f, 0.37f), new Vector3(0.08f, 0.26f, 0.02f), white, 1.5f, false);
                    Build.Cube(torso, new Vector3(0, 0.62f, -0.42f), new Vector3(0.36f, 0.42f, 0.18f), Palette.Hex("1d2f2c"));
                    var emitter = new GameObject("Emitter").transform;
                    emitter.SetParent(torso, false);
                    emitter.localPosition = new Vector3(0, 1.15f, -0.4f);
                    Build.Sphere(emitter, Vector3.zero, 0.12f, color, 5f, false);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * Mathf.PI / 3f;
                        Build.Cube(emitter, new Vector3(Mathf.Cos(a) * 0.25f, 0, Mathf.Sin(a) * 0.25f), new Vector3(0.07f, 0.03f, 0.07f), color, 3f, false);
                    }
                    { var spin = emitter.gameObject.AddComponent<Spinner>(); spin.Speed = 120f; spin.Axis = Vector3.up; }
                    break;
                }
                case Defs.KindShield:
                    var plate = Build.Cube(elbowL, new Vector3(0.1f, -0.1f, 0.4f), new Vector3(1.05f, 1.35f, 0.1f), color, 0.6f);
                    plate.GetComponent<MeshRenderer>().sharedMaterial = Build.Mat(color, 0.6f, 0.55f);
                    // Frame around the energy plate and a heavy helmet.
                    foreach (var (at, size) in new[] { (new Vector3(0.1f, 0.58f, 0.4f), new Vector3(1.1f, 0.07f, 0.13f)), (new Vector3(0.1f, -0.78f, 0.4f), new Vector3(1.1f, 0.07f, 0.13f)),
                                                       (new Vector3(-0.43f, -0.1f, 0.4f), new Vector3(0.07f, 1.42f, 0.13f)), (new Vector3(0.63f, -0.1f, 0.4f), new Vector3(0.07f, 1.42f, 0.13f)) })
                        Build.Cube(elbowL, at, size, Palette.Hex("2a3a55"), 0f, false);
                    Build.Cube(head, new Vector3(0, 0.33f, 0.12f), new Vector3(0.62f, 0.12f, 0.56f), Palette.Hex("2a3a55"), 0f, false);
                    break;
                case Defs.KindGuard:
                {
                    // Imperial Guard: gold pauldrons, a crested helm, a halberd and a hexagonal energy shield. The
                    // ChargeGlow parts light up red while it winds up and rushes.
                    Color gold = Palette.Hex("d8a740"), red = Palette.Hex("ff3b3b");
                    foreach (float x in new[] { -0.46f, 0.46f }) Build.Cube(torso, new Vector3(x, 0.92f, 0), new Vector3(0.36f, 0.14f, 0.5f), gold);
                    Build.Cube(torso, new Vector3(0, 0.45f, 0.36f), new Vector3(0.5f, 0.42f, 0.04f), gold);
                    Build.Cube(head, new Vector3(0, 0.4f, 0.0f), new Vector3(0.06f, 0.22f, 0.5f), Palette.Hex("b8323a"));
                    Build.Cube(head, new Vector3(0, 0.3f, 0.12f), new Vector3(0.6f, 0.06f, 0.54f), gold);
                    var halberd = Build.Cube(armR, new Vector3(0, -0.3f, 0.35f), new Vector3(0.05f, 0.05f, 1.6f), Palette.Hex("3a3024"));
                    halberd.transform.localRotation = Quaternion.Euler(-12, 0, 0);
                    Build.Cube(armR, new Vector3(0, -0.12f, 1.12f), new Vector3(0.04f, 0.3f, 0.22f), gold, 0.8f);
                    var shield = new GameObject("GuardShield").transform;
                    shield.SetParent(elbowL, false);
                    shield.localPosition = new Vector3(0.12f, -0.1f, 0.45f);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * Mathf.PI / 3f;
                        var cell = Build.Cube(shield, new Vector3(Mathf.Cos(a) * 0.36f, Mathf.Sin(a) * 0.36f, 0), new Vector3(0.36f, 0.36f, 0.04f), color, 1.2f, false);
                        cell.transform.localRotation = Quaternion.Euler(0, 0, a * Mathf.Rad2Deg + 30f);
                        cell.GetComponent<MeshRenderer>().sharedMaterial = Build.Mat(color, 1.2f, 0.45f);
                    }
                    Build.Cube(shield, Vector3.zero, new Vector3(0.4f, 0.4f, 0.05f), gold, 1.5f, false);
                    var glow = new GameObject("ChargeGlow").transform;
                    glow.SetParent(root, false);
                    Build.Cube(glow, new Vector3(0, 1.62f, 0.32f), new Vector3(0.46f, 0.1f, 0.04f), red, 5f, false);
                    var aura = Build.Sphere(glow, new Vector3(0, 1.1f, 0), 1.6f, red, 3f, false);
                    aura.GetComponent<MeshRenderer>().sharedMaterial = Build.Mat(red, 3f, 0.18f);
                    glow.gameObject.SetActive(false);
                    break;
                }
            }
            root.localScale = Vector3.one * def.Scale;
            CharacterRig.Attach(root.gameObject, CharacterRig.Style.Trooper);
            return root.gameObject;
        }

        /// Emperor Helio, the final boss: white-and-gold armour over a long robe, a spiked crown with a rotating
        /// halo, a crimson cape and a sceptre topped with a burning orb. Uses the boss rig's joints.
        public static GameObject Emperor()
        {
            var root = new GameObject("Emperor").transform;
            Color ivory = Palette.Hex("e8e0cc"), gold = Palette.Hex("d9a73e"), crimson = Palette.Hex("8a1c2c"), black = Palette.Hex("1c1a1f");
            Color glow = Defs.Kinds[Defs.KindEmperor].Color, hot = Palette.Hex("fff1b0");
            var hips = Joint(root, "Hips", new Vector3(0, 0.86f, 0));
            foreach (var (side, x) in new[] { ("L", -0.17f), ("R", 0.17f) })
            {
                var leg = Joint(hips, "Leg" + side, new Vector3(x, 0, 0));
                Build.Cube(leg, new Vector3(0, -0.2f, 0), new Vector3(0.24f, 0.42f, 0.28f), ivory);
                var knee = Joint(leg, "Knee" + side, new Vector3(0, -0.4f, 0));
                Build.Cube(knee, new Vector3(0, -0.02f, 0.15f), new Vector3(0.2f, 0.16f, 0.04f), gold);
                Build.Cube(knee, new Vector3(0, -0.2f, 0), new Vector3(0.22f, 0.4f, 0.26f), ivory);
                Build.Cube(knee, new Vector3(0, -0.38f, 0.05f), new Vector3(0.28f, 0.14f, 0.4f), gold);
            }
            // Robe skirt: overlapping panels that hang from the hips.
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                var panel = Build.Cube(hips, new Vector3(Mathf.Sin(a) * 0.3f, -0.28f, Mathf.Cos(a) * 0.24f), new Vector3(0.28f, 0.56f, 0.04f), i % 2 == 0 ? crimson : ivory);
                panel.transform.localRotation = Quaternion.Euler(-10, a * Mathf.Rad2Deg, 0);
            }
            var torso = Joint(hips, "Torso", Vector3.zero);
            Build.Cube(torso, new Vector3(0, 0.04f, 0), new Vector3(0.6f, 0.2f, 0.34f), gold);
            Build.Cube(torso, new Vector3(0, 0.39f, 0), new Vector3(0.74f, 0.6f, 0.42f), ivory);
            Build.Cube(torso, new Vector3(0, 0.42f, 0.22f), new Vector3(0.5f, 0.42f, 0.04f), gold);
            var core = Build.Sphere(torso, new Vector3(0, 0.45f, 0.25f), 0.16f, glow, 5f, false);
            core.AddComponent<Spinner>().Pulse = 0.25f;
            foreach (float x in new[] { -0.52f, 0.52f })
            {
                Build.Cube(torso, new Vector3(x, 0.72f, 0), new Vector3(0.38f, 0.2f, 0.52f), gold);
                Build.Cube(torso, new Vector3(x, 0.86f, 0), new Vector3(0.06f, 0.18f, 0.06f), gold).transform.localRotation = Quaternion.Euler(0, 0, x < 0 ? 20 : -20);
            }
            var cape = Build.Cube(torso, new Vector3(0, 0.05f, -0.28f), new Vector3(0.9f, 1.5f, 0.04f), crimson);
            cape.transform.localRotation = Quaternion.Euler(-10, 0, 0);
            Build.Cube(torso, new Vector3(0, 0.78f, -0.26f), new Vector3(0.92f, 0.08f, 0.06f), gold);
            var head = Joint(torso, "Head", new Vector3(0, 0.8f, 0));
            Build.Cube(head, new Vector3(0, 0.06f, 0), new Vector3(0.38f, 0.38f, 0.4f), ivory);
            Build.Cube(head, new Vector3(0, 0.06f, 0.205f), new Vector3(0.3f, 0.06f, 0.02f), glow, 4f);
            // Spiked crown and a slowly turning halo behind it.
            Build.Cube(head, new Vector3(0, 0.27f, 0), new Vector3(0.42f, 0.06f, 0.44f), gold);
            for (int i = 0; i < 5; i++)
            {
                float x = -0.16f + i * 0.08f;
                Build.Cube(head, new Vector3(x, 0.36f + (i == 2 ? 0.06f : 0f), 0.18f), new Vector3(0.04f, i == 2 ? 0.2f : 0.12f, 0.04f), gold, 0.6f);
            }
            var halo = new GameObject("Halo").transform;
            halo.SetParent(head, false);
            halo.localPosition = new Vector3(0, 0.22f, -0.28f);
            halo.gameObject.AddComponent<Spinner>().Speed = 25f;
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI / 6f;
                var ray = Build.Cube(halo, new Vector3(Mathf.Cos(a) * 0.42f, Mathf.Sin(a) * 0.42f, 0), new Vector3(0.05f, 0.18f, 0.02f), i % 2 == 0 ? hot : glow, 4f, false);
                ray.transform.localRotation = Quaternion.Euler(0, 0, a * Mathf.Rad2Deg - 90f);
            }
            foreach (var (side, x) in new[] { ("L", -0.52f), ("R", 0.52f) })
            {
                var arm = Joint(torso, "Arm" + side, new Vector3(x, 0.6f, 0));
                Build.Cube(arm, new Vector3(0, -0.16f, 0), new Vector3(0.2f, 0.32f, 0.22f), ivory);
                var elbow = Joint(arm, "Elbow" + side, new Vector3(0, -0.3f, 0));
                Build.Cube(elbow, new Vector3(0, -0.12f, 0), new Vector3(0.18f, 0.26f, 0.2f), ivory);
                Build.Cube(elbow, new Vector3(0, -0.28f, 0.04f), new Vector3(0.22f, 0.12f, 0.24f), gold);
                if (side == "R")
                {
                    // Sceptre held forward, burning orb at the tip (also where his volleys come from).
                    var staff = Build.Cube(elbow, new Vector3(0, -0.3f, 0.3f), new Vector3(0.05f, 0.05f, 1.1f), gold);
                    staff.transform.localRotation = Quaternion.Euler(-30, 0, 0);
                    var orb = Build.Sphere(elbow, new Vector3(0, -0.02f, 0.78f), 0.2f, hot, 6f, false);
                    orb.AddComponent<Spinner>().Pulse = 0.35f;
                }
            }
            root.localScale = Vector3.one * Defs.Kinds[Defs.KindEmperor].Scale;
            CharacterRig.Attach(root.gameObject, CharacterRig.Style.Boss);
            return root.gameObject;
        }

        /// Wave 10 / 20 bosses: oversized armoured soldiers with an arm cannon and a heavy stomping walk.
        public static GameObject Boss(int kind)
        {
            bool commander = kind == Defs.KindCommander;
            var root = new GameObject("Boss").transform;
            Color armor = commander ? Palette.Hex("2c2438") : Palette.Hex("3a2a2a");
            Color plate = commander ? Palette.Hex("8a6a3a") : Palette.Hex("6b3a30");
            Color glow = Defs.Kinds[kind].Color, black = Palette.Hex("1c1a1f");
            var hips = Joint(root, "Hips", new Vector3(0, 0.86f, 0));
            foreach (var (side, x) in new[] { ("L", -0.18f), ("R", 0.18f) })
            {
                var leg = Joint(hips, "Leg" + side, new Vector3(x, 0, 0));
                Build.Cube(leg, new Vector3(0, -0.2f, 0), new Vector3(0.26f, 0.42f, 0.3f), armor);
                var knee = Joint(leg, "Knee" + side, new Vector3(0, -0.4f, 0));
                Build.Cube(knee, new Vector3(0, -0.02f, 0.16f), new Vector3(0.2f, 0.18f, 0.04f), plate);
                Build.Cube(knee, new Vector3(0, -0.2f, 0), new Vector3(0.24f, 0.4f, 0.28f), armor);
                Build.Cube(knee, new Vector3(0, -0.38f, 0.05f), new Vector3(0.3f, 0.16f, 0.42f), black);
                if (commander)
                {
                    // Weak point: exposed knee actuators that glow and pulse.
                    var joint = Build.Sphere(knee, new Vector3(0, 0, 0.1f), 0.26f, Palette.Hex("ff5a3c"), 7f, false);
                    joint.AddComponent<Spinner>().Pulse = 0.25f;
                    foreach (float y in new[] { 0.1f, -0.1f }) Build.Cube(knee, new Vector3(0, y, 0.1f), new Vector3(0.22f, 0.025f, 0.04f), black, 0f, false);
                }
            }
            var torso = Joint(hips, "Torso", Vector3.zero);
            Build.Cube(torso, new Vector3(0, 0.04f, 0), new Vector3(0.62f, 0.2f, 0.34f), black);
            Build.Cube(torso, new Vector3(0, 0.39f, 0), new Vector3(0.78f, 0.62f, 0.44f), armor);
            Build.Cube(torso, new Vector3(0, 0.42f, 0.23f), new Vector3(0.6f, 0.46f, 0.04f), plate);
            Build.Cube(torso, new Vector3(0, 0.44f, 0.255f), new Vector3(0.36f, 0.05f, 0.02f), glow, 2.5f);
            if (!commander)
            {
                // Weak point: a reactor core set into the chest plate, ringed by vents.
                Build.Cube(torso, new Vector3(0, 0.42f, 0.25f), new Vector3(0.3f, 0.3f, 0.03f), black, 0f, false);
                var reactor = Build.Sphere(torso, new Vector3(0, 0.42f, 0.25f), 0.2f, Palette.Hex("ff6a2a"), 6f, false);
                reactor.AddComponent<Spinner>().Pulse = 0.3f;
                foreach (float a in new[] { 0f, 90f })
                    Build.Cube(torso, new Vector3(0, 0.42f, 0.275f), new Vector3(0.3f, 0.03f, 0.02f), black, 0f, false).transform.localRotation = Quaternion.Euler(0, 0, a + 45f);
            }
            Build.Cube(torso, new Vector3(0, 0.74f, 0), new Vector3(0.22f, 0.12f, 0.22f), black);
            foreach (float x in new[] { -0.56f, 0.56f }) Build.Cube(torso, new Vector3(x, 0.69f, 0), new Vector3(0.36f, 0.22f, 0.5f), plate);
            var head = Joint(torso, "Head", new Vector3(0, 0.8f, 0));
            Build.Cube(head, new Vector3(0, 0.06f, 0), new Vector3(0.42f, 0.36f, 0.44f), plate);
            Build.Cube(head, new Vector3(0, 0.06f, 0.225f), new Vector3(0.34f, 0.08f, 0.02f), glow, 3f);
            foreach (var (side, x) in new[] { ("L", -0.56f), ("R", 0.56f) })
            {
                var arm = Joint(torso, "Arm" + side, new Vector3(x, 0.62f, 0));
                Build.Cube(arm, new Vector3(0, -0.16f, 0), new Vector3(0.22f, 0.32f, 0.24f), armor);
                var elbow = Joint(arm, "Elbow" + side, new Vector3(0, -0.3f, 0));
                Build.Cube(elbow, new Vector3(0, -0.12f, 0), new Vector3(0.2f, 0.26f, 0.22f), armor);
                Build.Cube(elbow, new Vector3(0, -0.3f, 0.06f), new Vector3(0.24f, 0.16f, 0.28f), black);
                if (side == "R")
                {
                    Build.Cube(arm, new Vector3(0, -0.48f, 0.42f), new Vector3(0.24f, 0.26f, 0.95f), Palette.Hex("1a1a20"));
                    Build.Cube(arm, new Vector3(0, -0.48f, 0.95f), new Vector3(0.16f, 0.16f, 0.2f), Palette.Hex("2e2e36"));
                    Build.Cube(arm, new Vector3(0, -0.48f, 1.06f), new Vector3(0.12f, 0.12f, 0.03f), glow, 3f);
                }
            }
            if (commander)
            {
                Build.Cube(head, new Vector3(0, 0.34f, -0.02f), new Vector3(0.06f, 0.24f, 0.46f), glow, 1.2f);
                Build.Cube(torso, new Vector3(0, 0.19f, -0.3f), new Vector3(0.74f, 1.1f, 0.04f), Palette.Hex("5a1f2a")).transform.localRotation = Quaternion.Euler(-8, 0, 0);
                foreach (float x in new[] { -0.56f, 0.56f }) Build.Cube(torso, new Vector3(x, 0.82f, 0), new Vector3(0.3f, 0.06f, 0.44f), glow, 1.2f);
            }
            root.localScale = Vector3.one * Defs.Kinds[kind].Scale;
            CharacterRig.Attach(root.gameObject, CharacterRig.Style.Boss);
            return root.gameObject;
        }

        /// First-person weapon models, parented to the camera. Returns the muzzle transform.
        /// Each model names its magazine "Mag" so the reload animation can pull it out.
        /// Where the first-person gun sits (camera space) when aimed down sights: its sight on the view axis.
        public static Vector3 AdsPose(string weapon)
        {
            switch (weapon)
            {
                case "rifle": return new Vector3(0, -0.095f, 0.3f);      // through the holo window, dot centred
                case "shotgun": return new Vector3(0, -0.057f, 0.17f);   // along the vent rib to the bead
                case "sniper": return new Vector3(0, -0.1f, 0.1f);       // eye to the scope (the overlay takes over)
                default: return new Vector3(0, -0.058f, 0.3f);           // pistol: front post between the rear dots
            }
        }

        public static Transform Viewmodel(string weapon, Transform holder, int rarity = 1, bool firstPerson = false)
        {
            // Detach the old gun before destroying it: Destroy only happens at the end of the frame, and lookups by
            // name (Slide, Mag, Ammo cells) on the new gun must not find the old one's parts.
            for (int i = holder.childCount - 1; i >= 0; i--)
            {
                var old = holder.GetChild(i).gameObject;
                old.SetActive(false);
                old.transform.SetParent(null, false);
                Object.Destroy(old);
            }
            float muzzleZ;
            switch (weapon)
            {
                case "rifle": muzzleZ = Rifle(holder); break;
                case "shotgun": muzzleZ = Shotgun(holder); break;
                case "sniper": muzzleZ = Sniper(holder); break;
                case "saber": muzzleZ = Saber(holder, rarity); break;
                default: muzzleZ = Pistol(holder); break;
            }
            bool melee = weapon == "saber";
            if (!melee)   // the gun details and rarity finish are shaped for guns; the blade shows rarity itself
            {
                Detail(holder, muzzleZ);
                WeaponFinish(holder, rarity, muzzleZ);
            }
            Surfaces(holder);
            if (firstPerson && !melee)
            {
                AmmoBar(holder);
                // Heat sleeve over the end of the barrel: invisible when cold, glows orange as the gun heats up.
                var heat = P(holder, new Vector3(0, heatY, muzzleZ - heatLength * 0.5f - 0.01f), new Vector3(heatWidth, heatWidth, heatLength), Palette.Hex("ff6a1a"), 3f);
                heat.name = "Heat";
                heat.GetComponent<MeshRenderer>().enabled = false;
            }
            foreach (var c in holder.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            Build.NoShadows(holder.gameObject);
            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(holder, false);
            muzzle.localPosition = new Vector3(0, 0.01f, muzzleZ);
            return muzzle;
        }

        /// Rarity finish layered over any weapon: rust for 劣質, glowing inlays for 稀世+, gold trim for 傳奇+,
        /// a red core and floating rift shards for 神話.
        static void WeaponFinish(Transform h, int rarity, float length)
        {
            Color glow = Gear.RarityColors[Mathf.Clamp(rarity, 0, 4)];
            if (rarity == 0)
            {
                foreach (var (x, y, z) in new[] { (0.044f, 0.0f, 0.05f), (-0.044f, -0.03f, -0.08f), (0.044f, -0.05f, 0.18f) })
                    P(h, new Vector3(x, y, z), new Vector3(0.004f, 0.026f, 0.05f), Palette.Hex("6a4a2c"));
                return;
            }
            if (rarity >= 2)
                foreach (float x in new[] { -0.046f, 0.046f })
                    P(h, new Vector3(x, 0.012f, length * 0.35f), new Vector3(0.004f, 0.006f, length * 0.55f), glow, 3f);
            if (rarity < 3) return;

            // 傳奇 / 神話: re-skin the whole gun first (dark body, precious-metal edges), then add the extras.
            bool myth = rarity >= 4;
            Color dark = Palette.Hex(myth ? "160a0d" : "221a12"), metal = Palette.Hex(myth ? "7a1c24" : "c9a24a");
            foreach (var r in h.GetComponentsInChildren<MeshRenderer>())
            {
                var m = r.sharedMaterial;
                if (m == null || m.IsKeywordEnabled("_EMISSION")) continue;
                Color c = m.color;
                float lum = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
                r.sharedMaterial = Build.Mat(lum < 0.2f ? Color.Lerp(dark, c, 0.25f) : metal, lum < 0.2f ? 0f : 0.5f);
            }
            Color hot = Color.Lerp(glow, Color.white, 0.35f);

            // Engraved trim along the top and both flanks.
            P(h, new Vector3(0, 0.047f, length * 0.25f), new Vector3(0.012f, 0.005f, length * 0.6f), metal, 1.2f);
            foreach (float x in new[] { -0.047f, 0.047f })
            {
                P(h, new Vector3(x, -0.022f, length * 0.1f), new Vector3(0.004f, 0.006f, length * 0.5f), metal, 1.2f);
                // Pulsing energy vein in the rarity colour.
                var vein = P(h, new Vector3(x * 1.02f, 0.0f, length * 0.4f), new Vector3(0.005f, 0.01f, length * 0.62f), hot, 6f);
                vein.AddComponent<Spinner>().Pulse = 0.12f;
            }

            // Three glowing coolant rings around the barrel, pulsing out of phase.
            for (int i = 0; i < 3; i++)
            {
                var ring = P(h, new Vector3(0, 0.012f, length * (0.62f + i * 0.11f)), new Vector3(0.06f, 0.06f, 0.012f), glow, 5f);
                ring.transform.localRotation = Quaternion.Euler(0, 0, 45f);
                var sp = ring.AddComponent<Spinner>();
                sp.Pulse = 0.18f; sp.Speed = 90f * (i % 2 == 0 ? 1 : -1);
            }

            // Visible core in the receiver.
            var core = P(h, new Vector3(0, 0.01f, length * 0.05f), new Vector3(0.03f, 0.03f, 0.05f), hot, 7f);
            core.AddComponent<Spinner>().Pulse = 0.3f;

            // Muzzle energy: an orb plus a ring of shards (two counter-rotating rings for 神話).
            var orb = P(h, new Vector3(0, 0.01f, length + 0.01f), new Vector3(0.022f, 0.022f, 0.022f), hot, 8f);
            orb.AddComponent<Spinner>().Pulse = 0.4f;
            for (int ringIndex = 0; ringIndex < (myth ? 2 : 1); ringIndex++)
            {
                var halo = new GameObject("Halo").transform;
                halo.SetParent(h, false);
                halo.localPosition = new Vector3(0, 0.01f, length - 0.02f - ringIndex * 0.05f);
                halo.gameObject.AddComponent<Spinner>().Speed = ringIndex == 0 ? 220f : -160f;
                int count = myth ? 6 : 4;
                float radius = ringIndex == 0 ? 0.075f : 0.1f;
                for (int i = 0; i < count; i++)
                {
                    float a = i * Mathf.PI * 2f / count;
                    var shard = P(halo, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0), new Vector3(0.012f, 0.012f, 0.03f), glow, 5f);
                    shard.transform.localRotation = Quaternion.Euler(0, 0, a * Mathf.Rad2Deg + 45f);
                }
            }

            if (!myth) return;
            // 神話: crystal spikes on top and rift fragments orbiting the whole gun.
            for (int i = 0; i < 3; i++)
            {
                var spike = P(h, new Vector3(0, 0.06f + i * 0.004f, length * (0.15f + i * 0.12f)), new Vector3(0.014f, 0.04f - i * 0.008f, 0.014f), hot, 6f);
                spike.transform.localRotation = Quaternion.Euler(-20f, 45f, 0);
            }
            var orbit = new GameObject("RiftOrbit").transform;
            orbit.SetParent(h, false);
            orbit.localPosition = new Vector3(0, 0.01f, length * 0.4f);
            var spin = orbit.gameObject.AddComponent<Spinner>();
            spin.Speed = 70f;
            for (int i = 0; i < 5; i++)
            {
                float a = i * Mathf.PI * 2f / 5f;
                var frag = P(orbit, new Vector3(Mathf.Cos(a) * 0.11f, Mathf.Sin(a) * 0.11f, (i - 2) * length * 0.15f), new Vector3(0.01f, 0.018f, 0.01f), glow, 6f);
                frag.transform.localRotation = Quaternion.Euler(30, a * Mathf.Rad2Deg, 20);
                frag.AddComponent<Spinner>().Blink = i % 2 == 0 ? 0f : 0.7f;
            }
        }

        /// Display model for a gear item. Silhouette depends on the slot; colour, glow and extra parts on rarity.
        public static void GearModel(Transform root, string slot, int rarity)
        {
            rarity = Mathf.Clamp(rarity, 0, 4);
            Color[] bodies = { Palette.Hex("5a5650"), Palette.Hex("4b5a3e"), Palette.Hex("2c2a3e"), Palette.Hex("3a2e22"), Palette.Hex("2a1416") };
            Color[] trims = { Palette.Hex("3a3835"), Palette.Hex("2c3328"), Palette.Hex("1d1b2a"), Palette.Hex("c9a24a"), Palette.Hex("1a0c0e") };
            Color body = bodies[rarity], trim = trims[rarity], glow = Gear.RarityColors[rarity];
            float g = rarity == 0 ? 0f : 1f + rarity * 0.8f;
            GameObject C(Vector3 at, Vector3 size, Color c, float e = 0f) => Build.Cube(root, at, size, c, e, false);
            switch (slot)
            {
                case "armor":
                    C(new Vector3(0, 0, 0), new Vector3(0.9f, 1.0f, 0.4f), body);
                    C(new Vector3(0, 0.2f, 0.21f), new Vector3(0.7f, 0.45f, 0.04f), trim);
                    foreach (float x in new[] { -0.55f, 0.55f }) { C(new Vector3(x, 0.4f, 0), new Vector3(0.3f, 0.2f, 0.5f), trim); C(new Vector3(x, 0.3f, 0), new Vector3(0.24f, 0.08f, 0.52f), body); }
                    C(new Vector3(0, 0.56f, 0), new Vector3(0.5f, 0.12f, 0.44f), trim);
                    for (int i = 0; i < 3; i++) C(new Vector3(0, -0.12f - i * 0.13f, 0.21f), new Vector3(0.6f - i * 0.06f, 0.1f, 0.05f), body * 1.15f);
                    if (rarity >= 1) C(new Vector3(0, 0.2f, 0.235f), new Vector3(0.18f, 0.18f, 0.02f), glow, g);
                    if (rarity >= 2) foreach (float x in new[] { -0.3f, 0.3f }) C(new Vector3(x, -0.1f, 0.23f), new Vector3(0.03f, 0.5f, 0.02f), glow, g);
                    if (rarity >= 3) foreach (float x in new[] { -0.55f, 0.55f }) C(new Vector3(x, 0.52f, 0), new Vector3(0.32f, 0.04f, 0.52f), Palette.Hex("c9a24a"), 0.6f);
                    break;
                case "module":
                    C(Vector3.zero, new Vector3(0.36f, 0.3f, 0.9f), body);
                    C(new Vector3(0, -0.2f, 0), new Vector3(0.2f, 0.1f, 0.8f), trim);
                    var lens = Build.Cylinder(root, new Vector3(0, 0.02f, 0.5f), 0.32f, 0.12f, trim);
                    lens.transform.localRotation = Quaternion.Euler(90, 0, 0);
                    C(new Vector3(0, 0.02f, 0.57f), new Vector3(0.2f, 0.2f, 0.02f), rarity == 0 ? Palette.Hex("4a5058") : glow, rarity == 0 ? 0.3f : g + 1f);
                    C(new Vector3(0, 0.2f, -0.1f), new Vector3(0.12f, 0.1f, 0.3f), trim);
                    if (rarity >= 2) foreach (float x in new[] { -0.185f, 0.185f }) C(new Vector3(x, 0.05f, 0), new Vector3(0.02f, 0.04f, 0.7f), glow, g);
                    if (rarity >= 3) C(new Vector3(0, 0.16f, 0.2f), new Vector3(0.3f, 0.03f, 0.3f), Palette.Hex("c9a24a"), 0.6f);
                    break;
                case "boots":
                    foreach (float x in new[] { -0.26f, 0.26f })
                    {
                        C(new Vector3(x, 0.15f, -0.05f), new Vector3(0.3f, 0.6f, 0.34f), body);
                        C(new Vector3(x, -0.18f, 0.1f), new Vector3(0.32f, 0.2f, 0.6f), body * 0.9f);
                        C(new Vector3(x, -0.3f, 0.1f), new Vector3(0.34f, 0.06f, 0.64f), trim);
                        C(new Vector3(x, 0.3f, 0.12f), new Vector3(0.26f, 0.14f, 0.04f), trim);
                        if (rarity >= 1) C(new Vector3(x, -0.18f, 0.41f), new Vector3(0.2f, 0.05f, 0.02f), glow, g);
                        if (rarity >= 2) C(new Vector3(x, -0.05f, -0.23f), new Vector3(0.14f, 0.3f, 0.06f), glow, g);
                        if (rarity >= 3) C(new Vector3(x, 0.44f, -0.05f), new Vector3(0.32f, 0.04f, 0.36f), Palette.Hex("c9a24a"), 0.6f);
                    }
                    break;
                default: // chip
                    C(Vector3.zero, new Vector3(0.9f, 0.08f, 0.9f), body);
                    C(new Vector3(0, 0.06f, 0), new Vector3(0.36f, 0.06f, 0.36f), trim);
                    C(new Vector3(0, 0.1f, 0), new Vector3(0.2f, 0.03f, 0.2f), rarity == 0 ? Palette.Hex("4a5058") : glow, rarity == 0 ? 0.2f : g + 1f);
                    for (int i = 0; i < 6; i++)
                    {
                        float off = -0.35f + i * 0.14f;
                        C(new Vector3(off, 0.045f, 0.33f), new Vector3(0.04f, 0.02f, 0.18f), Palette.Hex("c9a24a"), 0.3f);
                        C(new Vector3(off, 0.045f, -0.33f), new Vector3(0.04f, 0.02f, 0.18f), Palette.Hex("c9a24a"), 0.3f);
                    }
                    if (rarity >= 2) foreach (float z in new[] { -0.15f, 0.15f }) C(new Vector3(0.28f, 0.05f, z), new Vector3(0.24f, 0.02f, 0.02f), glow, g);
                    if (rarity >= 3) C(new Vector3(0, 0.05f, 0), new Vector3(0.94f, 0.02f, 0.04f), Palette.Hex("c9a24a"), 0.6f);
                    break;
            }
            if (rarity >= 4)
            {
                // 神話: a halo of floating rift shards.
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.PI / 3f;
                    var shard = C(new Vector3(Mathf.Cos(a) * 0.75f, 0.15f * Mathf.Sin(a * 2f), Mathf.Sin(a) * 0.75f), new Vector3(0.06f, 0.14f, 0.06f), glow, 4f);
                    shard.transform.localRotation = Quaternion.Euler(30, a * Mathf.Rad2Deg, 20);
                }
            }
            foreach (var c in root.GetComponentsInChildren<Collider>()) Object.Destroy(c);
        }

        /// Display model for a weapon (the first-person model with its rarity finish).
        public static void WeaponDisplay(Transform root, string weapon, int rarity)
        {
            var holder = new GameObject("Weapon").transform;
            holder.SetParent(root, false);
            holder.localRotation = Quaternion.Euler(0, -90, 0);
            Viewmodel(weapon, holder, rarity);
        }

        static readonly Color GunInk = Palette.Hex("17191c"), GunSteel = Palette.Hex("2c3036"), GunPolymer = Palette.Hex("262a2f"),
            GunTan = Palette.Hex("8a7a5c"), GunEdge = Palette.Hex("4a5058");

        static GameObject P(Transform t, Vector3 at, Vector3 size, Color c, float glow = 0f) => Build.Cube(t, at, size, c, glow, false);

        // Per-gun anchors set by the builders: where the ammo bar sits (left flank or top, the faces the
        // first-person camera sees) and where the barrel's heat sleeve goes.
        static Vector3 ammoAt, ammoCell;
        static float ammoLength, heatY, heatWidth, heatLength;
        public const int AmmoCells = 10;

        /// Row of cells on the gun that light up for the rounds left in the magazine (named Ammo0..Ammo9).
        static void AmmoBar(Transform h)
        {
            var bar = Part(h.Find("Slide") ?? h, "AmmoBar", ammoAt);   // rides on the pistol's slide
            bool top = ammoCell.y < ammoCell.x;
            P(bar, Vector3.zero, top ? new Vector3(ammoCell.x + 0.006f, 0.003f, ammoLength + 0.012f) : new Vector3(0.003f, ammoCell.y + 0.006f, ammoLength + 0.012f), GunInk);
            float step = ammoLength / AmmoCells;
            for (int i = 0; i < AmmoCells; i++)
            {
                var cell = P(bar, new Vector3(top ? 0 : -0.0015f, top ? 0.0015f : 0, -ammoLength * 0.5f + step * (i + 0.5f)), new Vector3(ammoCell.x, ammoCell.y, step * 0.72f), AmmoOff);
                cell.name = "Ammo" + i;
            }
        }

        public static readonly Color AmmoLit = Palette.Hex("6fd3ff"), AmmoLow = Palette.Hex("ff4d4d"), AmmoOff = Palette.Hex("1f2328");

        /// Small manufacturing details every gun shares: a hazard-striped band and a stamped plate on the left flank.
        static void Detail(Transform h, float muzzleZ)
        {
            bool top = ammoCell.y < ammoCell.x;
            float x = top ? -0.0465f : ammoAt.x;
            if (muzzleZ > 0.3f)   // long guns: hazard band ahead of the receiver (the pistol's slide moves, so none there)
            {
                float z = Mathf.Min(muzzleZ - 0.12f, ammoAt.z + ammoLength * 0.5f + 0.05f);
                for (int i = 0; i < 4; i++)
                    P(h, new Vector3(x - 0.0005f, heatY - 0.012f, z + i * 0.008f), new Vector3(0.002f, 0.01f, 0.006f), i % 2 == 0 ? Palette.Hex("e8b23a") : GunInk);
            }
            if (!top) P(h, new Vector3(x - 0.0005f, ammoAt.y - 0.018f, ammoAt.z), new Vector3(0.002f, 0.008f, 0.04f), GunEdge);   // stamped plate
        }

        /// Material pass: gunmetal parts become polished metal, polymer stays satin, wood stays dull; glowing
        /// parts are left alone. Shared materials, cached per colour.
        static void Surfaces(Transform h)
        {
            foreach (var r in h.GetComponentsInChildren<MeshRenderer>())
            {
                var m = r.sharedMaterial;
                if (m == null || m.IsKeywordEnabled("_EMISSION") || m.color.a < 1f) continue;
                r.sharedMaterial = SurfaceFor(m.color);
            }
        }

        static readonly Dictionary<Color, Material> surfaces = new Dictionary<Color, Material>();

        static Material SurfaceFor(Color c)
        {
            if (surfaces.TryGetValue(c, out var m)) return m;
            Color.RGBToHSV(c, out float hue, out float sat, out float val);
            m = new Material(Build.Mat(c));
            bool wood = sat > 0.3f && hue > 0.03f && hue < 0.12f && val < 0.45f;
            bool metal = sat < 0.2f && val < 0.3f;
            float smooth = wood ? 0.22f : metal ? (val > 0.14f ? 0.72f : 0.6f) : 0.42f;
            m.SetFloat("_Glossiness", smooth);
            m.SetFloat("_Metallic", wood ? 0f : metal ? 0.75f : 0.15f);
            surfaces[c] = m;
            return m;
        }

        static Transform Part(Transform holder, string name, Vector3 at)
        {
            var t = new GameObject(name).transform;
            t.SetParent(holder, false);
            t.localPosition = at;
            return t;
        }

        static void Rail(Transform t, float y, float z0, float z1, float x = 0f)
        {
            P(t, new Vector3(x, y, (z0 + z1) / 2f), new Vector3(0.034f, 0.008f, z1 - z0), GunInk);
            for (float z = z0 + 0.012f; z < z1; z += 0.024f) P(t, new Vector3(x, y + 0.008f, z), new Vector3(0.038f, 0.008f, 0.01f), GunSteel);
        }

        static void Barrel(Transform t, Vector3 at, float diameter, float length, Color c)
        {
            Build.Cylinder(t, at, diameter, length, c).transform.localRotation = Quaternion.Euler(90, 0, 0);
        }

        /// AR-27: two-tone receiver, ventilated handguard, holo sight, curved magazine, flash hider and adjustable stock.
        static float Rifle(Transform h)
        {
            P(h, new Vector3(0, 0, -0.02f), new Vector3(0.085f, 0.075f, 0.3f), GunSteel);                 // upper receiver
            P(h, new Vector3(0, -0.055f, -0.03f), new Vector3(0.08f, 0.045f, 0.22f), GunTan);             // lower receiver
            P(h, new Vector3(0.044f, 0.005f, -0.03f), new Vector3(0.004f, 0.03f, 0.06f), GunInk);          // ejection port
            P(h, new Vector3(0.044f, -0.05f, 0.0f), new Vector3(0.006f, 0.012f, 0.02f), Palette.Amber, 0.8f); // selector
            P(h, new Vector3(0, 0.005f, 0.22f), new Vector3(0.078f, 0.07f, 0.2f), GunPolymer);            // handguard
            for (int i = 0; i < 5; i++)
                foreach (float x in new[] { -0.04f, 0.04f })
                    P(h, new Vector3(x, 0.005f, 0.15f + i * 0.035f), new Vector3(0.004f, 0.022f, 0.016f), GunInk);  // vent slots
            Rail(h, 0.042f, -0.14f, 0.3f);
            Barrel(h, new Vector3(0, 0.005f, 0.4f), 0.026f, 0.18f, GunSteel);
            Barrel(h, new Vector3(0, 0.005f, 0.5f), 0.036f, 0.06f, GunInk);                                // flash hider
            P(h, new Vector3(0, 0.03f, 0.29f), new Vector3(0.012f, 0.03f, 0.012f), GunInk);                 // front post
            // Holo sight
            P(h, new Vector3(0, 0.062f, -0.02f), new Vector3(0.05f, 0.022f, 0.08f), GunInk);
            // Open window frame (you look through it when aiming), a cyan lens edge and the floating red dot.
            foreach (var (at, size) in new[] { (new Vector3(0, 0.1165f, 0), new Vector3(0.054f, 0.007f, 0.01f)), (new Vector3(0, 0.0735f, 0), new Vector3(0.054f, 0.007f, 0.01f)),
                                               (new Vector3(-0.0235f, 0.095f, 0), new Vector3(0.007f, 0.05f, 0.01f)), (new Vector3(0.0235f, 0.095f, 0), new Vector3(0.007f, 0.05f, 0.01f)) })
                P(h, at, size, GunInk);
            foreach (var (at, size) in new[] { (new Vector3(0, 0.1125f, 0.004f), new Vector3(0.04f, 0.0015f, 0.002f)), (new Vector3(0, 0.0775f, 0.004f), new Vector3(0.04f, 0.0015f, 0.002f)) })
                P(h, at, size, Palette.Hex("6fd3ff"), 1.2f);
            P(h, new Vector3(0, 0.095f, 0.006f), new Vector3(0.0042f, 0.0042f, 0.001f), Palette.Hex("ff3b3b"), 6f);
            // Grip, trigger guard and stock
            var grip = P(h, new Vector3(0, -0.11f, -0.1f), new Vector3(0.05f, 0.11f, 0.055f), GunPolymer);
            grip.transform.localRotation = Quaternion.Euler(-16, 0, 0);
            P(h, new Vector3(0, -0.09f, -0.04f), new Vector3(0.012f, 0.008f, 0.06f), GunInk);
            P(h, new Vector3(0, -0.065f, -0.4f + 0.18f), new Vector3(0.03f, 0.03f, 0.18f), GunInk);       // buffer tube
            P(h, new Vector3(0, -0.06f, -0.34f), new Vector3(0.06f, 0.1f, 0.08f), GunPolymer);              // stock
            P(h, new Vector3(0, -0.06f, -0.385f), new Vector3(0.064f, 0.108f, 0.012f), GunInk);             // butt pad
            P(h, new Vector3(0.045f, 0.01f, 0.1f), new Vector3(0.004f, 0.006f, 0.12f), Palette.Amber, 0.6f); // accent line
            var mag = Part(h, "Mag", new Vector3(0, -0.12f, 0.04f));
            P(mag, Vector3.zero, new Vector3(0.05f, 0.13f, 0.07f), GunInk).transform.localRotation = Quaternion.Euler(14, 0, 0);
            P(mag, new Vector3(0, -0.07f, 0.02f), new Vector3(0.056f, 0.02f, 0.078f), GunSteel).transform.localRotation = Quaternion.Euler(14, 0, 0);
            var bolt = Part(h, "Bolt", new Vector3(-0.047f, 0.012f, 0.035f));                              // cycles on every shot
            P(bolt, Vector3.zero, new Vector3(0.006f, 0.022f, 0.05f), GunEdge);
            P(bolt, new Vector3(-0.012f, 0.002f, -0.018f), new Vector3(0.02f, 0.012f, 0.012f), GunSteel);   // handle knob
            ammoAt = new Vector3(-0.0405f, 0.029f, 0.225f); ammoCell = new Vector3(0.003f, 0.009f, 0); ammoLength = 0.13f;   // handguard flank, above the vents
            heatY = 0.005f; heatWidth = 0.034f; heatLength = 0.1f;
            return 0.54f;
        }

        /// Breacher shotgun: wood furniture, tube magazine under the barrel, pump with grip ridges and shell carrier.
        static float Shotgun(Transform h)
        {
            Color wood = Palette.Hex("5a3a24"), woodDark = Palette.Hex("3b2617");
            P(h, new Vector3(0, 0, -0.02f), new Vector3(0.09f, 0.09f, 0.26f), GunSteel);                  // receiver
            P(h, new Vector3(0.046f, 0.01f, 0.0f), new Vector3(0.004f, 0.04f, 0.09f), GunInk);              // loading port
            Barrel(h, new Vector3(0, 0.025f, 0.28f), 0.04f, 0.36f, GunInk);                                 // barrel
            Barrel(h, new Vector3(0, -0.022f, 0.24f), 0.036f, 0.3f, GunSteel);                              // tube magazine
            P(h, new Vector3(0, 0.05f, 0.45f), new Vector3(0.016f, 0.012f, 0.012f), Palette.Amber, 2f);     // bead sight
            P(h, new Vector3(0, 0.05f, 0.18f), new Vector3(0.012f, 0.006f, 0.48f), GunSteel);               // vent rib
            var pump = Part(h, "Pump", new Vector3(0, -0.02f, 0.22f));
            P(pump, Vector3.zero, new Vector3(0.07f, 0.06f, 0.14f), wood);
            for (int i = 0; i < 6; i++) P(pump, new Vector3(0, -0.031f, -0.055f + i * 0.022f), new Vector3(0.072f, 0.006f, 0.008f), woodDark);
            // Shell carrier with three shells
            P(h, new Vector3(-0.05f, 0.0f, -0.02f), new Vector3(0.012f, 0.05f, 0.1f), GunInk);
            for (int i = 0; i < 3; i++) Barrel(h, new Vector3(-0.058f, 0.0f, -0.05f + i * 0.03f), 0.022f, 0.05f, Palette.Hex("b8342a"));
            var grip = P(h, new Vector3(0, -0.085f, -0.12f), new Vector3(0.055f, 0.1f, 0.06f), wood);
            grip.transform.localRotation = Quaternion.Euler(-22, 0, 0);
            P(h, new Vector3(0, -0.065f, -0.05f), new Vector3(0.012f, 0.008f, 0.07f), GunInk);
            P(h, new Vector3(0, -0.04f, -0.28f), new Vector3(0.065f, 0.09f, 0.22f), wood);                 // stock
            P(h, new Vector3(0, -0.045f, -0.395f), new Vector3(0.068f, 0.1f, 0.014f), GunInk);
            var mag = Part(h, "Mag", new Vector3(-0.058f, 0.0f, 0.03f));                                     // a shell, fed by hand
            Barrel(mag, Vector3.zero, 0.024f, 0.06f, Palette.Hex("b8342a"));
            Barrel(mag, new Vector3(0, 0, -0.03f), 0.026f, 0.012f, Palette.Hex("c9a24a"));
            ammoAt = new Vector3(-0.026f, 0.046f, 0.03f); ammoCell = new Vector3(0.012f, 0.003f, 0); ammoLength = 0.12f;
            heatY = 0.025f; heatWidth = 0.048f; heatLength = 0.12f;
            return 0.47f;
        }

        /// LR-50: long fluted barrel with muzzle brake, scope with glowing lens, bolt handle, bipod and cheek rest.
        static float Sniper(Transform h)
        {
            Color olive = Palette.Hex("3d4433");
            P(h, new Vector3(0, 0, -0.04f), new Vector3(0.08f, 0.08f, 0.34f), olive);                     // chassis
            P(h, new Vector3(0, 0.03f, 0.0f), new Vector3(0.06f, 0.03f, 0.24f), GunSteel);                  // action
            Barrel(h, new Vector3(0, 0.02f, 0.38f), 0.03f, 0.5f, GunInk);
            for (int i = 0; i < 4; i++) P(h, new Vector3(0, 0.036f, 0.2f + i * 0.08f), new Vector3(0.006f, 0.004f, 0.05f), GunSteel); // fluting
            P(h, new Vector3(0, 0.02f, 0.65f), new Vector3(0.05f, 0.036f, 0.07f), GunSteel);                // muzzle brake
            foreach (float x in new[] { -0.026f, 0.026f }) P(h, new Vector3(x, 0.02f, 0.65f), new Vector3(0.004f, 0.02f, 0.05f), GunInk);
            P(h, new Vector3(0, 0, 0.2f), new Vector3(0.07f, 0.06f, 0.16f), olive);                         // forend
            // Scope
            Barrel(h, new Vector3(0, 0.1f, 0.03f), 0.05f, 0.26f, GunInk);
            Barrel(h, new Vector3(0, 0.1f, 0.175f), 0.07f, 0.06f, GunInk);
            Barrel(h, new Vector3(0, 0.1f, -0.11f), 0.062f, 0.05f, GunInk);
            P(h, new Vector3(0, 0.1f, 0.207f), new Vector3(0.05f, 0.05f, 0.004f), Palette.Hex("b47cff"), 2f);
            Barrel(h, new Vector3(0.04f, 0.1f, 0.03f), 0.022f, 0.02f, GunSteel);                            // turret
            P(h, new Vector3(0, 0.124f, 0.03f), new Vector3(0.022f, 0.02f, 0.022f), GunSteel);
            foreach (float z in new[] { -0.04f, 0.1f }) P(h, new Vector3(0, 0.065f, z), new Vector3(0.03f, 0.03f, 0.02f), GunInk); // rings
            // Bolt handle
            var bolt = Part(h, "Bolt", new Vector3(0.026f, 0.03f, -0.08f));                                 // lifted and racked after each shot
            P(bolt, new Vector3(0.024f, 0, 0), new Vector3(0.05f, 0.01f, 0.01f), GunSteel);
            Build.Sphere(bolt, new Vector3(0.052f, 0, 0), 0.024f, GunInk, 0, false);
            // Bipod folded under the forend
            foreach (float x in new[] { -0.02f, 0.02f }) P(h, new Vector3(x, -0.04f, 0.24f), new Vector3(0.008f, 0.008f, 0.14f), GunInk);
            var grip = P(h, new Vector3(0, -0.1f, -0.15f), new Vector3(0.05f, 0.11f, 0.06f), GunPolymer);
            grip.transform.localRotation = Quaternion.Euler(-12, 0, 0);
            P(h, new Vector3(0, -0.07f, -0.08f), new Vector3(0.012f, 0.008f, 0.06f), GunInk);
            P(h, new Vector3(0, -0.02f, -0.34f), new Vector3(0.066f, 0.1f, 0.2f), olive);                  // stock
            P(h, new Vector3(0, 0.04f, -0.32f), new Vector3(0.05f, 0.025f, 0.12f), GunPolymer);            // cheek rest
            P(h, new Vector3(0, -0.02f, -0.445f), new Vector3(0.07f, 0.11f, 0.014f), GunInk);
            var mag = Part(h, "Mag", new Vector3(0, -0.07f, 0.04f));
            P(mag, Vector3.zero, new Vector3(0.048f, 0.07f, 0.09f), GunInk);
            P(mag, new Vector3(0, -0.038f, 0), new Vector3(0.052f, 0.012f, 0.094f), GunSteel);
            ammoAt = new Vector3(-0.0365f, 0.016f, 0.2f); ammoCell = new Vector3(0.003f, 0.01f, 0); ammoLength = 0.13f;    // forend flank
            heatY = 0.02f; heatWidth = 0.04f; heatLength = 0.14f;
            return 0.7f;
        }

        /// Blade colour by rarity: cyan up to 普通, then the rarity colour (稀世 violet, 傳奇 gold, 神話 crimson).
        public static Color BladeColor(int rarity) => rarity >= 2 ? Gear.RarityColors[Mathf.Clamp(rarity, 0, 4)] : rarity == 1 ? Palette.Hex("6fd3ff") : Palette.Hex("8fb3bd");

        /// EX-1 energy sword: ribbed metal hilt with an emitter, guard ring and glowing switch, and a plasma blade
        /// (white-hot core inside a coloured glow). Held angled up and forward; 傳奇+ get gold hilt bands, 神話
        /// a second, crackling outer glow.
        static float Saber(Transform h, int rarity)
        {
            var saber = Part(h, "Saber", new Vector3(0, -0.02f, 0.02f));
            saber.localRotation = Quaternion.Euler(-58, 0, 0);
            Color hilt = Palette.Hex("3a3f47"), grip = Palette.Hex("15171a"), blade = BladeColor(rarity);
            Barrel(saber, new Vector3(0, 0, 0), 0.042f, 0.2f, hilt);
            for (int i = 0; i < 6; i++) Barrel(saber, new Vector3(0, 0, -0.07f + i * 0.026f), 0.046f, 0.012f, grip);
            Barrel(saber, new Vector3(0, 0, -0.112f), 0.05f, 0.024f, GunEdge);                       // pommel
            Barrel(saber, new Vector3(0, 0, 0.112f), 0.056f, 0.024f, GunEdge);                       // emitter
            Barrel(saber, new Vector3(0, 0, 0.128f), 0.064f, 0.008f, rarity >= 3 ? Palette.Hex("d9a73e") : hilt);   // guard ring
            P(saber, new Vector3(0, 0.024f, 0.03f), new Vector3(0.012f, 0.008f, 0.02f), blade, 4f);  // switch
            if (rarity >= 3)
                foreach (float z in new[] { -0.04f, 0.06f }) Barrel(saber, new Vector3(0, 0, z), 0.047f, 0.008f, Palette.Hex("d9a73e"));
            if (rarity == 0)
            {
                // 劣質: rust patches and a scratch on the hilt.
                foreach (var (x, y, z) in new[] { (0.02f, 0.012f, -0.05f), (-0.021f, -0.008f, 0.04f), (0.0f, -0.022f, 0.09f) })
                    P(saber, new Vector3(x, y, z), new Vector3(0.012f, 0.008f, 0.03f), Palette.Hex("6a4a2c"));
                P(saber, new Vector3(-0.0215f, 0.01f, -0.01f), new Vector3(0.002f, 0.004f, 0.07f), Palette.Hex("8a8f96"));
            }
            if (rarity >= 2)
            {
                // 稀世+: glowing lines running down the hilt in the blade colour.
                foreach (float a in new[] { 0f, 120f, 240f })
                {
                    float r = a * Mathf.Deg2Rad;
                    P(saber, new Vector3(Mathf.Cos(r) * 0.0215f, Mathf.Sin(r) * 0.0215f, 0.0f), new Vector3(0.004f, 0.004f, 0.17f), blade, 4f);
                }
            }
            var b = Part(saber, "Blade", new Vector3(0, 0, 0.56f));
            Barrel(b, Vector3.zero, 0.02f, 0.86f, Color.Lerp(blade, Color.white, rarity == 0 ? 0.4f : 0.75f));
            var glow = Build.Cylinder(b, Vector3.zero, 0.05f, 0.9f, blade, 4f);
            glow.transform.localRotation = Quaternion.Euler(90, 0, 0);
            glow.GetComponent<MeshRenderer>().sharedMaterial = Build.Mat(blade, 4f, rarity == 0 ? 0.25f : 0.45f);
            Build.Sphere(b, new Vector3(0, 0, 0.45f), 0.05f, Color.Lerp(blade, Color.white, 0.6f), 4f, false);
            if (rarity == 0) glow.AddComponent<Spinner>().Flicker = 0.62f;   // an unsteady, sputtering blade
            // Light trail from the tip (and a fainter one mid-blade) that streaks behind every swing.
            foreach (var (z, width, alpha) in new[] { (0.43f, rarity >= 3 ? 0.11f : 0.08f, rarity == 0 ? 0.5f : 0.9f), (0.1f, 0.05f, 0.45f) })
            {
                var tip = new GameObject("Trail").transform;
                tip.SetParent(b, false);
                tip.localPosition = new Vector3(0, 0, z);
                var trail = tip.gameObject.AddComponent<TrailRenderer>();
                trail.time = rarity >= 3 ? 0.18f : 0.13f;
                trail.minVertexDistance = 0.01f;
                trail.widthCurve = new AnimationCurve(new Keyframe(0, width), new Keyframe(1, 0));
                trail.sharedMaterial = Build.Mat(blade, 4f, 0.99f);
                var fade = new Gradient();
                fade.SetKeys(new[] { new GradientColorKey(Color.Lerp(blade, Color.white, 0.5f), 0), new GradientColorKey(blade, 1) },
                             new[] { new GradientAlphaKey(alpha, 0), new GradientAlphaKey(0, 1) });
                trail.colorGradient = fade;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.receiveShadows = false;
            }
            // Motes of light drifting up the blade; more and brighter with rarity.
            var motes = b.gameObject.AddComponent<BladeMotes>();
            motes.Build(new[] { 3, 6, 8, 11, 15 }[Mathf.Clamp(rarity, 0, 4)], Color.Lerp(blade, Color.white, 0.45f), rarity == 0 ? 0.6f : 1f);
            if (rarity >= 2)
            {
                // A soft halo around the blade; it breathes on 神話.
                var outer = Build.Cylinder(b, Vector3.zero, rarity >= 4 ? 0.1f : 0.075f, 0.94f, blade, 3f);
                outer.transform.localRotation = Quaternion.Euler(90, 0, 0);
                outer.GetComponent<MeshRenderer>().sharedMaterial = Build.Mat(blade, 3f, rarity >= 4 ? 0.22f : 0.14f);
                if (rarity >= 4) outer.AddComponent<Spinner>().Pulse = 0.25f;
            }
            if (rarity >= 4)
            {
                // 神話: rift shards circling the blade.
                var ring = new GameObject("Shards").transform;
                ring.SetParent(b, false);
                ring.gameObject.AddComponent<Spinner>().Speed = 160f;
                for (int i = 0; i < 5; i++)
                {
                    float a = i * Mathf.PI * 2f / 5f;
                    var shard = P(ring, new Vector3(Mathf.Cos(a) * 0.075f, Mathf.Sin(a) * 0.075f, -0.3f + i * 0.15f), new Vector3(0.016f, 0.016f, 0.045f), Palette.Hex("ff6b6b"), 5f);
                    shard.transform.localRotation = Quaternion.Euler(0, 0, a * Mathf.Rad2Deg + 45f);
                }
            }
            return 0.2f;
        }

        /// P-9 sidearm: slide with serrations and amber sights, frame with rail, textured grip and extended magazine.
        static float Pistol(Transform h)
        {
            var slide = Part(h, "Slide", Vector3.zero);                                                     // moves back on every shot
            P(slide, new Vector3(0, 0.022f, 0.02f), new Vector3(0.062f, 0.05f, 0.25f), GunSteel);          // slide
            for (int i = 0; i < 6; i++) foreach (float x in new[] { -0.032f, 0.032f })
                P(slide, new Vector3(x, 0.026f, -0.08f + i * 0.012f), new Vector3(0.004f, 0.034f, 0.005f), GunInk); // serrations
            P(slide, new Vector3(0.032f, 0.026f, 0.05f), new Vector3(0.004f, 0.016f, 0.05f), GunInk);       // ejection port
            P(h, new Vector3(0, -0.012f, 0.03f), new Vector3(0.058f, 0.03f, 0.22f), GunPolymer);           // frame
            Rail(h, -0.03f, 0.06f, 0.13f);
            Barrel(h, new Vector3(0, 0.022f, 0.15f), 0.022f, 0.02f, GunInk);
            P(slide, new Vector3(0, 0.054f, 0.13f), new Vector3(0.008f, 0.012f, 0.01f), Palette.Amber, 2f); // front sight
            foreach (float x in new[] { -0.014f, 0.014f }) P(slide, new Vector3(x, 0.054f, -0.09f), new Vector3(0.008f, 0.012f, 0.01f), Palette.Amber, 2f);
            var grip = P(h, new Vector3(0, -0.085f, -0.06f), new Vector3(0.054f, 0.13f, 0.07f), GunPolymer);
            grip.transform.localRotation = Quaternion.Euler(-14, 0, 0);
            for (int i = 0; i < 5; i++)
            {
                var ridge = P(h, new Vector3(0, -0.05f - i * 0.022f, -0.055f - i * 0.005f), new Vector3(0.058f, 0.006f, 0.06f), GunInk);
                ridge.transform.localRotation = Quaternion.Euler(-14, 0, 0);
            }
            P(h, new Vector3(0, -0.04f, 0.02f), new Vector3(0.01f, 0.008f, 0.05f), GunInk);                 // trigger guard
            P(h, new Vector3(0, -0.03f, 0.005f), new Vector3(0.006f, 0.022f, 0.006f), GunEdge);             // trigger
            P(h, new Vector3(0.031f, 0.0f, -0.02f), new Vector3(0.004f, 0.01f, 0.03f), Palette.Amber, 0.6f);
            var mag = Part(h, "Mag", new Vector3(0, -0.15f, -0.075f));
            P(mag, Vector3.zero, new Vector3(0.046f, 0.06f, 0.056f), GunInk).transform.localRotation = Quaternion.Euler(-14, 0, 0);
            P(mag, new Vector3(0, -0.032f, -0.008f), new Vector3(0.052f, 0.014f, 0.064f), GunSteel).transform.localRotation = Quaternion.Euler(-14, 0, 0);
            ammoAt = new Vector3(-0.019f, 0.048f, 0.02f); ammoCell = new Vector3(0.009f, 0.003f, 0); ammoLength = 0.12f;   // on top of the slide
            heatY = 0.022f; heatWidth = 0.03f; heatLength = 0.025f;
            return 0.17f;
        }

        /// Small hovering combat drone: hull, two rotor arms and a cyan sensor eye.
        /// Quad-rotor combat drone: canopy hull, four arms with motor pods, independently spinning rotors with a
        /// translucent blur disc, nav lights, landing skids and a cyan sensor eye. Faces +Z.
        public static GameObject Drone()
        {
            var root = new GameObject("Drone").transform;
            Color hull = Palette.Hex("2a2f36"), dark = Palette.Hex("1b1e22"), cyan = Palette.Hex("5ee6ff");
            Build.Cube(root, Vector3.zero, new Vector3(0.22f, 0.09f, 0.34f), hull, 0, false);
            Build.Cube(root, new Vector3(0, 0.06f, 0.04f), new Vector3(0.14f, 0.05f, 0.18f), Palette.Hex("394049"), 0, false);
            Build.Cube(root, new Vector3(0, 0.07f, 0.12f), new Vector3(0.1f, 0.03f, 0.04f), Palette.Hex("1a3a44"), 0.6f, false);
            Build.Cube(root, new Vector3(0, -0.03f, 0.18f), new Vector3(0.08f, 0.05f, 0.03f), cyan, 4f, false);
            int k = 0;
            foreach (var (x, z) in new[] { (1, 1), (-1, 1), (1, -1), (-1, -1) })
            {
                var arm = Build.Cube(root, new Vector3(x * 0.15f, 0.01f, z * 0.15f), new Vector3(0.3f, 0.025f, 0.04f), dark, 0, false);
                arm.transform.localRotation = Quaternion.Euler(0, x * z > 0 ? -45 : 45, 0);
                var pod = new Vector3(x * 0.26f, 0.03f, z * 0.26f);
                Build.Cylinder(root, pod, 0.07f, 0.07f, dark);
                Build.Cube(root, pod + new Vector3(0, -0.04f, 0), new Vector3(0.03f, 0.02f, 0.03f), z > 0 ? (x > 0 ? Palette.Hex("4bff7a") : Palette.Hex("ff3b3b")) : Palette.Hex("ffffff"), 4f, false)
                    .AddComponent<Spinner>().Blink = z > 0 ? 0f : 1.5f;
                var rotor = new GameObject("Rotor").transform;
                rotor.SetParent(root, false);
                rotor.localPosition = pod + new Vector3(0, 0.05f, 0);
                var spin = rotor.gameObject.AddComponent<Spinner>();
                spin.Axis = Vector3.up;
                spin.Speed = (k++ % 2 == 0 ? 1 : -1) * 2400f;
                Build.Cube(rotor, Vector3.zero, new Vector3(0.24f, 0.006f, 0.03f), Palette.Hex("3a4048"), 0, false);
                var blur = Build.Cylinder(rotor, Vector3.zero, 0.26f, 0.002f, Palette.Hex("8a95a3"));
                blur.GetComponent<MeshRenderer>().sharedMaterial = Build.Mat(Palette.Hex("8a95a3"), 0f, 0.18f);
            }
            foreach (float x in new[] { -0.08f, 0.08f }) Build.Cube(root, new Vector3(x, -0.08f, 0), new Vector3(0.015f, 0.015f, 0.26f), dark, 0, false);
            foreach (var c in root.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            return root.gameObject;
        }

        /// Shoulder-mounted plasma cannon added to a soldier when they pick the upgrade.
        public static void PlasmaCannon(Transform soldier)
        {
            var root = new GameObject("Plasma").transform;
            root.SetParent(CharacterRig.Find(soldier, "Torso") ?? soldier, false);
            root.localPosition = new Vector3(-0.36f, 0.74f, 0);
            Color hull = Palette.Hex("2c2a38"), glow = Palette.Hex("9b7bff");
            Build.Cube(root, Vector3.zero, new Vector3(0.18f, 0.16f, 0.5f), hull);
            Build.Cylinder(root, new Vector3(0, 0, 0.3f), 0.12f, 0.12f, Palette.Hex("1d1c26")).transform.localRotation = Quaternion.Euler(90, 0, 0);
            Build.Cube(root, new Vector3(0, 0, 0.37f), new Vector3(0.08f, 0.08f, 0.02f), glow, 4f, false);
            Build.Cube(root, new Vector3(0, 0.09f, -0.05f), new Vector3(0.06f, 0.02f, 0.3f), glow, 2f, false);
        }

        /// Orbit-shield saw: dark hub, three swept teeth with glowing edges and a fading light trail.
        public static GameObject Blade()
        {
            var root = new GameObject("Blade").transform;
            Color amber = Palette.Hex("f0b24a"), hot = Palette.Hex("ffe2a6"), steel = Palette.Hex("3a3f46");
            Build.Cylinder(root, Vector3.zero, 0.34f, 0.05f, steel);
            Build.Cylinder(root, Vector3.zero, 0.14f, 0.07f, Palette.Hex("1b1e22"));
            Build.Cylinder(root, new Vector3(0, 0.03f, 0), 0.06f, 0.02f, amber, 3f);
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f;
                var tooth = new GameObject("Tooth").transform;
                tooth.SetParent(root, false);
                tooth.localRotation = Quaternion.Euler(0, a, 0);
                var plate = Build.Cube(tooth, new Vector3(0.24f, 0, 0.05f), new Vector3(0.26f, 0.025f, 0.12f), steel, 0, false);
                plate.transform.localRotation = Quaternion.Euler(0, -28, 0);
                var edge = Build.Cube(tooth, new Vector3(0.27f, 0, 0.11f), new Vector3(0.24f, 0.03f, 0.025f), hot, 4f, false);
                edge.transform.localRotation = Quaternion.Euler(0, -28, 0);
                if (i == 0)
                {
                    var tip = new GameObject("Trail").transform;
                    tip.SetParent(tooth, false);
                    tip.localPosition = new Vector3(0.38f, 0, 0.06f);
                    var trail = tip.gameObject.AddComponent<TrailRenderer>();
                    trail.time = 0.12f;
                    trail.startWidth = 0.09f; trail.endWidth = 0f;
                    trail.sharedMaterial = Build.Mat(amber, 3f, 0.6f);
                    trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    trail.minVertexDistance = 0.02f;
                }
            }
            foreach (var c in root.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            return root.gameObject;
        }

        /// Elites are larger and wear a glowing gold crest and shoulder trim.
        public static void MakeElite(GameObject enemy)
        {
            var t = enemy.transform;
            t.localScale *= 1.35f;
            Color gold = Palette.Hex("ffcf4a");
            var head = CharacterRig.Find(t, "Head") ?? t;
            Build.Cube(head, new Vector3(0, 0.33f, 0.1f), new Vector3(0.5f, 0.08f, 0.08f), gold, 3f, false);
            foreach (float x in new[] { -0.2f, 0f, 0.2f }) Build.Cube(head, new Vector3(x, 0.43f, 0.1f), new Vector3(0.06f, 0.16f, 0.06f), gold, 3f, false);
            foreach (var side in new[] { "ArmL", "ArmR" })
            {
                var arm = CharacterRig.Find(t, side);
                if (arm != null) Build.Cube(arm, new Vector3(0, 0.1f, 0), new Vector3(0.3f, 0.05f, 0.4f), gold, 2f, false);
            }
        }

        public static GameObject Pickup(PickupKind kind)
        {
            var root = new GameObject("Pickup").transform;
            if (kind == PickupKind.Xp)
            {
                var gem = Build.Cube(root, Vector3.zero, Vector3.one * 0.2f, Palette.Hex("5ee6ff"), 3f, false);
                gem.transform.localRotation = Quaternion.Euler(45, 0, 45);
                root.localScale = Vector3.one * 0.9f;
                return root.gameObject;
            }
            if (kind == PickupKind.SuperChest)
            {
                // Super chest: a larger reinforced crate in boss red and gold, a glowing seam and a tall beacon.
                Color red = Palette.Hex("ff3b5c"), gold = Palette.Hex("ffcf4a");
                Build.Cube(root, Vector3.zero, new Vector3(1.15f, 0.7f, 0.8f), Palette.Hex("3a1622"));
                Build.Cube(root, new Vector3(0, 0.38f, 0), new Vector3(1.2f, 0.14f, 0.85f), red, 2.5f);
                Build.Cube(root, new Vector3(0, 0.28f, 0), new Vector3(1.17f, 0.04f, 0.82f), Palette.Hex("fff1c0"), 5f, false);
                foreach (float x in new[] { -0.45f, 0f, 0.45f }) Build.Cube(root, new Vector3(x, 0, 0), new Vector3(0.07f, 0.72f, 0.82f), gold, 1.2f);
                Build.Cube(root, new Vector3(0, 0.08f, 0.41f), new Vector3(0.2f, 0.24f, 0.04f), gold, 3f, false);
                var star = Build.Cube(root, new Vector3(0, 0.95f, 0), Vector3.one * 0.22f, gold, 5f, false);
                star.transform.localRotation = Quaternion.Euler(45, 0, 45);
                { var spin = star.AddComponent<Spinner>(); spin.Speed = 90f; spin.Axis = Vector3.up; }
                Art.Beacon(root, red, true);
                Art.Beacon(root, gold, false);
                return root.gameObject;
            }
            if (kind == PickupKind.Chest)
            {
                Build.Cube(root, Vector3.zero, new Vector3(0.8f, 0.5f, 0.55f), Palette.Hex("7a4f22"));
                Build.Cube(root, new Vector3(0, 0.27f, 0), new Vector3(0.84f, 0.1f, 0.6f), Palette.Hex("ffcf4a"), 2f);
                Build.Cube(root, new Vector3(0, 0.05f, 0.28f), new Vector3(0.14f, 0.18f, 0.03f), Palette.Hex("ffcf4a"), 3f, false);
                foreach (float x in new[] { -0.3f, 0.3f }) Build.Cube(root, new Vector3(x, 0, 0), new Vector3(0.06f, 0.52f, 0.57f), Palette.Hex("3a2a18"));   // iron bands
                Art.Beacon(root, Palette.Hex("ffcf4a"), true);
                return root.gameObject;
            }
            Color color = kind == PickupKind.Health ? Palette.Hex("5dff8f") : Palette.Hex("ffd166");
            Build.Cube(root, Vector3.zero, Vector3.one * 0.45f, color, 0.9f);
            if (kind == PickupKind.Health)
            {
                Build.Cube(root, Vector3.zero, new Vector3(0.55f, 0.14f, 0.14f), Color.white, 0.6f);
                Build.Cube(root, Vector3.zero, new Vector3(0.14f, 0.55f, 0.14f), Color.white, 0.6f);
            }
            else Build.Cube(root, new Vector3(0, 0.235f, 0), new Vector3(0.3f, 0.03f, 0.3f), Palette.Hex("3a2a10"));   // ammo crate lid
            Art.Beacon(root, color, kind == PickupKind.Health);
            return root.gameObject;
        }

        public static GameObject Projectile(BulletKind kind)
        {
            if (kind == BulletKind.Slam)
            {
                // Boss slam warning: a red disc on the ground that fills in until it bursts.
                var mark = new GameObject("SlamWarning").transform;
                // Faint area plus a bright rim made of short segments, so the circle reads without hiding the ground.
                var area = Build.Cylinder(mark, Vector3.zero, Defs.SlamRadius * 2f, 0.01f, Palette.Hex("ff3b2e"));
                area.GetComponent<MeshRenderer>().sharedMaterial = Build.Mat(Palette.Hex("ff3b2e"), 0.2f, 0.08f);
                for (int i = 0; i < 28; i++)
                {
                    float a = i * Mathf.PI * 2f / 28f;
                    var seg = Build.Cube(mark, new Vector3(Mathf.Cos(a) * Defs.SlamRadius, 0.02f, Mathf.Sin(a) * Defs.SlamRadius), new Vector3(0.5f, 0.02f, 0.07f), Palette.Hex("ff4a3a"), 4f, false);
                    seg.transform.localRotation = Quaternion.Euler(0, -a * Mathf.Rad2Deg + 90f, 0);
                }
                var fill = Build.Cylinder(mark, new Vector3(0, 0.015f, 0), Defs.SlamRadius * 2f, 0.01f, Palette.Hex("ff6a3a"));
                fill.GetComponent<MeshRenderer>().sharedMaterial = Build.Mat(Palette.Hex("ff5a3a"), 0.5f, 0.16f);
                fill.AddComponent<GrowIn>().Duration = Defs.SlamWindup;
                foreach (var c in mark.GetComponentsInChildren<Collider>()) Object.Destroy(c);
                return mark.gameObject;
            }
            if (kind == BulletKind.Mine)
            {
                // Proximity mine: armoured disc, hazard ring, sensor prongs and a blinking arm light.
                var mine = new GameObject("Mine").transform;
                Build.Cylinder(mine, Vector3.zero, 0.5f, 0.1f, Palette.Hex("2b2e33"));
                Build.Cylinder(mine, new Vector3(0, 0.05f, 0), 0.42f, 0.02f, Palette.Hex("e0a32e"));
                Build.Cylinder(mine, new Vector3(0, 0.07f, 0), 0.3f, 0.04f, Palette.Hex("1b1e22"));
                for (int i = 0; i < 3; i++)
                {
                    float a = i * Mathf.PI * 2f / 3f;
                    Build.Cube(mine, new Vector3(Mathf.Cos(a) * 0.12f, 0.12f, Mathf.Sin(a) * 0.12f), new Vector3(0.015f, 0.08f, 0.015f), Palette.Hex("4a5058"), 0, false);
                }
                Build.Cube(mine, new Vector3(0, 0.1f, 0), new Vector3(0.08f, 0.04f, 0.08f), Palette.Hex("ff3b3b"), 5f, false).AddComponent<Spinner>().Blink = 2.5f;
                foreach (var c in mine.GetComponentsInChildren<Collider>()) Object.Destroy(c);
                return mine.gameObject;
            }
            if (kind == BulletKind.Missile)
            {
                // Homing missile facing +Z: body, nose cone, cross fins, glowing exhaust and twin smoke/fire trails.
                var missile = new GameObject("Missile").transform;
                Build.Cylinder(missile, Vector3.zero, 0.11f, 0.42f, Palette.Hex("d8d4ca")).transform.localRotation = Quaternion.Euler(90, 0, 0);
                Build.Cylinder(missile, new Vector3(0, 0, 0.12f), 0.115f, 0.04f, Palette.Hex("c0392b")).transform.localRotation = Quaternion.Euler(90, 0, 0);
                var nose = Build.Sphere(missile, new Vector3(0, 0, 0.22f), 0.11f, Palette.Hex("3a3f46"), 0, false);
                nose.transform.localScale = new Vector3(0.11f, 0.11f, 0.2f);
                for (int i = 0; i < 4; i++)
                {
                    var fin = Build.Cube(missile, new Vector3(0, 0, -0.16f), new Vector3(0.24f, 0.012f, 0.08f), Palette.Hex("3a3f46"), 0, false);
                    fin.transform.localRotation = Quaternion.Euler(0, 0, i * 45f + 45f * (i % 2));
                    if (i >= 2) break;
                }
                var flame = Build.Sphere(missile, new Vector3(0, 0, -0.25f), 0.12f, Palette.Hex("ffb347"), 5f, false);
                flame.transform.localScale = new Vector3(0.1f, 0.1f, 0.22f);
                flame.AddComponent<Spinner>().Pulse = 0.35f;
                var fire = new GameObject("Fire").AddComponent<TrailRenderer>();
                fire.transform.SetParent(missile, false);
                fire.transform.localPosition = new Vector3(0, 0, -0.26f);
                fire.time = 0.12f; fire.startWidth = 0.1f; fire.endWidth = 0f;
                fire.sharedMaterial = Build.Mat(Palette.Hex("ff8a3d"), 4f, 0.8f);
                fire.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var smoke = new GameObject("Smoke").AddComponent<TrailRenderer>();
                smoke.transform.SetParent(missile, false);
                smoke.transform.localPosition = new Vector3(0, 0, -0.3f);
                smoke.time = 0.6f; smoke.startWidth = 0.08f; smoke.endWidth = 0.3f;
                smoke.sharedMaterial = Build.Mat(Palette.Hex("9a9a9a"), 0f, 0.35f);
                smoke.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                foreach (var c in missile.GetComponentsInChildren<Collider>()) Object.Destroy(c);
                return missile.gameObject;
            }
            return Art.Projectile(kind);
        }
    }

    /// Scales a ground marker from a dot to full size over Duration (the slam wind-up).
    public sealed class GrowIn : MonoBehaviour
    {
        public float Duration = 1f;
        float age;
        Vector3 full;
        void Awake() { full = transform.localScale; transform.localScale = new Vector3(0.05f * full.x, full.y, 0.05f * full.z); }
        void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / Mathf.Max(0.01f, Duration));
            transform.localScale = new Vector3(full.x * t, full.y, full.z * t);
        }
    }
}
