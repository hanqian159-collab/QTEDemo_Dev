using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Witch;
using Witch.Mod;

namespace QTEDemo
{
    /// <summary>QTE 示例模组 — 回合开始滑块判定，Q键/鼠标左键停止</summary>
    public static class Entry
    {
        public const string ModTag = "QTEDemo";
        private const float QTE_DURATION = 3f;
        private const float BAR_SPEED = 1.6f;
        private const float TARGET_ZONE_RADIUS = 0.16f;
        private const float PERFECT_RADIUS = 0.04f;
        private const float GREAT_RADIUS = 0.10f;

        private static GameObject qteRoot;
        private static bool qteActive;
        private static float qteTimeLeft;
        private static float barNorm;
        private static float barDir = 1f;
        private static float targetCenter;
        private static CoroutineRunner runner;
        private static bool canvasReady;

        // UI 组件
        private static RectTransform barRailRt;
        private static RectTransform indicatorRt;
        private static RectTransform targetZoneRt;
        private static Text timerLabel;
        private static Text instructionLabel;
        private static Text resultLabel;
        private static GameObject panelObj;

        [ModInitialize]
        public static void Initialize(ModConfig config)
        {
            Commands.Log(ModTag, "QTE Demo 模组已加载 (默认键: Q / 鼠标左键)");
        }

        // ── 玩家回合开始 → 触发 QTE ──
        [HookAfter(typeof(Fight_PlayerTurn), "Init")]
        public static void OnPlayerTurnInit(Fight_PlayerTurn instance)
        {
            try { if (!qteActive) { StartQTE(); } }
            catch (Exception ex) { Commands.LogError(ModTag, "Hook失败: " + ex.Message); }
        }

        // ── QTE 流程 ──
        private static void StartQTE()
        {
            EnsureCanvas();
            targetCenter = UnityEngine.Random.Range(0.18f, 0.82f);
            qteTimeLeft = QTE_DURATION;
            barNorm = 0f; barDir = 1f;
            qteActive = true;
            UpdateQTEDisplay();
            qteRoot.SetActive(true);

            if (runner == null)
            {
                var go = new GameObject("QTEDemo_Runner");
                UnityEngine.Object.DontDestroyOnLoad(go);
                runner = go.AddComponent<CoroutineRunner>();
            }
            runner.StartCoroutine(QTELoop());
        }

        private static IEnumerator QTELoop()
        {
            while (qteActive)
            {
                float dt = Time.deltaTime;
                qteTimeLeft -= dt;
                if (qteTimeLeft <= 0f) { ResolveQTE(float.NaN); yield break; }

                // 滑块移动 (ping-pong)
                float step = dt * BAR_SPEED;
                barNorm += step * barDir;
                if (barNorm >= 1f) { barNorm = 1f; barDir = -1f; }
                if (barNorm <= 0f) { barNorm = 0f; barDir = 1f; }

                // 判定触发: Q 键 或 鼠标左键
                bool triggered = false;
                try
                {
                    var kb = Keyboard.current;
                    if (kb != null && kb[Key.Q].wasPressedThisFrame) triggered = true;
                }
                catch { }
                try
                {
                    var mouse = Mouse.current;
                    if (mouse != null && mouse.leftButton.wasPressedThisFrame) triggered = true;
                }
                catch { }

                if (triggered) { ResolveQTE(barNorm); yield break; }

                UpdateQTEDisplay();
                yield return null;
            }
        }

        private static void ResolveQTE(float hit)
        {
            qteActive = false;
            string rank; int level;
            if (float.IsNaN(hit)) { rank = "MISS"; level = 0; }
            else
            {
                float d = Mathf.Abs(hit - targetCenter);
                if (d <= PERFECT_RADIUS) { rank = "PERFECT"; level = 50; }
                else if (d <= GREAT_RADIUS) { rank = "GREAT"; level = 30; }
                else if (d <= TARGET_ZONE_RADIUS) { rank = "GOOD"; level = 15; }
                else { rank = "MISS"; level = 0; }
            }
            Commands.Log(ModTag, "QTE: " + rank + " level=" + level);
            ShowResult(rank);
            if (level > 0) ApplyBuff(level);
            if (runner != null) runner.StartCoroutine(HideAfter(1.5f));
        }

        private static IEnumerator HideAfter(float s) { yield return new WaitForSeconds(s); if (qteRoot != null) qteRoot.SetActive(false); }

        // ── Lua 桥接：应用 buff_extraordinary ──
        private static void ApplyBuff(int level)
        {
            try
            {
                var env = ScriptExecutor.luaEnv;
                if (env == null) { Commands.LogError(ModTag, "luaEnv 不可用"); return; }
                var fn = env.Global.Get<Action<string>>("QTEDemoApplyBuff");
                if (fn != null) fn(level.ToString());
                else Commands.LogError(ModTag, "QTEDemoApplyBuff 未注册 (Scripts/Entry.lua 存在吗?)");
            }
            catch (Exception ex) { Commands.LogError(ModTag, "ApplyBuff: " + ex.Message); }
        }

        // ── 创建 UI ──
        private static void EnsureCanvas()
        {
            if (canvasReady && qteRoot != null) return;

            qteRoot = new GameObject("QTEDemo_Canvas");
            var canvas = qteRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            qteRoot.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            qteRoot.AddComponent<GraphicRaycaster>();
            UnityEngine.Object.DontDestroyOnLoad(qteRoot);
            qteRoot.SetActive(false);

            // ── 背景遮罩 ──
            var bg = new GameObject("BG", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(qteRoot.transform, false);
            var bgImg = bg.GetComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 0.55f);
            var bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one; bgRt.sizeDelta = Vector2.zero;

            // ── 主面板 ──
            panelObj = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panelObj.transform.SetParent(qteRoot.transform, false);
            var pnlImg = panelObj.GetComponent<Image>();
            pnlImg.color = new Color(0.08f, 0.10f, 0.16f, 0.95f);
            var pnlRt = panelObj.GetComponent<RectTransform>();
            pnlRt.anchorMin = pnlRt.anchorMax = new Vector2(0.5f, 0.5f);
            pnlRt.pivot = new Vector2(0.5f, 0.5f);
            pnlRt.sizeDelta = new Vector2(560f, 220f);
            pnlRt.anchoredPosition = Vector2.zero;

            // ── 创建字体 ──
            Font font = null;
            try { font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 20); } catch { }
            if (font == null) try { font = Font.CreateDynamicFontFromOSFont("SimHei", 20); } catch { }
            if (font == null) try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }

            // ── 标题 ──
            CreateText("Title", panelObj.transform, new Vector2(400, 36), new Vector2(0, -18),
                "⚡ QTE 判定！", 24, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.3f), font);

            // ── 操作提示 ──
            instructionLabel = CreateText("Instruction", panelObj.transform, new Vector2(400, 22), new Vector2(0, -50),
                "按 [Q] 或点击鼠标左键在目标区内停止", 15, TextAnchor.MiddleCenter, new Color(0.7f, 0.75f, 0.85f), font);

            // ── 滑轨背景 ──
            var rail = new GameObject("Rail", typeof(RectTransform), typeof(Image));
            rail.transform.SetParent(panelObj.transform, false);
            var railImg = rail.GetComponent<Image>();
            railImg.color = new Color(0.20f, 0.22f, 0.30f, 1f);
            barRailRt = rail.GetComponent<RectTransform>();
            barRailRt.anchorMin = barRailRt.anchorMax = new Vector2(0.5f, 0.5f);
            barRailRt.pivot = new Vector2(0.5f, 0.5f);
            barRailRt.sizeDelta = new Vector2(460f, 28f);
            barRailRt.anchoredPosition = new Vector2(0f, 20f);

            // ── 目标区（整体绿色背景） ──
            var tz = new GameObject("TargetZone", typeof(RectTransform), typeof(Image));
            tz.transform.SetParent(barRailRt, false);
            targetZoneRt = tz.GetComponent<RectTransform>();
            var tzImg = tz.GetComponent<Image>();
            tzImg.color = new Color(0.2f, 0.85f, 0.35f, 0.55f);
            targetZoneRt.anchorMin = targetZoneRt.anchorMax = new Vector2(0f, 0f);
            targetZoneRt.pivot = new Vector2(0.5f, 0.5f);
            targetZoneRt.sizeDelta = new Vector2(460f * TARGET_ZONE_RADIUS * 2f, 0f);

            // ── 完美区（金色内层） ──
            var pz = new GameObject("PerfectZone", typeof(RectTransform), typeof(Image));
            pz.transform.SetParent(targetZoneRt, false);
            var pzImg = pz.GetComponent<Image>();
            pzImg.color = new Color(1f, 0.9f, 0.15f, 0.75f);
            var pzRt = pz.GetComponent<RectTransform>();
            pzRt.anchorMin = new Vector2(0.5f - PERFECT_RADIUS / TARGET_ZONE_RADIUS * 0.5f, 0f);
            pzRt.anchorMax = new Vector2(0.5f + PERFECT_RADIUS / TARGET_ZONE_RADIUS * 0.5f, 1f);
            pzRt.sizeDelta = Vector2.zero;

            // ── 指示器（白色竖条） ──
            var ind = new GameObject("Indicator", typeof(RectTransform), typeof(Image));
            ind.transform.SetParent(barRailRt, false);
            indicatorRt = ind.GetComponent<RectTransform>();
            var indImg = ind.GetComponent<Image>();
            indImg.color = Color.white;
            indicatorRt.anchorMin = indicatorRt.anchorMax = new Vector2(0f, 0f);
            indicatorRt.pivot = new Vector2(0.5f, 0.5f);
            indicatorRt.sizeDelta = new Vector2(8f, 0f);

            // ── 计时 ──
            timerLabel = CreateText("Timer", panelObj.transform, new Vector2(200, 24), new Vector2(0, 74),
                "剩余: 3.0s", 18, TextAnchor.MiddleCenter, new Color(0.9f, 0.9f, 1f), font);

            // ── 结果 ──
            resultLabel = CreateText("Result", panelObj.transform, new Vector2(400, 50), new Vector2(0, -60),
                "", 38, TextAnchor.MiddleCenter, Color.white, font);
            resultLabel.gameObject.SetActive(false);

            canvasReady = true;
        }

        private static Text CreateText(string name, Transform parent, Vector2 size, Vector2 pos,
            string text, int fontSize, TextAnchor anchor, Color color, Font font)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var txt = go.GetComponent<Text>();
            if (font != null) txt.font = font;
            txt.text = text;
            txt.fontSize = fontSize;
            txt.alignment = anchor;
            txt.color = color;
            return txt;
        }

        // ── UI 帧更新 ──
        private static void UpdateQTEDisplay()
        {
            if (!canvasReady || qteRoot == null) return;

            float railWidth = 460f;
            if (barRailRt != null) railWidth = barRailRt.rect.width;

            if (indicatorRt != null)
                indicatorRt.anchoredPosition = new Vector2((barNorm - 0.5f) * railWidth, 0f);
            if (targetZoneRt != null)
                targetZoneRt.anchoredPosition = new Vector2((targetCenter - 0.5f) * railWidth, 0f);
            if (timerLabel != null)
            {
                timerLabel.text = string.Format("剩余: {0:F1}s", qteTimeLeft);
                timerLabel.color = qteTimeLeft <= 1f ? new Color(1f, 0.35f, 0.25f) : new Color(0.9f, 0.9f, 1f);
                timerLabel.fontSize = qteTimeLeft <= 1f ? 20 : 18;
            }
        }

        private static void ShowResult(string rank)
        {
            if (resultLabel == null) return;
            resultLabel.gameObject.SetActive(true);
            if (timerLabel != null) timerLabel.gameObject.SetActive(false);
            if (instructionLabel != null) instructionLabel.gameObject.SetActive(false);

            switch (rank)
            {
                case "PERFECT":
                    resultLabel.text = "完美！伤害 +50%";
                    resultLabel.color = new Color(1f, 0.9f, 0.15f);
                    resultLabel.fontSize = 42;
                    break;
                case "GREAT":
                    resultLabel.text = "优秀！伤害 +30%";
                    resultLabel.color = new Color(0.2f, 0.95f, 0.45f);
                    resultLabel.fontSize = 38;
                    break;
                case "GOOD":
                    resultLabel.text = "成功！伤害 +15%";
                    resultLabel.color = new Color(0.3f, 0.7f, 1f);
                    resultLabel.fontSize = 38;
                    break;
                default:
                    resultLabel.text = "错过… 无加成";
                    resultLabel.color = new Color(0.6f, 0.6f, 0.6f);
                    resultLabel.fontSize = 34;
                    break;
            }
        }
    }

    public class CoroutineRunner : MonoBehaviour { }
}
