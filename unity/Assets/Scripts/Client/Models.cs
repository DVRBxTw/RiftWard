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
            Joint(torso, "GunMount", new Vector3(0.1f, 0.36f, 0.3f)).localScale = Vector3.one * 1.35f;
            CharacterRig.Attach(root.gameObject, CharacterRig.Style.Soldier);
            return root.gameObject;
        }

        public static GameObject Enemy(int kind)
        {
            if (kind >= Defs.KindHeavyBoss) return Boss(kind);
            var def = Defs.Kinds[kind];
            Color color = def.Color, shell = Palette.Hex("4a2c26"), dark = Palette.Hex("231a19");
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
            Build.Cube(torso, new Vector3(0, 0.6f, 0.36f), new Vector3(0.12f, 0.12f, 0.03f), color, 2f);
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
                    break;
                case Defs.KindSniper:
                    Build.Cube(armR, new Vector3(0, -0.3f, 1.05f), new Vector3(0.07f, 0.07f, 0.9f), Palette.Hex("2a2140"));
                    var laser = Build.Cube(armR, new Vector3(0, -0.3f, 13.5f), new Vector3(0.025f, 0.025f, 24f), Palette.Hex("ff2244"), 4f, false);
                    laser.name = "Laser";
                    laser.SetActive(false);
                    break;
                case Defs.KindBomber:
                    Build.Sphere(torso, new Vector3(0, 0.65f, -0.35f), 0.64f, color, 1.5f);
                    break;
                case Defs.KindShield:
                    var plate = Build.Cube(elbowL, new Vector3(0.1f, -0.1f, 0.4f), new Vector3(1.05f, 1.35f, 0.1f), color, 0.6f);
                    plate.GetComponent<MeshRenderer>().sharedMaterial = Build.Mat(color, 0.6f, 0.55f);
                    break;
            }
            root.localScale = Vector3.one * def.Scale;
            CharacterRig.Attach(root.gameObject, CharacterRig.Style.Trooper);
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
            }
            var torso = Joint(hips, "Torso", Vector3.zero);
            Build.Cube(torso, new Vector3(0, 0.04f, 0), new Vector3(0.62f, 0.2f, 0.34f), black);
            Build.Cube(torso, new Vector3(0, 0.39f, 0), new Vector3(0.78f, 0.62f, 0.44f), armor);
            Build.Cube(torso, new Vector3(0, 0.42f, 0.23f), new Vector3(0.6f, 0.46f, 0.04f), plate);
            Build.Cube(torso, new Vector3(0, 0.44f, 0.255f), new Vector3(0.36f, 0.05f, 0.02f), glow, 2.5f);
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
        public static Transform Viewmodel(string weapon, Transform holder, int rarity = 1)
        {
            for (int i = holder.childCount - 1; i >= 0; i--) Object.Destroy(holder.GetChild(i).gameObject);
            float muzzleZ;
            switch (weapon)
            {
                case "rifle": muzzleZ = Rifle(holder); break;
                case "shotgun": muzzleZ = Shotgun(holder); break;
                case "sniper": muzzleZ = Sniper(holder); break;
                default: muzzleZ = Pistol(holder); break;
            }
            WeaponFinish(holder, rarity, muzzleZ);
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
            if (rarity >= 3)
            {
                Color gold = Palette.Hex("c9a24a");
                P(h, new Vector3(0, 0.046f, length * 0.2f), new Vector3(0.05f, 0.004f, length * 0.4f), gold, 0.6f);
                foreach (float x in new[] { -0.047f, 0.047f }) P(h, new Vector3(x, -0.03f, 0f), new Vector3(0.004f, 0.02f, 0.08f), gold, 0.6f);
            }
            if (rarity >= 3)
            {
                var ember = P(h, new Vector3(0, -0.02f, length * 0.6f), new Vector3(0.02f, 0.02f, 0.06f), glow, 5f);
                ember.AddComponent<Spinner>().Pulse = 0.35f;
            }
            if (rarity >= 4)
            {
                var halo = new GameObject("Halo").transform;
                halo.SetParent(h, false);
                halo.localPosition = new Vector3(0, 0.01f, length - 0.02f);
                halo.gameObject.AddComponent<Spinner>().Speed = 220f;
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.PI / 3f;
                    var shard = P(halo, new Vector3(Mathf.Cos(a) * 0.075f, Mathf.Sin(a) * 0.075f, 0), new Vector3(0.012f, 0.012f, 0.03f), glow, 5f);
                    shard.transform.localRotation = Quaternion.Euler(0, 0, a * Mathf.Rad2Deg + 45f);
                }
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
            P(h, new Vector3(0, 0.095f, 0.0f), new Vector3(0.054f, 0.05f, 0.008f), GunInk);
            P(h, new Vector3(0, 0.095f, 0.002f), new Vector3(0.036f, 0.032f, 0.004f), Palette.Hex("6fd3ff"), 0.4f);
            P(h, new Vector3(0, 0.095f, 0.004f), new Vector3(0.004f, 0.004f, 0.002f), Palette.Hex("ff3b3b"), 4f);
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
            P(h, new Vector3(0.05f, 0.03f, -0.08f), new Vector3(0.05f, 0.01f, 0.01f), GunSteel);
            Build.Sphere(h, new Vector3(0.078f, 0.03f, -0.08f), 0.024f, GunInk, 0, false);
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
            return 0.7f;
        }

        /// P-9 sidearm: slide with serrations and amber sights, frame with rail, textured grip and extended magazine.
        static float Pistol(Transform h)
        {
            P(h, new Vector3(0, 0.022f, 0.02f), new Vector3(0.062f, 0.05f, 0.25f), GunSteel);              // slide
            for (int i = 0; i < 6; i++) foreach (float x in new[] { -0.032f, 0.032f })
                P(h, new Vector3(x, 0.026f, -0.08f + i * 0.012f), new Vector3(0.004f, 0.034f, 0.005f), GunInk); // serrations
            P(h, new Vector3(0.032f, 0.026f, 0.05f), new Vector3(0.004f, 0.016f, 0.05f), GunInk);           // ejection port
            P(h, new Vector3(0, -0.012f, 0.03f), new Vector3(0.058f, 0.03f, 0.22f), GunPolymer);           // frame
            Rail(h, -0.03f, 0.06f, 0.13f);
            Barrel(h, new Vector3(0, 0.022f, 0.15f), 0.022f, 0.02f, GunInk);
            P(h, new Vector3(0, 0.054f, 0.13f), new Vector3(0.008f, 0.012f, 0.01f), Palette.Amber, 2f);     // front sight
            foreach (float x in new[] { -0.014f, 0.014f }) P(h, new Vector3(x, 0.054f, -0.09f), new Vector3(0.008f, 0.012f, 0.01f), Palette.Amber, 2f);
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
            if (kind == PickupKind.Chest)
            {
                Build.Cube(root, Vector3.zero, new Vector3(0.8f, 0.5f, 0.55f), Palette.Hex("7a4f22"));
                Build.Cube(root, new Vector3(0, 0.27f, 0), new Vector3(0.84f, 0.1f, 0.6f), Palette.Hex("ffcf4a"), 2f);
                Build.Cube(root, new Vector3(0, 0.05f, 0.28f), new Vector3(0.14f, 0.18f, 0.03f), Palette.Hex("ffcf4a"), 3f, false);
                return root.gameObject;
            }
            Color color = kind == PickupKind.Health ? Palette.Hex("5dff8f") : Palette.Hex("ffd166");
            Build.Cube(root, Vector3.zero, Vector3.one * 0.45f, color, 0.9f);
            if (kind == PickupKind.Health)
            {
                Build.Cube(root, Vector3.zero, new Vector3(0.55f, 0.14f, 0.14f), Color.white, 0.6f);
                Build.Cube(root, Vector3.zero, new Vector3(0.14f, 0.55f, 0.14f), Color.white, 0.6f);
            }
            return root.gameObject;
        }

        public static GameObject Projectile(BulletKind kind)
        {
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
            Color color = kind == BulletKind.Sniper ? Palette.Hex("c78bff") : kind == BulletKind.Grenade ? Palette.Hex("7dff9a") : Palette.Hex("ff5a3c");
            float size = kind == BulletKind.Sniper ? 0.2f : kind == BulletKind.Grenade ? 0.4f : 0.32f;
            return Build.Sphere(null, Vector3.zero, size, color, 1.4f, false);
        }
    }
}
