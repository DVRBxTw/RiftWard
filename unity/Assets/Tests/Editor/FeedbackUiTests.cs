using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RiftWard.Client;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.TestTools;
using UnityEditor;

namespace RiftWard.Tests
{
    public class FeedbackUiTests
    {
        GameObject appObject;
        Screens screens;
        Canvas canvas;
        EventSystem events;
        bool ownsEvents;

        static FieldInfo Field(object owner, string name) => owner.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        static T Get<T>(object owner, string name) => (T)Field(owner, name).GetValue(owner);
        static void Set(object owner, string name, object value) => Field(owner, name).SetValue(owner, value);

        [SetUp]
        public void SetUp()
        {
            ownsEvents = EventSystem.current == null;
            appObject = new GameObject("Feedback UI regression");
            var app = appObject.AddComponent<AppRoot>(); // Start does not run in EditMode.
            Set(app, "<Profile>k__BackingField", new Profile { Username = "UI test" });
            Set(app, "<Api>k__BackingField", new AccountApi(app));
            screens = new Screens(app);
            canvas = Get<Canvas>(screens, "canvas");
            events = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            // EventSystem does not run OnEnable automatically outside PlayMode.
            typeof(EventSystem).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(events, null);
            typeof(BaseRaycaster).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(canvas.GetComponent<GraphicRaycaster>(), null);
            screens.ShowFeedback(false); // Never issue an HTTP request.
            Layout();
        }

        void Layout()
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(screens.Root);
            Canvas.ForceUpdateCanvases();
        }

        [TearDown]
        public void TearDown()
        {
            if (screens != null)
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                ChatLog.Added -= (Action<ChatLog.Line>)Delegate.CreateDelegate(typeof(Action<ChatLog.Line>), screens,
                    typeof(Screens).GetMethod("OnChatLine", flags));
                FriendPresence.Updated -= (Action)Delegate.CreateDelegate(typeof(Action), screens,
                    typeof(Screens).GetMethod("RepaintPresence", flags));
            }
            if (canvas != null)
            {
                typeof(BaseRaycaster).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(canvas.GetComponent<GraphicRaycaster>(), null);
                UnityEngine.Object.DestroyImmediate(canvas.gameObject);
            }
            if (appObject != null) UnityEngine.Object.DestroyImmediate(appObject);
            if (ownsEvents && events != null)
            {
                typeof(EventSystem).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(events, null);
                UnityEngine.Object.DestroyImmediate(events.gameObject);
            }
        }

        [Test]
        public void MineRefreshPreservesDraftInputFocusAndSendButton()
        {
            var body = Get<InputField>(screens, "feedbackBody");
            var send = Get<Button>(screens, "feedbackSend");
            body.text = "第二章無法繼續，這是尚未送出的草稿。";
            events.SetSelectedGameObject(body.gameObject);
            Set(screens, "myFeedback", new List<Dictionary<string, object>>());
            typeof(Screens).GetMethod("RefreshFeedbackList", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(screens, null);
            Layout();
            Assert.That(Get<InputField>(screens, "feedbackBody"), Is.SameAs(body));
            Assert.That(Get<Button>(screens, "feedbackSend"), Is.SameAs(send));
            Assert.That(body.text, Is.EqualTo("第二章無法繼續，這是尚未送出的草稿。"));
            Assert.That(events.currentSelectedGameObject, Is.SameAs(body.gameObject));
            Assert.That(Get<RectTransform>(screens, "feedbackList").GetComponentsInChildren<Text>()
                .Any(t => t.text == "還沒有送出過回饋。"), Is.True);
        }

        [Test]
        public void RebuildingFormKeepsDraftAndInFlightButtonState()
        {
            Get<InputField>(screens, "feedbackBody").text = "分類切換後仍應保留的草稿";
            Set(screens, "feedbackKind", "bug");
            Set(screens, "feedbackSending", true);
            // Destroy immediately in EditMode before the production rebuild's deferred Destroy.
            UnityEngine.Object.DestroyImmediate(screens.Root.gameObject);
            screens.ShowFeedback(false);
            Layout();
            Assert.That(Get<InputField>(screens, "feedbackBody").text, Is.EqualTo("分類切換後仍應保留的草稿"));
            Assert.That(Get<Button>(screens, "feedbackSend").interactable, Is.False);
            Assert.That(Get<Text>(screens, "socialStatus").text, Is.EqualTo("送出中…"));
        }
    }

    // Only pointer testing enters PlayMode: native Graphic.depth is not populated in EditMode.
    public class FeedbackPointerUiTests
    {
        GameObject appObject;
        Screens screens;
        Canvas canvas;
        EventSystem events;
        bool ownsEvents;
        bool oldOptionsEnabled;
        EnterPlayModeOptions oldOptions;

        static FieldInfo Field(object owner, string name) => owner.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        static T Get<T>(object owner, string name) => (T)Field(owner, name).GetValue(owner);
        static void Set(object owner, string name, object value) => Field(owner, name).SetValue(owner, value);

        [SetUp]
        public void PrepareDisabledAppBeforeBootstrap()
        {
            oldOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            oldOptions = EditorSettings.enterPlayModeOptions;
            // Keep the disabled app through entry so GameClient.Bootstrap finds it and does nothing.
            // Disabling its component also prevents AppRoot.Start from connecting/building the game.
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            appObject = new GameObject("Feedback pointer regression");
            appObject.AddComponent<AppRoot>().enabled = false;
        }

        [UnityTest]
        public IEnumerator SendButtonIsTopPointerTargetAndShortDraftShowsValidation()
        {
            yield return new EnterPlayMode(false);
            var app = appObject.GetComponent<AppRoot>();
            Assert.That(app.enabled, Is.False);
            Assert.That(app.Client, Is.Null, "AppRoot.Start must not run in this UI-only test.");
            Set(app, "<Profile>k__BackingField", new Profile { Username = "UI test" });
            Set(app, "<Api>k__BackingField", new AccountApi(app));
            ownsEvents = UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null;
            screens = new Screens(app);
            canvas = Get<Canvas>(screens, "canvas");
            events = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            EventSystem.current = events;
            screens.ShowFeedback(false); // No HTTP requests; the short draft stays in client validation.
            yield return null;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(screens.Root);
            yield return null;
            Canvas.ForceUpdateCanvases();

            var send = Get<Button>(screens, "feedbackSend");
            var rect = send.GetComponent<RectTransform>();
            Assert.That(send.targetGraphic.depth, Is.GreaterThanOrEqualTo(0), "Runtime canvas must have generated native graphic depth.");
            var center = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var pointer = new PointerEventData(events) { position = center, button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>();
            events.RaycastAll(pointer, hits);
            Assert.That(rect.rect.height, Is.GreaterThanOrEqualTo(50));
            Assert.That(hits, Is.Not.Empty);
            Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.SameAs(send),
                "A graphic outside the send button must not consume the click.");
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Assert.That(Get<Text>(screens, "socialStatus").text, Is.EqualTo("內容至少 5 個字"));
            Assert.That(send.interactable, Is.True);

            var body = Get<InputField>(screens, "feedbackBody");
            body.text = "四人遊戲送出回饋測試";
            using (var reply = new FeedbackReply("{\"ok\":false,\"error\":\"暫時無法送出\"}"))
            {
                app.Api.Host = "127.0.0.1";
                app.Api.Port = reply.Port;
                ExecuteEvents.ExecuteHierarchy(send.gameObject, pointer, ExecuteEvents.pointerClickHandler);
                Assert.That(send.interactable, Is.False);
                float deadline = Time.realtimeSinceStartup + 5;
                while (!send.interactable && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(send.interactable, Is.True, "Failed submissions must allow retry");
                Assert.That(body.text, Is.EqualTo("四人遊戲送出回饋測試"));
                Assert.That(Get<Text>(screens, "socialStatus").text, Is.EqualTo("暫時無法送出"));
                Assert.That(reply.Request, Does.Contain("POST /api/feedback/send"));
            }
            using (var reply = new FeedbackReply("{\"ok\":true,\"feedback\":[]}"))
            {
                app.Api.Port = reply.Port;
                ExecuteEvents.ExecuteHierarchy(send.gameObject, pointer, ExecuteEvents.pointerClickHandler);
                body.text = "送出期間寫下的新草稿";
                float deadline = Time.realtimeSinceStartup + 5;
                while (!send.interactable && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(send.interactable, Is.True);
                Assert.That(Get<Text>(screens, "socialStatus").text, Is.EqualTo("已送出，感謝你的回饋！"));
                Assert.That(body.text, Is.EqualTo("送出期間寫下的新草稿"));
                Assert.That(reply.Request, Does.Contain("四人遊戲送出回饋測試"));
            }
        }

        sealed class FeedbackReply : IDisposable
        {
            readonly System.Net.Sockets.TcpListener listener;
            readonly System.Threading.Tasks.Task worker;
            public string Request;
            public int Port => ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

            public FeedbackReply(string json)
            {
                listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
                listener.Start();
                worker = System.Threading.Tasks.Task.Run(() =>
                {
                    using var client = listener.AcceptTcpClient();
                    client.ReceiveTimeout = 5000;
                    using var stream = client.GetStream();
                    var header = new System.Text.StringBuilder();
                    while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                    {
                        int next = stream.ReadByte();
                        if (next < 0) throw new System.IO.EndOfStreamException();
                        header.Append((char)next);
                    }
                    int length = 0;
                    foreach (string line in header.ToString().Split('\n'))
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            length = int.Parse(line.Substring(15).Trim());
                    var payload = new byte[length];
                    for (int read = 0; read < length;)
                    {
                        int count = stream.Read(payload, read, length - read);
                        if (count == 0) throw new System.IO.EndOfStreamException();
                        read += count;
                    }
                    Request = header + System.Text.Encoding.UTF8.GetString(payload);
                    byte[] response = System.Text.Encoding.UTF8.GetBytes(json);
                    byte[] prefix = System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {response.Length}\r\nConnection: close\r\n\r\n");
                    stream.Write(prefix, 0, prefix.Length);
                    stream.Write(response, 0, response.Length);
                });
            }

            public void Dispose()
            {
                listener.Stop();
                if (!worker.Wait(6000)) throw new TimeoutException("Feedback API test worker did not finish");
            }
        }

        [UnityTearDown]
        public IEnumerator RestoreEditorAndDestroyUi()
        {
            try
            {
                if (screens != null)
                {
                    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    ChatLog.Added -= (Action<ChatLog.Line>)Delegate.CreateDelegate(typeof(Action<ChatLog.Line>), screens,
                        typeof(Screens).GetMethod("OnChatLine", flags));
                    FriendPresence.Updated -= (Action)Delegate.CreateDelegate(typeof(Action), screens,
                        typeof(Screens).GetMethod("RepaintPresence", flags));
                }
                if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas.gameObject);
                if (appObject != null) UnityEngine.Object.DestroyImmediate(appObject);
                if (ownsEvents && events != null) UnityEngine.Object.DestroyImmediate(events.gameObject);
                if (Application.isPlaying) yield return new ExitPlayMode();
            }
            finally
            {
                EditorSettings.enterPlayModeOptions = oldOptions;
                EditorSettings.enterPlayModeOptionsEnabled = oldOptionsEnabled;
            }
        }
    }
}
