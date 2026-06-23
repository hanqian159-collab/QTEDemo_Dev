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
        private const float BAR_SPEED = 1.8f;
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

        private static RectTransform barRailRt;
        private static RectTransform indicatorRt;
        private static Image indicatorImg;
        private static RectTransform indicatorDotRt;
        private static RectTransform targetZoneRt;
        private static RectTransform perfectZoneRt;
        private static Text timerLabel;
        private static Text instructionLabel;
        private static Text resultLabel;
        private static GameObject panelObj, tipObj;

        [ModInitialize]
        public static void Initialize(ModConfig config)
        {
            Commands.Log(ModTag, "QTE Demo 模组已加载 (Q/鼠标左键)");
        }

        [HookAfter(typeof(Fight_PlayerTurn), "Init")]
        public static void OnPlayerTurnInit(Fight_PlayerTurn instance)
        {
            try { if (!qteActive) StartQTE(); }
            catch (Exception ex) { Commands.LogError(ModTag, "Hook失败: " + ex.Message); }
        }

        private static void StartQTE()
        {
            EnsureCanvas();
            // 重置 UI：隐藏上次结果，显示计时和提示
            if (resultLabel != null) resultLabel.gameObject.SetActive(false);
            if (timerLabel != null) { timerLabel.gameObject.SetActive(true); timerLabel.text = "剩余: 3.0s"; timerLabel.color = new Color(0.9f, 0.9f, 1f); }
            if (instructionLabel != null) instructionLabel.gameObject.SetActive(true);
            if (tipObj != null) tipObj.SetActive(false);

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

                float step = dt * BAR_SPEED;
                barNorm += step * barDir;
                if (barNorm >= 1f) { barNorm = 1f; barDir = -1f; }
                if (barNorm <= 0f) { barNorm = 0f; barDir = 1f; }

                bool triggered = false;
                try { if (Keyboard.current != null && Keyboard.current[Key.Q].wasPressedThisFrame) triggered = true; } catch { }
                try { if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) triggered = true; } catch { }

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
            if (runner != null) runner.StartCoroutine(HideAfter(1.8f));
        }

        private static IEnumerator HideAfter(float s)
        {
            yield return new WaitForSeconds(s);
            // 只隐藏面板，但保留结果文本的引用（供下次 StartQTE 清理）
            if (qteRoot != null) qteRoot.SetActive(false);
        }

        private static void ApplyBuff(int level)
        {
            try
            {
                var env = ScriptExecutor.luaEnv;
                if (env == null) return;
                var fn = env.Global.Get<Action<string>>("QTEDemoApplyBuff");
                if (fn != null) fn(level.ToString());
            }
            catch { }
        }

        // ══════════════════════════════════════════════════
        //  UI 创建 — 紧凑布局 + 高可见度滑块
        // ══════════════════════════════════════════════════
        private static void EnsureCanvas()
        {
            if (canvasReady && qteRoot != null) return;

            qteRoot = new GameObject("QTEDemo_Canvas");
            var canvas = qteRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = qteRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            qteRoot.AddComponent<GraphicRaycaster>();
            UnityEngine.Object.DontDestroyOnLoad(qteRoot);
            qteRoot.SetActive(false);

            // 字体
            Font font = null;
            try { font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 16); } catch { }
            if (font == null) try { font = Font.CreateDynamicFontFromOSFont("SimHei", 16); } catch { }
            if (font == null) try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }

            // ── 背景遮罩 ──
            var bg = new GameObject("BG", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(qteRoot.transform, false);
            var bgImg = bg.GetComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 0.50f);
            var bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one; bgRt.sizeDelta = Vector2.zero;

            // ── 主面板 (420x155) ──
            panelObj = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panelObj.transform.SetParent(qteRoot.transform, false);
            var pnlImg = panelObj.GetComponent<Image>();
            pnlImg.color = new Color(0.06f, 0.08f, 0.14f, 0.96f);
            var pnlRt = panelObj.GetComponent<RectTransform>();
            pnlRt.anchorMin = pnlRt.anchorMax = new Vector2(0.5f, 0.5f);
            pnlRt.pivot = new Vector2(0.5f, 0.5f);
            pnlRt.sizeDelta = new Vector2(420f, 155f);
            pnlRt.anchoredPosition = new Vector2(0f, 0f);

            // ── 标题 (靠上) ──
            CreateText("Title", panelObj.transform, new Vector2(300, 24), new Vector2(0f, -12f),
                "⚡ QTE 判定！", 18, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.3f), font);

            // ── 滑轨背景 — 高对比度 ──
            var rail = new GameObject("Rail", typeof(RectTransform), typeof(Image));
            rail.transform.SetParent(panelObj.transform, false);
            var railImg = rail.GetComponent<Image>();
            railImg.color = new Color(0.15f, 0.17f, 0.25f, 1f); // 深色滑轨背景
            barRailRt = rail.GetComponent<RectTransform>();
            barRailRt.anchorMin = barRailRt.anchorMax = new Vector2(0.5f, 0.5f);
            barRailRt.pivot = new Vector2(0.5f, 0.5f);
            barRailRt.sizeDelta = new Vector2(370f, 26f);
            barRailRt.anchoredPosition = new Vector2(0f, 16f);

            // ── 滑轨边框线 (上下两条细线，增加视觉层次) ──
            for (int side = 0; side < 2; side++)
            {
                var border = new GameObject("Border" + side, typeof(RectTransform), typeof(Image));
                border.transform.SetParent(barRailRt, false);
                var bImg = border.GetComponent<Image>();
                bImg.color = new Color(0.3f, 0.35f, 0.5f, 0.6f);
                var bRt = border.GetComponent<RectTransform>();
                bRt.anchorMin = new Vector2(0f, side == 0 ? 1f : 0f);
                bRt.anchorMax = new Vector2(1f, side == 0 ? 1f : 0f);
                bRt.pivot = new Vector2(0.5f, side == 0 ? 1f : 0f);
                bRt.sizeDelta = new Vector2(0f, 1f);
                bRt.anchoredPosition = Vector2.zero;
            }

            // ── 目标区 ──
            var tz = new GameObject("TargetZone", typeof(RectTransform), typeof(Image));
            tz.transform.SetParent(barRailRt, false);
            targetZoneRt = tz.GetComponent<RectTransform>();
            var tzImg = tz.GetComponent<Image>();
            // 用半透明绿底 + 边框色
            tzImg.color = new Color(0.15f, 0.80f, 0.30f, 0.30f);
            targetZoneRt.anchorMin = targetZoneRt.anchorMax = new Vector2(0f, 0f);
            targetZoneRt.pivot = new Vector2(0.5f, 0.5f);
            targetZoneRt.sizeDelta = new Vector2(370f * TARGET_ZONE_RADIUS * 2f, 0f);

            // ── 目标区边框（亮绿色） ──
            var tzBorder = new GameObject("TZBorder", typeof(RectTransform), typeof(Image));
            tzBorder.transform.SetParent(targetZoneRt, false);
            var tzBImg = tzBorder.GetComponent<Image>();
            tzBImg.color = new Color(0.2f, 0.95f, 0.4f, 0.5f);
            var tzBRt = tzBorder.GetComponent<RectTransform>();
            tzBRt.anchorMin = Vector2.zero; tzBRt.anchorMax = Vector2.one;
            tzBRt.sizeDelta = Vector2.zero;
            tzBRt.anchoredPosition = Vector2.zero;

            // ── 完美区（亮金色） ──
            var pz = new GameObject("PerfectZone", typeof(RectTransform), typeof(Image));
            pz.transform.SetParent(targetZoneRt, false);
            perfectZoneRt = pz.GetComponent<RectTransform>();
            var pzImg = pz.GetComponent<Image>();
            pzImg.color = new Color(1f, 0.85f, 0.10f, 0.55f);
            perfectZoneRt.anchorMin = new Vector2(0.5f - PERFECT_RADIUS / TARGET_ZONE_RADIUS * 0.5f, 0f);
            perfectZoneRt.anchorMax = new Vector2(0.5f + PERFECT_RADIUS / TARGET_ZONE_RADIUS * 0.5f, 1f);
            perfectZoneRt.sizeDelta = Vector2.zero;

            // ── 指示器（白色竖条 + 顶部圆点） ──
            var ind = new GameObject("Indicator", typeof(RectTransform), typeof(Image));
            ind.transform.SetParent(barRailRt, false);
            indicatorRt = ind.GetComponent<RectTransform>();
            indicatorImg = ind.GetComponent<Image>();
            indicatorImg.color = Color.white;
            indicatorRt.anchorMin = indicatorRt.anchorMax = new Vector2(0f, 0f);
            indicatorRt.pivot = new Vector2(0.5f, 0.5f);
            indicatorRt.sizeDelta = new Vector2(6f, 0f);  // 6px 竖条

            // 指示器顶端圆点
            var dot = new GameObject("IndicatorDot", typeof(RectTransform), typeof(Image));
            dot.transform.SetParent(indicatorRt, false);
            indicatorDotRt = dot.GetComponent<RectTransform>();
            var dotImg = dot.GetComponent<Image>();
            dotImg.color = new Color(1f, 1f, 1f, 0.9f);
            indicatorDotRt.anchorMin = indicatorDotRt.anchorMax = new Vector2(0.5f, 1f);
            indicatorDotRt.pivot = new Vector2(0.5f, 1f);
            indicatorDotRt.sizeDelta = new Vector2(14f, 14f);
            indicatorDotRt.anchoredPosition = new Vector2(0f, -2f);

            // ── 底部栏：提示 + 计时 (同行左右分布) ──
            instructionLabel = CreateText("Instruction", panelObj.transform, new Vector2(260, 20), new Vector2(-55f, -60f),
                "按 [Q] 或点击鼠标左键停止", 13, TextAnchor.MiddleLeft, new Color(0.7f, 0.75f, 0.85f), font);

            timerLabel = CreateText("Timer", panelObj.transform, new Vector2(100, 20), new Vector2(140f, -60f),
                "剩余: 3.0s", 14, TextAnchor.MiddleRight, new Color(0.9f, 0.9f, 1f), font);

            // ── 结果（居中） ──
            resultLabel = CreateText("Result", panelObj.transform, new Vector2(350, 44), new Vector2(0f, -48f),
                "", 36, TextAnchor.MiddleCenter, Color.white, font);
            resultLabel.gameObject.SetActive(false);

            // ── 底部小提示（判定档位说明） ──
            tipObj = new GameObject("Tip", typeof(RectTransform), typeof(Text));
            tipObj.transform.SetParent(panelObj.transform, false);
            var tipTxt = tipObj.GetComponent<Text>();
            if (font != null) tipTxt.font = font;
            tipTxt.text = "🟡完美 +50%   🟢优秀 +30%   🔵成功 +15%";
            tipTxt.fontSize = 11;
            tipTxt.alignment = TextAnchor.MiddleCenter;
            tipTxt.color = new Color(0.5f, 0.55f, 0.65f);
            var tipRt = tipObj.GetComponent<RectTransform>();
            tipRt.anchorMin = tipRt.anchorMax = new Vector2(0.5f, 0f);
            tipRt.pivot = new Vector2(0.5f, 0f);
            tipRt.sizeDelta = new Vector2(380f, 18f);
            tipRt.anchoredPosition = new Vector2(0f, 6f);
            tipObj.SetActive(false);

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

        private static void UpdateQTEDisplay()
        {
            if (!canvasReady || qteRoot == null) return;
            float railWidth = 370f;
            if (barRailRt != null) railWidth = Mathf.Max(1f, barRailRt.rect.width);

            if (indicatorRt != null)
                indicatorRt.anchoredPosition = new Vector2((barNorm - 0.5f) * railWidth, 0f);
            if (targetZoneRt != null)
                targetZoneRt.anchoredPosition = new Vector2((targetCenter - 0.5f) * railWidth, 0f);
            if (timerLabel != null)
            {
                timerLabel.text = string.Format("剩余: {0:F1}s", qteTimeLeft);
                timerLabel.color = qteTimeLeft <= 1f ? new Color(1f, 0.3f, 0.2f) : new Color(0.9f, 0.9f, 1f);
            }
        }

        private static void ShowResult(string rank)
        {
            if (resultLabel == null) return;
            // 隐藏计时和提示，显示结果
            if (timerLabel != null) timerLabel.gameObject.SetActive(false);
            if (instructionLabel != null) instructionLabel.gameObject.SetActive(false);
            if (tipObj != null) tipObj.SetActive(true);
            resultLabel.gameObject.SetActive(true);

            switch (rank)
            {
                case "PERFECT":
                    resultLabel.text = "完美！+50%";
                    resultLabel.color = new Color(1f, 0.9f, 0.1f);
                    resultLabel.fontSize = 38;
                    break;
                case "GREAT":
                    resultLabel.text = "优秀！+30%";
                    resultLabel.color = new Color(0.2f, 0.95f, 0.4f);
                    resultLabel.fontSize = 34;
                    break;
                case "GOOD":
                    resultLabel.text = "成功！+15%";
                    resultLabel.color = new Color(0.3f, 0.7f, 1f);
                    resultLabel.fontSize = 34;
                    break;
                default:
                    resultLabel.text = "错过… 无加成";
                    resultLabel.color = new Color(0.6f, 0.6f, 0.6f);
                    resultLabel.fontSize = 30;
                    break;
            }
        }
    }

    public class CoroutineRunner : MonoBehaviour { }
}
