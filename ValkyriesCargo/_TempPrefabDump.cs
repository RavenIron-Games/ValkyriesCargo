// TEMPORARY — NOT FOR COMMIT. Headless prefab reader for the P5 design pass.
// Deleted as soon as the numbers are recorded.
using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace RavenIron.ValkyriesCargo.Patches
{
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    public static class _TempPrefabDump
    {
        private static void Postfix()
        {
            try
            {
                L("======== VC PREFAB DUMP BEGIN ========");
                ZoneSystem zs = ZoneSystem.instance;
                L("ZoneSystem: " + (zs != null ? "activeArea=" + zs.m_activeArea + " zoneSize=" + zs.m_zoneSize + " waterLevel=" + zs.m_waterLevel : "null at ZNetScene.Awake"));
                EnvMan em = EnvMan.instance;
                L("EnvMan: " + (em != null ? "dayLengthSec=" + em.m_dayLengthSec : "null at ZNetScene.Awake"));
                foreach (string n in new[] { "Valkyrie", "Dverger", "odin", "Haldor" }) Dump(n);
                L("======== VC PREFAB DUMP END ========");
            }
            catch (Exception ex) { L("DUMP THREW: " + ex); }
        }

        private static void L(string s) { ValkyriesCargo.Log.LogWarning("[VCDUMP] " + s); }

        private static void Dump(string name)
        {
            GameObject p = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
            if (p == null) { L("--- '" + name + "': NOT IN ZNetScene"); return; }
            L("--- prefab '" + p.name + "' : " + p.transform.childCount + " child(ren)");
            L("    components: " + Join(p.GetComponents<Component>()));
            int i = 0;
            foreach (Transform c in p.transform)
            {
                if (i++ >= 12) { L("    ... more children"); break; }
                L("    child '" + c.name + "': " + Join(c.GetComponents<Component>()));
            }

            ZNetView nv = p.GetComponent<ZNetView>();
            if (nv != null) L("    ZNetView: persistent=" + nv.m_persistent + " distant=" + nv.m_distant + " type=" + nv.m_type + " syncInitialScale=" + nv.m_syncInitialScale);

            Valkyrie v = p.GetComponent<Valkyrie>();
            if (v != null)
            {
                L("    Valkyrie: speed=" + v.m_speed + " turnRate=" + v.m_turnRate + " dropHeight=" + v.m_dropHeight);
                L("    Valkyrie: startAltitude=" + v.m_startAltitude + " descentAltitude=" + v.m_descentAltitude +
                  " startDistance=" + v.m_startDistance + " startDescentDistance=" + v.m_startDescentDistance);
                L("    Valkyrie: attachOffset=" + v.m_attachOffset + " textDuration=" + v.m_textDuration);
                L("    Valkyrie: attachPoint=" + (v.m_attachPoint != null
                    ? "'" + v.m_attachPoint.name + "' localPos=" + v.m_attachPoint.localPosition + " path=" + Path(v.m_attachPoint, p.transform)
                    : "NULL ON THE PREFAB"));
            }

            Odin o = p.GetComponent<Odin>();
            if (o != null)
            {
                L("    Odin: m_ttl=" + o.m_ttl + "  <<< the number design 3.6 quotes as 300");
                L("    Odin: despawnCloseDistance=" + o.m_despawnCloseDistance + " despawnFarDistance=" + o.m_despawnFarDistance);
                Effects("Odin.m_despawn", o.m_despawn);
            }

            Character ch = p.GetComponent<Character>();
            if (ch != null)
            {
                L("    Character: name='" + ch.m_name + "' faction=" + ch.m_faction + " health=" + ch.m_health +
                  " boss=" + ch.m_boss + " tolerateWater=" + ch.m_tolerateWater);
                L("    Character: MonsterAI=" + (p.GetComponent<MonsterAI>() != null) +
                  " BaseAI=" + (p.GetComponent<BaseAI>() != null) +
                  " NpcTalk=" + (p.GetComponent<NpcTalk>() != null) +
                  " Tameable=" + (p.GetComponent<Tameable>() != null) +
                  " ZSyncAnimation=" + (p.GetComponent<ZSyncAnimation>() != null) +
                  " Humanoid=" + (p.GetComponent<Humanoid>() != null) +
                  " CapsuleCollider=" + (p.GetComponent<CapsuleCollider>() != null) +
                  " Rigidbody=" + (p.GetComponent<Rigidbody>() != null));
            }

            Humanoid h = p.GetComponent<Humanoid>();
            if (h != null)
            {
                L("    Humanoid: defaultItems=" + Names(h.m_defaultItems) + " randomWeapon=" + Names(h.m_randomWeapon));
                L("    Humanoid: walkSpeed=" + h.m_walkSpeed + " speed=" + h.m_speed + " runSpeed=" + h.m_runSpeed + " turnSpeed=" + h.m_turnSpeed);
            }

            MonsterAI ai = p.GetComponent<MonsterAI>();
            if (ai != null)
            {
                L("    MonsterAI: alertRange=" + ai.m_alertRange + " viewRange=" + ai.m_viewRange + " fleeIfHurtWhenTargetCantBeReached=" + ai.m_fleeIfHurtWhenTargetCantBeReached);
                L("    MonsterAI: avoidFire=" + ai.m_avoidFire + " afraidOfFire=" + ai.m_afraidOfFire + " circulateWhileCharging=" + ai.m_circulateWhileCharging +
                  " randomMoveRange=" + ai.m_randomMoveRange + " randomMoveInterval=" + ai.m_randomMoveInterval);
            }

            NpcTalk nt = p.GetComponent<NpcTalk>();
            if (nt != null)
                L("    NpcTalk: name='" + nt.m_name + "' maxRange=" + nt.m_maxRange + " greetRange=" + nt.m_greetRange +
                  " byeRange=" + nt.m_byeRange + " offset=" + nt.m_offset + " randomTalkInterval=" + nt.m_randomTalkInterval);

            Trader tr = p.GetComponent<Trader>();
            if (tr != null) L("    Trader: name='" + tr.m_name + "' items=" + (tr.m_items != null ? tr.m_items.Count : -1) + " standRange=" + tr.m_standRange);

            Animator an = p.GetComponentInChildren<Animator>(true);
            if (an != null)
            {
                L("    Animator on '" + an.gameObject.name + "': controller=" + (an.runtimeAnimatorController != null ? an.runtimeAnimatorController.name : "NULL") +
                  " avatar=" + (an.avatar != null ? an.avatar.name : "NULL") + " applyRootMotion=" + an.applyRootMotion);
                if (an.runtimeAnimatorController != null)
                {
                    var ps = new List<string>();
                    try { foreach (var prm in an.parameters) ps.Add(prm.type + " " + prm.name); }
                    catch (Exception e) { ps.Add("(parameters unreadable: " + e.GetType().Name + ")"); }
                    L("    Animator parameters (" + ps.Count + "): " + string.Join(", ", ps.ToArray()));
                }
            }
            else L("    Animator: none found in children");

            var smr = p.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            L("    SkinnedMeshRenderers=" + smr.Length + (smr.Length > 0 && smr[0].bones != null ? " bones[0]=" + smr[0].bones.Length : ""));
        }

        private static void Effects(string label, EffectList list)
        {
            if (list == null || list.m_effectPrefabs == null || list.m_effectPrefabs.Length == 0) { L("    " + label + ": empty"); return; }
            for (int i = 0; i < list.m_effectPrefabs.Length; i++)
            {
                var d = list.m_effectPrefabs[i];
                if (d == null || d.m_prefab == null) { L("    " + label + "[" + i + "]: null"); continue; }
                L("    " + label + "[" + i + "]: " + d.m_prefab.name + " enabled=" + d.m_enabled +
                  " networked=" + (d.m_prefab.GetComponent<ZNetView>() != null ? "YES(owner creates)" : "no(every client creates)"));
            }
        }

        private static string Path(Transform t, Transform root)
        {
            var sb = new StringBuilder(t.name);
            for (Transform c = t.parent; c != null && c != root; c = c.parent) sb.Insert(0, c.name + "/");
            return sb.ToString();
        }

        private static string Names(GameObject[] a)
        {
            if (a == null || a.Length == 0) return "(none)";
            var sb = new StringBuilder();
            foreach (var g in a) { if (sb.Length > 0) sb.Append(' '); sb.Append(g != null ? g.name : "null"); }
            return sb.ToString();
        }

        private static string Join(Component[] cs)
        {
            var sb = new StringBuilder();
            foreach (var c in cs) { if (c == null) continue; if (sb.Length > 0) sb.Append(' '); sb.Append(c.GetType().Name); }
            return sb.ToString();
        }
    }
}
