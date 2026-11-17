using RiftWard.Sim;
using UnityEngine;

namespace RiftWard.Client
{
    /// Blocky character, enemy and weapon models. Everything faces +Z.
    public static class Models
    {
        public static GameObject Soldier(Color color, Transform parent = null)
        {
            var root = new GameObject("Soldier").transform;
            root.SetParent(parent, false);
            Color body = Palette.Hex("3a3f46"), dark = Palette.Hex("22262b");
            foreach (float x in new[] { -0.17f, 0.17f })
            {
                Build.Cube(root, new Vector3(x, 0.38f, 0), new Vector3(0.22f, 0.76f, 0.26f), dark);
                Build.Cube(root, new Vector3(x, 0.06f, 0.04f), new Vector3(0.26f, 0.12f, 0.36f), Palette.Hex("1a1c1f"));
            }
            Build.Cube(root, new Vector3(0, 1.08f, 0), new Vector3(0.62f, 0.62f, 0.36f), body);
            Build.Cube(root, new Vector3(0, 1.12f, 0.19f), new Vector3(0.48f, 0.4f, 0.04f), color);
            foreach (float x in new[] { -0.42f, 0.42f })
            {
                Build.Cube(root, new Vector3(x, 1.32f, 0), new Vector3(0.24f, 0.16f, 0.36f), color);
                Build.Cube(root, new Vector3(x, 1.0f, 0.05f), new Vector3(0.18f, 0.52f, 0.2f), body);
            }
            Build.Cube(root, new Vector3(0, 1.62f, 0), new Vector3(0.36f, 0.34f, 0.38f), body);
            Build.Cube(root, new Vector3(0, 1.64f, 0.19f), new Vector3(0.3f, 0.08f, 0.02f), color, 2f);
            Build.Cube(root, new Vector3(0.36f, 1.02f, 0.38f), new Vector3(0.1f, 0.12f, 0.62f), Palette.Hex("15171a"));
            // Helmet brim, visor glass, backpack with antenna, belt pouches and knee pads.
            Build.Cube(root, new Vector3(0, 1.8f, 0.02f), new Vector3(0.42f, 0.06f, 0.46f), dark);
            Build.Cube(root, new Vector3(0, 1.62f, 0.2f), new Vector3(0.32f, 0.12f, 0.02f), Palette.Hex("1a2a33"));
            Build.Cube(root, new Vector3(0, 1.1f, -0.27f), new Vector3(0.46f, 0.5f, 0.2f), dark);
            Build.Cube(root, new Vector3(0, 1.28f, -0.38f), new Vector3(0.3f, 0.06f, 0.02f), color, 1.5f);
            Build.Cube(root, new Vector3(0.16f, 1.6f, -0.3f), new Vector3(0.025f, 0.5f, 0.025f), Palette.Hex("15171a"));
            Build.Cube(root, new Vector3(0, 0.8f, 0), new Vector3(0.64f, 0.08f, 0.38f), Palette.Hex("1a1c1f"));
            foreach (float x in new[] { -0.2f, 0f, 0.2f }) Build.Cube(root, new Vector3(x, 0.82f, 0.2f), new Vector3(0.13f, 0.14f, 0.06f), Palette.Hex("4a4f46"));
            foreach (float x in new[] { -0.17f, 0.17f }) Build.Cube(root, new Vector3(x, 0.42f, 0.14f), new Vector3(0.2f, 0.14f, 0.04f), body);
            return root.gameObject;
        }

        public static GameObject Enemy(int kind)
        {
            if (kind >= Defs.KindHeavyBoss) return Boss(kind);
            var def = Defs.Kinds[kind];
            Color color = def.Color, shell = Palette.Hex("4a2c26"), dark = Palette.Hex("231a19");
            var root = new GameObject("Enemy").transform;
            var bodyCapsule = Build.Prim(PrimitiveType.Capsule, root, new Vector3(0, 1.0f, 0), new Vector3(0.7f, 0.6f, 0.7f), shell);
            Build.Cube(root, new Vector3(0, 1.65f, 0.12f), new Vector3(0.56f, 0.38f, 0.5f), color);
            Build.Cube(root, new Vector3(0, 1.67f, 0.39f), new Vector3(0.42f, 0.09f, 0.03f), Palette.Hex("fff1d6"), 2f);
            Build.Cube(root, new Vector3(-0.48f, 1.15f, 0), new Vector3(0.25f, 0.6f, 0.35f), color);
            Build.Cube(root, new Vector3(0.48f, 1.15f, 0.2f), new Vector3(0.25f, 0.38f, 0.8f), dark);
            foreach (float x in new[] { -0.2f, 0.2f }) Build.Cube(root, new Vector3(x, 0.3f, 0), new Vector3(0.25f, 0.6f, 0.28f), dark);
            // Imperial trooper details: shoulder plate, chest light, side tubes and antenna.
            Build.Cube(root, new Vector3(-0.48f, 1.48f, 0), new Vector3(0.34f, 0.1f, 0.44f), dark);
            Build.Cube(root, new Vector3(0, 1.2f, 0.36f), new Vector3(0.12f, 0.12f, 0.03f), color, 2f);
            foreach (float x in new[] { -0.3f, 0.3f }) Build.Cube(root, new Vector3(x, 1.1f, -0.3f), new Vector3(0.08f, 0.5f, 0.08f), Palette.Hex("2b2321"));
            Build.Cube(root, new Vector3(0.18f, 1.95f, 0), new Vector3(0.03f, 0.3f, 0.03f), dark);
            Build.Cube(root, new Vector3(0.18f, 2.11f, 0), new Vector3(0.06f, 0.06f, 0.06f), color, 2f);
            switch (kind)
            {
                case Defs.KindCharger:
                    foreach (float x in new[] { -0.22f, 0.22f })
                        Build.Cube(root, new Vector3(x, 1.95f, 0.1f), new Vector3(0.08f, 0.28f, 0.08f), color, 1.5f).transform.localRotation = Quaternion.Euler(25, 0, 0);
                    break;
                case Defs.KindSniper:
                    Build.Cube(root, new Vector3(0.48f, 1.15f, 1.05f), new Vector3(0.07f, 0.07f, 0.9f), Palette.Hex("2a2140"));
                    var laser = Build.Cube(root, new Vector3(0.48f, 1.15f, 13.5f), new Vector3(0.025f, 0.025f, 24f), Palette.Hex("ff2244"), 4f, false);
                    laser.name = "Laser";
                    laser.SetActive(false);
                    break;
                case Defs.KindBomber:
                    Build.Sphere(root, new Vector3(0, 1.25f, -0.35f), 0.64f, color, 1.5f);
                    break;
                case Defs.KindShield:
                    var plate = Build.Cube(root, new Vector3(0, 1.05f, 0.62f), new Vector3(1.05f, 1.35f, 0.1f), color, 0.6f);
                    plate.GetComponent<MeshRenderer>().sharedMaterial = Build.Mat(color, 0.6f, 0.55f);
                    break;
            }
            root.localScale = Vector3.one * def.Scale;
            return root.gameObject;
        }

        /// Wave 10 / 20 bosses: oversized armoured soldiers with a hip cannon.
        public static GameObject Boss(int kind)
        {
            bool commander = kind == Defs.KindCommander;
            var root = new GameObject("Boss").transform;
            Color armor = commander ? Palette.Hex("2c2438") : Palette.Hex("3a2a2a");
            Color plate = commander ? Palette.Hex("8a6a3a") : Palette.Hex("6b3a30");
            Color glow = Defs.Kinds[kind].Color, black = Palette.Hex("1c1a1f");
            foreach (float x in new[] { -0.18f, 0.18f })
            {
                Build.Cube(root, new Vector3(x, 0.42f, 0), new Vector3(0.26f, 0.84f, 0.3f), armor);
                Build.Cube(root, new Vector3(x, 0.08f, 0.05f), new Vector3(0.3f, 0.16f, 0.42f), black);
                Build.Cube(root, new Vector3(x, 0.55f, 0.16f), new Vector3(0.2f, 0.18f, 0.04f), plate);
            }
            Build.Cube(root, new Vector3(0, 0.9f, 0), new Vector3(0.62f, 0.2f, 0.34f), black);
            Build.Cube(root, new Vector3(0, 1.25f, 0), new Vector3(0.78f, 0.62f, 0.44f), armor);
            Build.Cube(root, new Vector3(0, 1.28f, 0.23f), new Vector3(0.6f, 0.46f, 0.04f), plate);
            Build.Cube(root, new Vector3(0, 1.3f, 0.255f), new Vector3(0.36f, 0.05f, 0.02f), glow, 2.5f);
            foreach (float x in new[] { -0.56f, 0.56f })
            {
                Build.Cube(root, new Vector3(x, 1.55f, 0), new Vector3(0.36f, 0.22f, 0.5f), plate);
                Build.Cube(root, new Vector3(x, 1.18f, 0), new Vector3(0.22f, 0.6f, 0.24f), armor);
                Build.Cube(root, new Vector3(x, 0.86f, 0.06f), new Vector3(0.24f, 0.16f, 0.28f), black);
            }
            Build.Cube(root, new Vector3(0, 1.6f, 0), new Vector3(0.22f, 0.12f, 0.22f), black);
            Build.Cube(root, new Vector3(0, 1.72f, 0), new Vector3(0.42f, 0.36f, 0.44f), plate);
            Build.Cube(root, new Vector3(0, 1.72f, 0.225f), new Vector3(0.34f, 0.08f, 0.02f), glow, 3f);
            Build.Cube(root, new Vector3(0.56f, 1.0f, 0.42f), new Vector3(0.24f, 0.26f, 0.95f), Palette.Hex("1a1a20"));
            Build.Cube(root, new Vector3(0.56f, 1.0f, 0.95f), new Vector3(0.16f, 0.16f, 0.2f), Palette.Hex("2e2e36"));
            Build.Cube(root, new Vector3(0.56f, 1.0f, 1.06f), new Vector3(0.12f, 0.12f, 0.03f), glow, 3f);
            if (commander)
            {
                Build.Cube(root, new Vector3(0, 2.0f, -0.02f), new Vector3(0.06f, 0.24f, 0.46f), glow, 1.2f);
                Build.Cube(root, new Vector3(0, 1.05f, -0.3f), new Vector3(0.74f, 1.1f, 0.04f), Palette.Hex("5a1f2a")).transform.localRotation = Quaternion.Euler(-8, 0, 0);
                foreach (float x in new[] { -0.56f, 0.56f }) Build.Cube(root, new Vector3(x, 1.68f, 0), new Vector3(0.3f, 0.06f, 0.44f), glow, 1.2f);
            }
            root.localScale = Vector3.one * Defs.Kinds[kind].Scale;
            return root.gameObject;
        }

        /// First-person weapon models, parented to the camera. Returns the muzzle transform.
        public static Transform Viewmodel(string weapon, Transform holder)
        {
            for (int i = holder.childCount - 1; i >= 0; i--) Object.Destroy(holder.GetChild(i).gameObject);
            Color ink = Palette.Hex("1b1d21"), steel = Palette.Hex("2f343b"), grip = Palette.Hex("3a3f46"), accent = Palette.Amber;
            float muzzleZ;
            switch (weapon)
            {
                case "rifle":
                    Build.Cube(holder, Vector3.zero, new Vector3(0.1f, 0.12f, 0.5f), ink, 0, false);
                    Build.Cube(holder, new Vector3(0, 0, 0.36f), new Vector3(0.05f, 0.05f, 0.3f), steel, 0, false);
                    Build.Cube(holder, new Vector3(0, -0.14f, 0.06f), new Vector3(0.07f, 0.18f, 0.1f), steel, 0, false);
                    Build.Cube(holder, new Vector3(0, -0.1f, -0.16f), new Vector3(0.07f, 0.14f, 0.08f), grip, 0, false);
                    Build.Cube(holder, new Vector3(0, 0.07f, 0.1f), new Vector3(0.04f, 0.018f, 0.26f), accent, 0.6f, false);
                    muzzleZ = 0.52f;
                    break;
                case "shotgun":
                    Build.Cube(holder, Vector3.zero, new Vector3(0.13f, 0.13f, 0.46f), Palette.Hex("3b2a22"), 0, false);
                    foreach (float x in new[] { -0.03f, 0.03f }) Build.Cube(holder, new Vector3(x, 0.02f, 0.34f), new Vector3(0.05f, 0.05f, 0.28f), ink, 0, false);
                    Build.Cube(holder, new Vector3(0, -0.07f, 0.2f), new Vector3(0.1f, 0.06f, 0.16f), Palette.Hex("5a4030"), 0, false);
                    Build.Cube(holder, new Vector3(0, -0.1f, -0.15f), new Vector3(0.08f, 0.15f, 0.09f), grip, 0, false);
                    muzzleZ = 0.5f;
                    break;
                case "sniper":
                    Build.Cube(holder, new Vector3(0, 0, -0.02f), new Vector3(0.08f, 0.1f, 0.6f), ink, 0, false);
                    Build.Cube(holder, new Vector3(0, 0.01f, 0.48f), new Vector3(0.035f, 0.035f, 0.4f), steel, 0, false);
                    Build.Cylinder(holder, new Vector3(0, 0.1f, 0.04f), 0.07f, 0.22f, Palette.Hex("1a2430")).transform.localRotation = Quaternion.Euler(90, 0, 0);
                    Build.Cube(holder, new Vector3(0, 0.1f, 0.155f), new Vector3(0.05f, 0.05f, 0.01f), Palette.Hex("b47cff"), 2f, false);
                    Build.Cube(holder, new Vector3(0, -0.1f, -0.18f), new Vector3(0.07f, 0.15f, 0.08f), grip, 0, false);
                    muzzleZ = 0.7f;
                    break;
                default:
                    Build.Cube(holder, new Vector3(0, 0.01f, 0.02f), new Vector3(0.07f, 0.09f, 0.26f), ink, 0, false);
                    Build.Cube(holder, new Vector3(0, -0.1f, -0.06f), new Vector3(0.06f, 0.16f, 0.08f), grip, 0, false);
                    Build.Cube(holder, new Vector3(0, 0.06f, 0.04f), new Vector3(0.035f, 0.012f, 0.18f), accent, 0.8f, false);
                    muzzleZ = 0.16f;
                    break;
            }
            foreach (var c in holder.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            Build.NoShadows(holder.gameObject);
            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(holder, false);
            muzzle.localPosition = new Vector3(0, 0.01f, muzzleZ);
            return muzzle;
        }

        /// Small hovering combat drone: hull, two rotor arms and a cyan sensor eye.
        public static GameObject Drone()
        {
            var root = new GameObject("Drone").transform;
            Color hull = Palette.Hex("2a2f36"), cyan = Palette.Hex("5ee6ff");
            Build.Cube(root, Vector3.zero, new Vector3(0.26f, 0.1f, 0.3f), hull, 0, false);
            Build.Cube(root, new Vector3(0, 0.02f, 0), new Vector3(0.62f, 0.03f, 0.06f), Palette.Hex("1b1e22"), 0, false);
            Build.Cube(root, new Vector3(0, 0.02f, 0), new Vector3(0.06f, 0.03f, 0.62f), Palette.Hex("1b1e22"), 0, false);
            foreach (var p in new[] { new Vector3(0.31f, 0.05f, 0), new Vector3(-0.31f, 0.05f, 0), new Vector3(0, 0.05f, 0.31f), new Vector3(0, 0.05f, -0.31f) })
                Build.Cylinder(root, p, 0.16f, 0.01f, Palette.Hex("3a4048"));
            Build.Cube(root, new Vector3(0, -0.02f, 0.16f), new Vector3(0.1f, 0.05f, 0.02f), cyan, 3f, false);
            foreach (var c in root.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            return root.gameObject;
        }

        /// Shoulder-mounted plasma cannon added to a soldier when they pick the upgrade.
        public static void PlasmaCannon(Transform soldier)
        {
            var root = new GameObject("Plasma").transform;
            root.SetParent(soldier, false);
            root.localPosition = new Vector3(-0.36f, 1.52f, 0);
            Color hull = Palette.Hex("2c2a38"), glow = Palette.Hex("9b7bff");
            Build.Cube(root, Vector3.zero, new Vector3(0.18f, 0.16f, 0.5f), hull);
            Build.Cylinder(root, new Vector3(0, 0, 0.3f), 0.12f, 0.12f, Palette.Hex("1d1c26")).transform.localRotation = Quaternion.Euler(90, 0, 0);
            Build.Cube(root, new Vector3(0, 0, 0.37f), new Vector3(0.08f, 0.08f, 0.02f), glow, 4f, false);
            Build.Cube(root, new Vector3(0, 0.09f, -0.05f), new Vector3(0.06f, 0.02f, 0.3f), glow, 2f, false);
        }

        /// Spinning amber blade for the orbit shield.
        public static GameObject Blade()
        {
            var root = new GameObject("Blade").transform;
            Build.Cube(root, Vector3.zero, new Vector3(0.7f, 0.04f, 0.14f), Palette.Hex("f0b24a"), 2.5f, false);
            Build.Cube(root, Vector3.zero, new Vector3(0.14f, 0.05f, 0.7f), Palette.Hex("ffd98a"), 2.5f, false);
            Build.Cylinder(root, Vector3.zero, 0.18f, 0.08f, Palette.Hex("2a2f36"));
            return root.gameObject;
        }

        /// Elites are larger and wear a glowing gold crest and shoulder trim.
        public static void MakeElite(GameObject enemy)
        {
            var t = enemy.transform;
            t.localScale *= 1.35f;
            Color gold = Palette.Hex("ffcf4a");
            Build.Cube(t, new Vector3(0, 1.98f, 0.1f), new Vector3(0.5f, 0.08f, 0.08f), gold, 3f, false);
            foreach (float x in new[] { -0.2f, 0f, 0.2f }) Build.Cube(t, new Vector3(x, 2.08f, 0.1f), new Vector3(0.06f, 0.16f, 0.06f), gold, 3f, false);
            foreach (float x in new[] { -0.48f, 0.48f }) Build.Cube(t, new Vector3(x, 1.47f, 0), new Vector3(0.3f, 0.05f, 0.4f), gold, 2f, false);
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
                var mine = new GameObject("Mine").transform;
                Build.Cylinder(mine, Vector3.zero, 0.5f, 0.1f, Palette.Hex("2b2e33"));
                Build.Cube(mine, new Vector3(0, 0.07f, 0), new Vector3(0.12f, 0.04f, 0.12f), Palette.Hex("ff3b3b"), 4f, false);
                return mine.gameObject;
            }
            if (kind == BulletKind.Missile)
            {
                var missile = new GameObject("Missile").transform;
                Build.Sphere(missile, Vector3.zero, 0.22f, Palette.Hex("e8e4da"), 0.5f, false);
                Build.Sphere(missile, Vector3.zero, 0.34f, Palette.Hex("ff8a3d"), 3f, false).GetComponent<MeshRenderer>().sharedMaterial = Build.Mat(Palette.Hex("ff8a3d"), 3f, 0.5f);
                return missile.gameObject;
            }
            Color color = kind == BulletKind.Sniper ? Palette.Hex("c78bff") : kind == BulletKind.Grenade ? Palette.Hex("7dff9a") : Palette.Hex("ff5a3c");
            float size = kind == BulletKind.Sniper ? 0.2f : kind == BulletKind.Grenade ? 0.4f : 0.32f;
            return Build.Sphere(null, Vector3.zero, size, color, 1.4f, false);
        }
    }
}
