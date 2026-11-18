using UnityEngine;

namespace RiftWard.Client
{
    /// Procedural animation for jointed characters (soldiers, troopers, bosses). It reads its own movement each
    /// frame, so it animates correctly no matter who moves the transform; the client only feeds aim, reload,
    /// firing and hits. Joint names are fixed by Models: Hips, Torso, Head, LegL/LegR, KneeL/KneeR, ArmL/ArmR,
    /// ElbowL/ElbowR and an optional GunMount.
    public sealed class CharacterRig : MonoBehaviour
    {
        public enum Style { Soldier, Trooper, Boss }

        public Style Kind;
        public bool Alive = true, Airborne;
        public float Pitch, Reload;           // aim pitch (radians, +up) and reload progress 0..1 (0 = not reloading)
        public Transform Muzzle;               // third-person muzzle when a weapon is mounted

        Transform hips, torso, head, legL, legR, kneeL, kneeR, armL, armR, elbowL, elbowR, gunMount;
        Vector3 hipsBase, lastPos;
        float phase, speed, recoil, flinch, idle, stride = 1f;
        string weaponShown;
        int rarityShown = -1;
        bool started;

        public static CharacterRig Attach(GameObject root, Style kind)
        {
            var rig = root.AddComponent<CharacterRig>();
            rig.Kind = kind;
            rig.hips = Find(root.transform, "Hips");
            rig.torso = Find(root.transform, "Torso");
            rig.head = Find(root.transform, "Head");
            rig.legL = Find(root.transform, "LegL"); rig.legR = Find(root.transform, "LegR");
            rig.kneeL = Find(root.transform, "KneeL"); rig.kneeR = Find(root.transform, "KneeR");
            rig.armL = Find(root.transform, "ArmL"); rig.armR = Find(root.transform, "ArmR");
            rig.elbowL = Find(root.transform, "ElbowL"); rig.elbowR = Find(root.transform, "ElbowR");
            rig.gunMount = Find(root.transform, "GunMount");
            rig.hipsBase = rig.hips != null ? rig.hips.localPosition : Vector3.zero;
            rig.stride = kind == Style.Boss ? 0.55f : kind == Style.Trooper ? 0.85f : 1f;
            rig.idle = Random.value * 10f;
            return rig;
        }

        public static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            // Indexed loop: foreach over a Transform allocates an enumerator per node.
            for (int i = 0; i < t.childCount; i++) { var f = Find(t.GetChild(i), name); if (f != null) return f; }
            return null;
        }

        /// Puts the given weapon (with its rarity finish) in the soldier's hands; rebuilt only when it changes.
        public void SetWeapon(string weapon, int rarity)
        {
            if (gunMount == null || (weapon == weaponShown && rarity == rarityShown)) return;
            weaponShown = weapon; rarityShown = rarity;
            Muzzle = Models.Viewmodel(weapon, gunMount, rarity);
            foreach (var r in gunMount.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        public void Fire() => recoil = 1f;

        /// A blade swing: a fast horizontal sweep of the sword arm, alternating sides, with a torso twist.
        public void Swing() { swingTime = 0f; swingSide = -swingSide; }
        float swingTime = 9f, swingSide = 1f;
        const float SwingDuration = 0.3f;
        public void Hit() => flinch = 1f;

        static Quaternion E(float x, float y = 0, float z = 0) => Quaternion.Euler(x, y, z);

        void LateUpdate()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 pos = transform.position;
            if (!started) { lastPos = pos; started = true; }
            float scale = Mathf.Max(0.1f, transform.lossyScale.y);
            var delta = pos - lastPos; delta.y = 0;
            lastPos = pos;
            float measured = Mathf.Min(delta.magnitude / dt / scale, 14f);
            speed = Mathf.Lerp(speed, measured, 1f - Mathf.Exp(-10f * dt));
            float move = Mathf.Clamp01(speed / 4f);
            phase += speed * dt * 2.3f * stride;
            idle += dt;
            recoil = Mathf.MoveTowards(recoil, 0, dt * 9f);
            swingTime += dt;
            // 0 → 1 over the swing: wind-up for the first 15%, a fast eased sweep, then a short recovery back to rest.
            float st = swingTime / SwingDuration;
            float sweep = st >= 1f ? 0f : st < 0.15f ? -st / 0.15f : Mathf.Clamp01((st - 0.15f) / 0.55f);
            float swingAmount = st >= 1f ? 0f : st < 0.7f ? 1f : 1f - (st - 0.7f) / 0.3f;   // pose weight
            float arc = st < 0.15f ? Mathf.Lerp(0f, 75f, -sweep) : Mathf.Lerp(75f, -80f, 1f - Mathf.Pow(1f - sweep, 3f));   // degrees of yaw
            arc *= swingSide;
            bool swinging = swingAmount > 0f && weaponShown == "saber";
            flinch = Mathf.MoveTowards(flinch, 0, dt * 5f);
            if (!Alive) { move = 0; }

            float s = Mathf.Sin(phase), c = Mathf.Cos(phase);
            float swing = (Kind == Style.Boss ? 26f : 34f) * move;
            float tuck = Airborne ? 1f : 0f;

            // Legs: thigh swing, knee bends on the back-swing; tucked when airborne.
            if (legL != null) legL.localRotation = Quaternion.Slerp(legL.localRotation, E(s * swing - tuck * 35f), 1f - Mathf.Exp(-18f * dt));
            if (legR != null) legR.localRotation = Quaternion.Slerp(legR.localRotation, E(-s * swing - tuck * 20f), 1f - Mathf.Exp(-18f * dt));
            if (kneeL != null) kneeL.localRotation = Quaternion.Slerp(kneeL.localRotation, E(Mathf.Max(0, -c) * 55f * move + tuck * 60f), 1f - Mathf.Exp(-18f * dt));
            if (kneeR != null) kneeR.localRotation = Quaternion.Slerp(kneeR.localRotation, E(Mathf.Max(0, c) * 55f * move + tuck * 45f), 1f - Mathf.Exp(-18f * dt));

            // Hips bob twice per stride (heavier for bosses) plus idle breathing.
            if (hips != null)
            {
                float bob = Mathf.Abs(c) * (Kind == Style.Boss ? 0.07f : 0.045f) * move;
                float breathe = Mathf.Sin(idle * 2.1f) * 0.006f * (1f - move);
                hips.localPosition = hipsBase + new Vector3(0, -bob + breathe, 0);
            }

            // Torso: lean into the run, counter-twist the stride, flinch back when hit, recoil kick.
            if (torso != null)
            {
                float lean = 7f * move + (Kind == Style.Boss ? Mathf.Abs(s) * 4f * move : 0f);
                float twist = s * 6f * move;
                torso.localRotation = E(lean - flinch * 16f - recoil * 3f, twist + (swinging ? arc * 0.35f * swingAmount : 0f), Kind == Style.Boss ? s * 3f * move : 0f);
            }
            if (head != null) head.localRotation = E(-Mathf.Rad2Deg * Pitch * 0.5f - 7f * move * 0.5f + flinch * 8f, -s * 3f * move);

            float aim = -Mathf.Rad2Deg * Pitch;
            if (gunMount != null)
            {
                // Two-handed carry: both arms forward on the weapon, which follows the aim pitch.
                float reloadDip = Reload > 0 ? Mathf.Sin(Mathf.PI * Reload) : 0f;
                gunMount.localRotation = E(aim * 0.85f + recoil * -6f, 0, reloadDip * 28f);
                gunMount.localPosition = new Vector3(0.1f, 0.36f - reloadDip * 0.06f, 0.3f - recoil * 0.06f);
                if (armR != null) armR.localRotation = E(-62f + aim * 0.7f - recoil * 6f, -8f, 4f);
                if (elbowR != null) elbowR.localRotation = E(-48f, 0, 0);
                // Left hand on the foregrip; during a reload it drops to the belt for a fresh magazine.
                float toBelt = Reload > 0 ? Mathf.Clamp01(Mathf.Sin(Mathf.PI * Mathf.Clamp01((Reload - 0.1f) / 0.7f))) : 0f;
                if (armL != null) armL.localRotation = Quaternion.Slerp(E(-80f + aim * 0.8f, 32f, -10f), E(-10f, 10f, -8f), toBelt);
                if (elbowL != null) elbowL.localRotation = Quaternion.Slerp(E(-30f, 0, 0), E(-70f, 0, 0), toBelt);
                if (swinging)
                {
                    // Sword swing: the blade sweeps across the body at chest height, tipped toward the swing.
                    var swingRot = E(-25f + aim * 0.4f, arc, -swingSide * 55f);
                    gunMount.localRotation = Quaternion.Slerp(gunMount.localRotation, swingRot, swingAmount);
                    var swingPos = new Vector3(Mathf.Sin(arc * Mathf.Deg2Rad) * 0.32f, 0.42f, 0.22f + Mathf.Cos(arc * Mathf.Deg2Rad) * 0.18f);
                    gunMount.localPosition = Vector3.Lerp(gunMount.localPosition, swingPos, swingAmount);
                    if (armR != null) armR.localRotation = Quaternion.Slerp(armR.localRotation, E(-80f, arc * 0.9f, 4f), swingAmount);
                    if (elbowR != null) elbowR.localRotation = Quaternion.Slerp(elbowR.localRotation, E(-20f, 0, 0), swingAmount);
                    if (armL != null) armL.localRotation = Quaternion.Slerp(armL.localRotation, E(-30f, -arc * 0.3f, -18f), swingAmount);
                }
            }
            else
            {
                // Troopers / bosses: the off arm swings with the stride, the weapon arm stays levelled forward.
                float armSwing = swing * 0.8f;
                if (armL != null) armL.localRotation = E(-s * armSwing - flinch * 20f, 0, -4f);
                if (elbowL != null) elbowL.localRotation = E(-15f - Mathf.Max(0, s) * 20f * move);
                if (armR != null) armR.localRotation = E(Kind == Style.Boss ? s * armSwing * 0.4f - recoil * 10f : -recoil * 14f - flinch * 10f + Mathf.Sin(idle * 1.7f) * 2f);
                if (elbowR != null) elbowR.localRotation = E(-10f);
            }
        }
    }
}
