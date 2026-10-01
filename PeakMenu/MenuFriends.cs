using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Peak.Afflictions;
using Photon.Pun;
using UnityEngine;

namespace PeakMenu
{
    // Friends tab (pranks, aura) and Gifts tab (feed / hand items to friends).
    public partial class Menu
    {
        List<Entry> giftFiltered;
        string giftFilteredFor;

        static string Nice(string s) => Regex.Replace(s, "(?<=[a-z])(?=[A-Z])", " ");

        // ---------- item effect tagging ----------
        // Reads which item actions a prefab has, so presets can find e.g. "an item that gives infinite stamina".
        void Tag(Entry e)
        {
            var it = e.item;
            void Add(string t) { if (!e.tags.Contains(t)) e.tags.Add(t); }
            void AddAff(Affliction a)
            {
                if (a == null) return;
                var type = a.GetAfflictionType();
                if (type == Affliction.AfflictionType.ZombieBite) e.fatal = true;
                Add(Nice(type.ToString()));
            }

            foreach (var aa in it.GetComponents<Action_ApplyAffliction>())
            {
                AddAff(aa.affliction);
                if (aa.extraAfflictions != null) foreach (var x in aa.extraAfflictions) AddAff(x);
            }
            if (it.GetComponent<Action_ApplyInfiniteStamina>() != null) Add("Infinite Stamina");
            if (it.GetComponent<Action_GiveExtraStamina>() != null) Add("Extra Stamina");
            if (it.GetComponent<Action_ClearAllStatus>() != null) Add("Cure All");
            if (it.GetComponent<Action_BecomeSkeleton>() != null) Add("Skeleton");
            if (it.GetComponent<Action_WarpRandomly>() != null) Add("Random Warp");
            if (it.GetComponent<Action_WarpToRandomPlayer>() != null) Add("Warp To Player");
            if (it.GetComponent<Action_LaunchPlayer>() != null) Add("Launch");
            if (it.GetComponent<Action_CallScoutmaster>() != null) Add("Scoutmaster");
            if (it.GetComponent<Action_RandomMushroomEffect>() != null) Add("Random Mushroom");
            if (it.GetComponent<Action_InflictPoison>() != null) Add("Inflicts Poison");
            if (it.GetComponent<Action_Die>() != null) e.fatal = true;

            foreach (var ms in it.GetComponents<Action_ModifyStatus>())
            {
                if (ms.changeAmount <= 0f) continue;
                var s = ms.statusType;
                if (s == CharacterAfflictions.STATUSTYPE.Hot || s == CharacterAfflictions.STATUSTYPE.Hunger ||
                    s == CharacterAfflictions.STATUSTYPE.Cold || s == CharacterAfflictions.STATUSTYPE.Poison ||
                    s == CharacterAfflictions.STATUSTYPE.Drowsy)
                    Add("Adds " + s);
            }
        }

        // ---------- helpers ----------
        void Rpc(Character target, string method, string msg, params object[] args)
        {
            try { target.photonView.RPC(method, RpcTarget.All, args); Say(msg); }
            catch (Exception e) { Say(e.Message); }
        }

        void WarpFriend(Character o, Vector3 pos) => o.photonView.RPC("WarpPlayerRPC", RpcTarget.All, pos, true);

        // ---------- FRIENDS tab ----------
        void DrawPlayers(Character c)
        {
            var friends = Friends(c).ToList();

            GUILayout.BeginVertical(sCard);
            Section("INFINITE STAMINA AURA");
            aura = Switch("Share infinite stamina with nearby friends", aura);
            if (aura) auraRadius = Slider("Aura radius", auraRadius, 10f, 150f, "0 m");
            GUILayout.Label("Everyone within range gets infinite stamina. They don't need the mod.", sMuted);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sCard);
            Section("EVERYONE");
            yeet = Slider("Yeet power", yeet, 5f, 80f, "0");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Bring all", sSmallBtn))
            {
                foreach (var o in friends)
                    WarpFriend(o, c.Center + Vector3.up * 1.5f + new Vector3(UnityEngine.Random.Range(-2f, 2f), 0, UnityEngine.Random.Range(-2f, 2f)));
                Say("Everyone to you");
            }
            if (GUILayout.Button("Mass ragdoll", sSmallBtn))
            {
                foreach (var o in friends) o.photonView.RPC("RPCA_Fall", RpcTarget.All, 4f, 0f);
                Say("Everyone fell over");
            }
            if (GUILayout.Button("Mass yeet", sSmallBtn))
            {
                foreach (var o in friends) o.photonView.RPC("RPCA_AddForceAtPosition", RpcTarget.All, Vector3.up * yeet, o.Center, 100f);
                Say("Yeeted everyone");
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Shuffle positions", sSmallBtn))
            {
                var all = new List<Character> { c };
                all.AddRange(friends);
                if (all.Count < 2) Say("Need at least one friend");
                else
                {
                    var spots = all.Select(x => x.Center + Vector3.up * 1.5f).ToList();
                    for (int i = 0; i < all.Count; i++)
                    {
                        var dest = spots[(i + 1) % all.Count];
                        if (all[i] == c) Warp(c, dest); else WarpFriend(all[i], dest);
                    }
                    Say("Everyone swapped places");
                }
            }
            if (GUILayout.Button("Stop all loops", sSmallBtn)) { ragdollLoop.Clear(); pogoLoop.Clear(); Say("Loops stopped"); }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            if (friends.Count == 0)
            {
                GUILayout.BeginVertical(sCard);
                GUILayout.Label("No other players in the lobby", sMuted);
                GUILayout.EndVertical();
                return;
            }

            foreach (var o in friends)
            {
                string name = o.characterName ?? "Player";
                int actor = o.photonView.OwnerActorNr;
                float dist = Vector3.Distance(c.Center, o.Center);

                GUILayout.BeginVertical(sCard);
                GUILayout.BeginHorizontal();
                GUILayout.Label(name, skin.label);
                GUILayout.FlexibleSpace();
                GUILayout.Label(dist.ToString("0") + " m away  ·  " + o.Center.y.ToString("0") + " m high", sMuted);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Go to", sSmallBtn)) { Warp(c, o.Center + Vector3.up * 1.5f); Say("Teleported to " + name); }
                if (GUILayout.Button("Bring", sSmallBtn)) Rpc(o, "WarpPlayerRPC", "Brought " + name, c.Center + Vector3.up * 1.5f, true);
                if (GUILayout.Button("Swap", sSmallBtn))
                {
                    Vector3 mine = c.Center + Vector3.up * 1.5f;
                    Vector3 theirs = o.Center + Vector3.up * 1.5f;
                    WarpFriend(o, mine);
                    Warp(c, theirs);
                    Say("Swapped with " + name);
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Ragdoll", sSmallBtn)) Rpc(o, "RPCA_Fall", "Ragdolled " + name, 4f, 0f);
                if (GUILayout.Button("Yeet up", sSmallBtn))
                    Rpc(o, "RPCA_AddForceAtPosition", "Yeeted " + name, Vector3.up * yeet, o.Center, 100f);
                if (GUILayout.Button("Scoutmaster", sSmallBtn)) SicScoutmaster(o, 30f);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Skeleton on", sSmallBtn))
                    Rpc(o.data, "RPC_SyncSkeleton", "Spooked " + name, true);
                if (GUILayout.Button("Skeleton off", sSmallBtn))
                    Rpc(o.data, "RPC_SyncSkeleton", "Un-spooked " + name, false);
                if (GUILayout.Button("Revive", sSmallBtn))
                    Rpc(o, "RPCA_ReviveAtPosition", "Revived " + name, o.Center, false, -1);
                GUILayout.EndHorizontal();

                bool rl = ragdollLoop.Contains(actor);
                bool rlNew = Switch("Keep ragdolled", rl);
                if (rlNew != rl) { if (rlNew) ragdollLoop.Add(actor); else ragdollLoop.Remove(actor); }

                bool pg = pogoLoop.Contains(actor);
                bool pgNew = Switch("Pogo (auto-yeet)", pg);
                if (pgNew != pg) { if (pgNew) pogoLoop.Add(actor); else pogoLoop.Remove(actor); }
                GUILayout.EndVertical();
            }
        }

        void Rpc(CharacterData d, string method, string msg, params object[] args)
        {
            try { d.photonView.RPC(method, RpcTarget.All, args); Say(msg); }
            catch (Exception e) { Say(e.Message); }
        }

        // ---------- GIFTS tab ----------
        void DrawGifts(Character c)
        {
            BuildItems();
            if (entries == null) { GUILayout.Label("Item database not loaded", sMuted); return; }

            var friends = Friends(c).ToList();
            if (friends.Count == 0)
            {
                GUILayout.BeginVertical(sCard);
                GUILayout.Label("No other players in the lobby", sMuted);
                GUILayout.EndVertical();
                return;
            }
            if (giftTarget != 0 && !friends.Any(f => f.photonView.OwnerActorNr == giftTarget)) giftTarget = 0;
            var targets = giftTarget == 0 ? friends : friends.Where(f => f.photonView.OwnerActorNr == giftTarget).ToList();

            GUILayout.BeginVertical(sCard);
            Section("WHO");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Everyone", giftTarget == 0 ? sSmallBtnOn : sSmallBtn)) giftTarget = 0;
            int n = 1;
            foreach (var f in friends)
            {
                if (GUILayout.Button(f.characterName ?? "Player", giftTarget == f.photonView.OwnerActorNr ? sSmallBtnOn : sSmallBtn))
                    giftTarget = f.photonView.OwnerActorNr;
                if (++n % 3 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
            }
            GUILayout.EndHorizontal();
            Section("HOW");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Use it on them", giftMode == 0 ? sSmallBtnOn : sSmallBtn)) giftMode = 0;
            if (GUILayout.Button("Put it in their hands", giftMode == 1 ? sSmallBtnOn : sSmallBtn)) giftMode = 1;
            GUILayout.EndHorizontal();
            GUILayout.Label(giftMode == 0
                ? "Runs the item's effect on them instantly, like feeding them."
                : "Hands them the item itself to hold.", sMuted);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sCard);
            Section("BUFFS");
            PresetGrid(BuffTags, targets);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sCard);
            Section("TROLLS");
            PresetGrid(TrollTags, targets);
            GUILayout.Label("Items that can kill are blocked from 'Use it on them'.", sMuted);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sCard);
            Section("ANY ITEM");
            giftFilter = GUILayout.TextField(giftFilter);
            string key = giftFilter + "|" + giftMode;
            if (giftFiltered == null || giftFilteredFor != key)
            {
                string f = giftFilter.Trim().ToLowerInvariant();
                giftFiltered = entries
                    .Where(e => giftMode == 1 || e.tags.Count > 0)
                    .Where(e => f.Length == 0 || e.search.Contains(f) || e.tags.Any(t => t.ToLowerInvariant().Contains(f)))
                    .ToList();
                giftFilteredFor = key;
            }
            GUILayout.Label(giftFiltered.Count + " items", sMuted);
            foreach (var e in giftFiltered)
            {
                string label = e.tags.Count > 0 ? e.display + "   ·   " + string.Join(", ", e.tags) : e.display;
                if (GUILayout.Button(label, sRow)) Give(e, targets);
            }
            GUILayout.EndVertical();
        }

        void PresetGrid(string[] tags, List<Character> targets)
        {
            var available = tags.Where(t => byTag.TryGetValue(t, out var l) && l.Any(x => !x.fatal)).ToList();
            if (available.Count == 0) { GUILayout.Label("No matching items found", sMuted); return; }
            for (int i = 0; i < available.Count; i += 3)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < i + 3; j++)
                {
                    if (j >= available.Count) { GUILayout.FlexibleSpace(); break; }
                    if (GUILayout.Button(available[j], sSmallBtn))
                    {
                        var best = byTag[available[j]].Where(x => !x.fatal).OrderBy(x => x.tags.Count).First();
                        Give(best, targets);
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        void Give(Entry e, List<Character> targets)
        {
            if (giftMode == 0 && e.fatal) { Say("Blocked: that item can kill"); return; }
            foreach (var t in targets) StartCoroutine(GiveRoutine(e, t, giftMode));
            Say((giftMode == 0 ? "Used " : "Gave ") + e.display + " on " + (targets.Count == 1 ? (targets[0].characterName ?? "friend") : targets.Count + " friends"));
        }

        IEnumerator GiveRoutine(Entry e, Character t, int mode)
        {
            if (t == null) yield break;
            GameObject go = null;
            string err = null;
            try { go = PhotonNetwork.Instantiate("0_Items/" + e.name, t.Center + Vector3.up * 1.5f, Quaternion.identity, 0); }
            catch (Exception ex) { err = ex.Message; }
            if (go == null) { if (err != null) Say(err); yield break; }

            yield return new WaitForSecondsRealtime(0.6f);   // let every client spawn it first
            if (go == null || t == null) yield break;

            try
            {
                var it = go.GetComponent<Item>();
                if (mode == 0) t.photonView.RPC("GetFedItemRPC", t.photonView.Owner, it.photonView.ViewID);
                else it.RequestPickup(t.photonView);
            }
            catch (Exception ex) { Say(ex.Message); }

            if (mode == 0)
            {
                yield return new WaitForSecondsRealtime(6f);
                if (go != null)
                {
                    var pv = go.GetComponent<PhotonView>();
                    if (pv != null && pv.IsMine) PhotonNetwork.Destroy(go);
                }
            }
        }
    }
}
