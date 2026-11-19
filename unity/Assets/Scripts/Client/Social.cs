using System;
using System.Collections.Generic;
using RiftWard.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace RiftWard.Client
{
    /// Chat messages received this session (world, room and server notices), newest last.
    public static class ChatLog
    {
        public struct Line
        {
            public byte Channel;
            public int AccountId;
            public string Name, Title, Text;
            public bool Admin;
            public float Time;
        }

        public const int Capacity = 120;
        public static readonly List<Line> Lines = new List<Line>();
        public static event Action<Line> Added;

        public static void Add(Line line)
        {
            line.Time = UnityEngine.Time.realtimeSinceStartup;
            if (line.Channel == ChatChannel.Whisper && !string.IsNullOrEmpty(line.Name)) LastWhisperFrom = line.Name;
            Lines.Add(line);
            if (Lines.Count > Capacity) Lines.RemoveAt(0);
            Added?.Invoke(line);
        }

        public static void Notice(string text) => Add(new Line { Channel = ChatChannel.System, Text = text });

        /// Who last sent you a private message (for /r).
        public static string LastWhisperFrom = "";

        public static Line Read(System.IO.BinaryReader r) => new Line
        {
            Channel = r.ReadByte(), AccountId = r.ReadInt32(), Name = r.ReadString(), Title = r.ReadString(), Text = r.ReadString(), Admin = r.ReadBoolean(),
        };

        /// Player text shown in a rich-text label: angle brackets are swapped so nobody can inject markup.
        public static string Safe(string text) => (text ?? "").Replace("<", "‹").Replace(">", "›");

        /// One message as rich text: channel tag, title and name, then the message.
        public static string Format(Line l)
        {
            if (l.Channel == ChatChannel.System) return $"<color=#f0b24a>[系統]</color> <color=#d6d1c6>{Safe(l.Text)}</color>";
            if (l.Channel == ChatChannel.Whisper || l.Channel == ChatChannel.WhisperSent)
            {
                string dir = l.Channel == ChatChannel.Whisper ? "來自" : "給";
                return $"<color=#ff8fd0>[私訊] {dir} <b>{Safe(l.Name)}</b>：</color><color=#f3d6e8>{Safe(l.Text)}</color>";
            }
            string tag = l.Channel == ChatChannel.Room ? "<color=#82aaff>[房間]</color>" : "<color=#8c8f93>[全頻]</color>";
            string title = string.IsNullOrEmpty(l.Title) ? "" : $"<color=#f0b24a>〔{Safe(l.Title)}〕</color>";
            string name = l.Admin ? $"<color=#e5534b>{Safe(l.Name)}</color>" : $"<color=#ebe6db>{Safe(l.Name)}</color>";
            return $"{tag} {title}<b>{name}</b><color=#8c8f93>：</color><color=#d6d1c6>{Safe(l.Text)}</color>";
        }
    }

    /// Friends' presence as last reported by the game server.
    public static class FriendPresence
    {
        public static readonly Dictionary<int, (byte state, string room)> States = new Dictionary<int, (byte, string)>();
        public static event Action Updated;
        /// A friend went from offline to online (not raised for the first report after connecting). Gives the name.
        public static event Action<string> CameOnline;
        /// Unix seconds each friend was last seen, from the account service's friend list (0 = unknown).
        public static readonly Dictionary<int, long> LastSeen = new Dictionary<int, long>();
        static bool primed;

        public static void Read(System.IO.BinaryReader r)
        {
            var before = new Dictionary<int, (byte state, string room)>(States);
            States.Clear();
            int n = r.ReadInt32();
            var names = new Dictionary<int, string>();
            for (int i = 0; i < n; i++)
            {
                int id = r.ReadInt32(); byte state = r.ReadByte(); States[id] = (state, r.ReadString()); names[id] = r.ReadString();
            }
            if (primed)
                foreach (var kv in States)
                {
                    bool wasOn = before.TryGetValue(kv.Key, out var b) && b.state != PresenceState.Offline;
                    if (kv.Value.state != PresenceState.Offline && !wasOn && !string.IsNullOrEmpty(names[kv.Key])) CameOnline?.Invoke(names[kv.Key]);
                    // Just went offline: they were seen now.
                    if (kv.Value.state == PresenceState.Offline && wasOn) LastSeen[kv.Key] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                }
            primed = true;
            Updated?.Invoke();
        }

        public static void Clear() { States.Clear(); primed = false; Updated?.Invoke(); }

        /// "3 分鐘前", "2 小時前", "5 天前".
        public static string Ago(long unix)
        {
            if (unix <= 0) return "";
            long s = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - unix);
            if (s < 60) return "剛剛";
            if (s < 3600) return $"{s / 60} 分鐘前";
            if (s < 86400) return $"{s / 3600} 小時前";
            if (s < 86400 * 30) return $"{s / 86400} 天前";
            return "很久以前";
        }

        public static byte StateOf(int id) => States.TryGetValue(id, out var s) ? s.state : PresenceState.Offline;

        public static string Describe(int id)
        {
            string offline = LastSeen.TryGetValue(id, out long seen) && seen > 0 ? $"離線 · {Ago(seen)}" : "離線";
            if (!States.TryGetValue(id, out var s)) return offline;
            switch (s.state)
            {
                case PresenceState.Lobby: return "在基地";
                case PresenceState.InRoom: return string.IsNullOrEmpty(s.room) ? "在房間中" : $"在房間 {s.room}";
                case PresenceState.InMission: return "任務中";
                default: return offline;
            }
        }

        public static Color ColorOf(int id)
        {
            switch (StateOf(id))
            {
                case PresenceState.Lobby: return Palette.Hex("5dd39e");
                case PresenceState.InRoom: return Palette.Teammate;
                case PresenceState.InMission: return Palette.Amber;
                default: return Palette.Hex("5d6064");
            }
        }
    }

    /// Overlays shown above every screen: a short toast and a friend's room invitation.
    public sealed class SocialOverlay
    {
        readonly Canvas canvas;
        RectTransform toast, invite;
        float toastUntil;
        public Action<string> Join;
        public Action<int> Decline;
        public Action<string, bool> RespondFriend;
        public Action<int, bool> AnswerJoin;

        public SocialOverlay() { canvas = Ui.Canvas("Social", 30); }
        /// Parent for modal sheets that sit above every screen.
        public Transform Root => canvas.transform;

        public void Toast(string text, float seconds = 2.6f)
        {
            if (toast != null) UnityEngine.Object.Destroy(toast.gameObject);
            toast = Ui.Box(canvas.transform, new Color(0.07f, 0.072f, 0.078f, 0.96f), "Toast").rectTransform;
            var label = Ui.Label(toast, text, 15, Palette.Bone, TextAnchor.MiddleCenter);
            label.rectTransform.Fill();
            float width = Mathf.Clamp(label.preferredWidth + 56, 260, 720);
            toast.Place(new Vector2(0.5f, 1), new Vector2(0, -88), new Vector2(width, 40), new Vector2(0.5f, 1));
            var line = toast.gameObject.AddComponent<Outline>();
            line.effectColor = Palette.Amber; line.effectDistance = new Vector2(1, -1);
            toastUntil = Time.realtimeSinceStartup + seconds;
        }

        /// One invitation card waiting its turn; cards show one at a time, in arrival order.
        sealed class Card { public string Kind, Key; public Action<RectTransform> Build; }
        readonly List<Card> queue = new List<Card>();
        Card current;
        /// While true (in a mission) queued cards wait instead of popping up.
        public Func<bool> Hold = () => false;

        public void ShowInvite(int fromId, string fromName, string code)
        {
            Enqueue("room", "room:" + fromId, Palette.Teammate, "組隊邀請", $"{fromName} 邀請你加入房間 {code}",
                ("婉拒", false, () => Decline?.Invoke(fromId)), ("加入房間", true, () => Join?.Invoke(code)));
        }

        public void ShowFriendRequest(string fromName)
        {
            Enqueue("friend", "friend:" + fromName.ToLowerInvariant(), Palette.Amber, "好友邀請", $"{fromName} 想加你為好友",
                ("拒絕", false, () => RespondFriend?.Invoke(fromName, false)), ("接受", true, () => RespondFriend?.Invoke(fromName, true)));
        }

        public void ShowJoinRequest(int fromId, string fromName)
        {
            Enqueue("join", "join:" + fromId, Palette.Teammate, "入隊申請", $"{fromName} 申請加入你的房間",
                ("拒絕", false, () => AnswerJoin?.Invoke(fromId, false)), ("同意", true, () => AnswerJoin?.Invoke(fromId, true)));
        }

        void Enqueue(string kind, string key, Color accent, string title, string message, params (string label, bool primary, Action act)[] buttons)
        {
            // The same person inviting again replaces their waiting card instead of stacking a duplicate.
            queue.RemoveAll(c => c.Key == key);
            var card = new Card { Kind = kind, Key = key };
            card.Build = box =>
            {
                box.Place(new Vector2(0.5f, 1), new Vector2(0, -140), new Vector2(520, 124), new Vector2(0.5f, 1));
                var outline = box.gameObject.AddComponent<Outline>();
                outline.effectColor = accent; outline.effectDistance = new Vector2(1, -1);
                Ui.Box(box, accent, "Mark").rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(4, 124), new Vector2(0, 0.5f));
                Ui.Label(box, title, 13, accent, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 1), new Vector2(24, -14), new Vector2(300, 18), new Vector2(0, 1));
                Ui.Label(box, message, 19, Palette.Bone, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 1), new Vector2(24, -36), new Vector2(470, 26), new Vector2(0, 1));
                var row = Ui.HStack(box, 10);
                row.Place(new Vector2(1, 0), new Vector2(-18, 14), new Vector2(250, 38), new Vector2(1, 0));
                foreach (var (label, primary, act) in buttons)
                {
                    if (primary) Ui.Button(row, label, () => { Dismiss(); act(); }, 16, Palette.Ink, accent, accent);
                    else Ui.Button(row, label, () => { Dismiss(); act(); }, 15, Palette.Muted);
                }
                box.gameObject.AddComponent<Expire>().Seconds = 30f;
            };
            if (current != null && current.Key == key && invite != null) { Dismiss(); queue.Insert(0, card); }
            else queue.Add(card);
            ShowNext();
        }

        void ShowNext()
        {
            if (invite != null || queue.Count == 0 || Hold()) return;
            current = queue[0];
            queue.RemoveAt(0);
            invite = Ui.Box(canvas.transform, new Color(0.07f, 0.072f, 0.078f, 0.97f), "Invite").rectTransform;
            current.Build(invite);
        }

        void Dismiss()
        {
            if (invite != null) UnityEngine.Object.Destroy(invite.gameObject);
            invite = null; current = null;
        }

        /// Clears room invitations (on deploy or disconnect); friend requests keep waiting their turn.
        public void HideInvite()
        {
            queue.RemoveAll(c => c.Kind == "room");
            if (current != null && current.Kind == "room") Dismiss();
        }

        public void Update()
        {
            if (toast != null && Time.realtimeSinceStartup > toastUntil) { UnityEngine.Object.Destroy(toast.gameObject); toast = null; }
            if (invite == null && current != null) current = null;   // the card expired
            if (invite != null && Hold()) { queue.Insert(0, current); Dismiss(); }   // a mission started: put it back
            ShowNext();
        }

        sealed class Expire : MonoBehaviour
        {
            public float Seconds;
            void Update() { Seconds -= Time.unscaledDeltaTime; if (Seconds <= 0) Destroy(gameObject); }
        }
    }

    /// In-mission chat: recent lines fade in the lower left; the chat key opens a one-line input.
    public sealed class ChatHud
    {
        readonly Canvas canvas;
        readonly RectTransform logRoot;
        readonly Text log;
        readonly RectTransform backing;
        InputField input;
        RectTransform inputRow;
        byte channel = ChatChannel.Room;
        Text channelTag;
        public Action<byte, string> Send;
        /// Set while the input is open; the game ignores movement and shooting keys meanwhile.
        public static bool Typing { get; private set; }
        public static int ClosedFrame = -1;

        public ChatHud()
        {
            canvas = Ui.Canvas("ChatHud", 15);
            logRoot = Ui.Rect(canvas.transform, "ChatLog").Place(new Vector2(0, 0), new Vector2(28, 220), new Vector2(520, 200), new Vector2(0, 0));
            backing = Ui.Box(logRoot, new Color(0.03f, 0.031f, 0.034f, 0.3f), "Backing").rectTransform;
            backing.anchorMin = Vector2.zero; backing.anchorMax = new Vector2(1, 0); backing.pivot = new Vector2(0.5f, 0);
            // Hidden until there is a line to show: Update only repaints when the text changes, and it starts empty.
            backing.sizeDelta = Vector2.zero;
            backing.gameObject.SetActive(false);
            log = Ui.Label(logRoot, "", 14, Palette.Bone, TextAnchor.LowerLeft);
            log.supportRichText = true;
            log.rectTransform.Fill();
            log.rectTransform.offsetMin = new Vector2(10, 6); log.rectTransform.offsetMax = new Vector2(-10, 0);
            log.Outlined(0.9f);
            canvas.gameObject.SetActive(false);
        }

        public void Show(bool on)
        {
            canvas.gameObject.SetActive(on);
            if (!on) Close(false);
        }

        public void Update(bool inMission, bool canOpen)
        {
            if (!inMission) return;
            if (!Typing && canOpen && Input.GetKeyDown(Bindings.Key(Bindings.Action.Chat))) Open();
            else if (Typing && Input.GetKeyDown(KeyCode.Tab)) { channel = channel == ChatChannel.Room ? ChatChannel.World : ChatChannel.Room; PaintChannel(); input.ActivateInputField(); }
            else if (Typing && Input.GetKeyDown(KeyCode.Escape)) Close(true);
            float now = Time.realtimeSinceStartup;
            var lines = new List<string>();
            for (int i = ChatLog.Lines.Count - 1; i >= 0 && lines.Count < (Typing ? 10 : 6); i--)
                if (Typing || now - ChatLog.Lines[i].Time < 12f) lines.Insert(0, ChatLog.Format(ChatLog.Lines[i]));
            string text = string.Join("\n", lines);
            if (log.text != text)
            {
                log.text = text;
                backing.gameObject.SetActive(lines.Count > 0);
                backing.sizeDelta = new Vector2(0, lines.Count == 0 ? 0 : Mathf.Min(200, log.preferredHeight + 12));
            }
        }

        public void OpenForCapture() => Open();

        void Open()
        {
            Typing = true;
            GameClient.LockCursor(false);
            inputRow = Ui.Box(canvas.transform, new Color(0.04f, 0.043f, 0.05f, 0.6f), "ChatInput").rectTransform;
            inputRow.Place(new Vector2(0, 0), new Vector2(28, 176), new Vector2(520, 36), new Vector2(0, 0));
            var outline = inputRow.gameObject.AddComponent<Outline>();
            outline.effectColor = Palette.Amber; outline.effectDistance = new Vector2(1, -1);
            channelTag = Ui.Label(inputRow, "", 13, Palette.Teammate, TextAnchor.MiddleCenter, true);
            channelTag.rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(64, 36), new Vector2(0, 0.5f));
            input = Widgets.Input(inputRow, "輸入訊息，Enter 送出 · Tab 切換頻道 · /w 帳號 私訊 · Esc 取消", false, ChatChannel.MaxLength + 24);
            var rt = input.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(64, 0); rt.offsetMax = Vector2.zero;
            input.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            input.onEndEdit.AddListener(text =>
            {
                if (!Typing) return;
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                {
                    if (!string.IsNullOrWhiteSpace(text)) Send?.Invoke(channel, text.Trim());
                    Close(true);
                }
            });
            PaintChannel();
            input.ActivateInputField();
        }

        void PaintChannel()
        {
            channelTag.text = channel == ChatChannel.Room ? "房間" : "全頻";
            channelTag.color = channel == ChatChannel.Room ? Palette.Teammate : Palette.Muted;
        }

        void Close(bool relock)
        {
            if (!Typing) return;
            Typing = false;
            ClosedFrame = Time.frameCount;
            if (inputRow != null) UnityEngine.Object.Destroy(inputRow.gameObject);
            inputRow = null; input = null;
            if (relock) GameClient.LockCursor(true);
        }
    }
}
