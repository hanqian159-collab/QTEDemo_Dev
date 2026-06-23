using System;
using System.Collections;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Witch;
using Witch.Mod;
using Witch.UI.Window;

namespace QTEDemo
{
    /// <summary>QTE 示例模组 — 回合开始时出现滑条判定，按空格停在目标区域获得伤害加成</summary>
    public static class Entry
    {
        public const string ModTag = "QTEDemo";

        // 参数
        private const float QTE_DURATION = 3f;
        private const float BAR_SPEED = 1.6f;
        private const float TARGET_ZONE_RADIUS = 0.16f;
        private const float PERFECT_RADIUS = 0.04f;
        private const float GREAT_RADIUS = 0.10f;
        private const Key QTE_KEY = Key.Space;

        // 状态
        private static GameObject qteRoot;
        private static bool qteActive;
        private static float qteTimeLeft;
        private static float barNorm;
        private static float barDir = 1f;
        private static float targetCenter;
        private static CoroutineRunner runner;
        private static bool canvasReady;

        // UI 引用
        private static RectTransform barRailRt;
        private static RectTransform indicatorRt;
        private static RectTransform targetZoneRt;
        private static TextMeshProUGUI timerLabel;
        private static TextMeshProUGUI instructionLabel;
        private static TextMeshProUGUI resultLabel;

        [ModInitialize]
        public static void Initialize(ModConfig config)
        {
            Commands.Log(ModTag, "QTE Demo 模组已加载");
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
            targetCenter = UnityEngine.Random.Range(0.15f, 0.85f);
            qteTimeLeft = QTE_DURATION;
            barNorm = 0f; barDir = 1f;
            qteActive = true;
            qteRoot.SetActive(true);
            UpdateQTEDisplay();
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

                if (Keyboard.current != null && Keyboard.current[QTE_KEY].wasPressedThisFrame)
                { ResolveQTE(barNorm); yield break; }

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
            if (runner != null) runner.StartCoroutine(HideAfter(1.2f));
        }

        private static IEnumerator HideAfter(float s) { yield return new WaitForSeconds(s); if (qteRoot != null) qteRoot.SetActive(false); }

        private static void ApplyBuff(int level)
        {
            try
            {
                var env = ScriptExecutor.luaEnv;
                if (env == null) { Commands.LogError(ModTag, "luaEnv 不可用"); return; }
                var fn = env.Global.Get<Action<string>>("QTEDemoApplyBuff");
                if (fn != null) fn(level.ToString());
                else Commands.LogError(ModTag, "QTEDemoApplyBuff 未注册");
            }
            catch (Exception ex) { Commands.LogError(ModTag, "ApplyBuff: " + ex.Message); }
        }

        private static void EnsureCanvas()
        {
            if (canvasReady && qteRoot != null) return;

            qteRoot = new GameObject("QTEDemo_Canvas");
            var canvas = qteRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 31000;
            qteRoot.AddComponent<CanvasScaler>();
            qteRoot.AddComponent<GraphicRaycaster>();
            UnityEngine.Object.DontDestroyOnLoad(qteRoot);
            qteRoot.SetActive(false);

            // 背景遮罩
            var bg = new GameObject("BG", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(qteRoot.transform, false);
            var bgImg = bg.GetComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 0.55f);
            var bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.sizeDelta = Vector2.zero;

            // 面板
            var pnl = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            pnl.transform.SetParent(qteRoot.transform, false);
            var pnlImg = pnl.GetComponent<Image>();
            pnlImg.color = new Color(0.10f, 0.12f, 0.18f, 0.92f);
            var pnlRt = pnl.GetComponent<RectTransform>();
            pnlRt.anchorMin = pnlRt.anchorMax = new Vector2(0.5f, 0.5f);
            pnlRt.pivot = new Vector2(0.5f, 0.5f);
            pnlRt.sizeDelta = new Vector2(520f, 200f);
            pnlRt.anchoredPosition = Vector2.zero;

            // 标题
            var title = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
            title.transform.SetParent(pnl.transform, false);
            var tTmp = title.GetComponent<TextMeshProUGUI>();
            tTmp.text = "⚡ QTE 判定！"; tTmp.fontSize = 22;
            tTmp.alignment = TextAlignmentOptions.Center;
            tTmp.color = new Color(1f, 0.85f, 0.3f);
            var tRt = title.GetComponent<RectTransform>();
            tRt.anchorMin = tRt.anchorMax = new Vector2(0.5f, 1f);
            tRt.pivot = new Vector2(0.5f, 1f);
            tRt.anchoredPosition = new Vector2(0f, -18f);
            tRt.sizeDelta = new Vector2(400f, 30f);

            // 滑轨
            var rail = new GameObject("Rail", typeof(RectTransform), typeof(Image));
            rail.transform.SetParent(pnl.transform, false);
            rail.GetComponent<Image>().color = new Color(0.25f, 0.28f, 0.35f, 1f);
            barRailRt = rail.GetComponent<RectTransform>();
            barRailRt.anchorMin = barRailRt.anchorMax = new Vector2(0.5f, 0.5f);
            barRailRt.pivot = new Vector2(0.5f, 0.5f);
            barRailRt.sizeDelta = new Vector2(440f, 24f);
            barRailRt.anchoredPosition = new Vector2(0f, 10f);

            // 目标区
            var tz = new GameObject("TargetZone", typeof(RectTransform), typeof(Image));
            tz.transform.SetParent(barRailRt, false);
            targetZoneRt = tz.GetComponent<RectTransform>();
            tz.GetComponent<Image>().color = new Color(0.2f, 0.9f, 0.3f, 0.60f);
            targetZoneRt.anchorMin = targetZoneRt.anchorMax = new Vector2(0f, 0f);
            targetZoneRt.pivot = new Vector2(0.5f, 0.5f);
            targetZoneRt.sizeDelta = new Vector2(440f * TARGET_ZONE_RADIUS * 2f, 0f);

            // 完美区
            var pz = new GameObject("PerfectZone", typeof(RectTransform), typeof(Image));
            pz.transform.SetParent(targetZoneRt, false);
            pz.GetComponent<Image>().color = new Color(1f, 0.95f, 0.2f, 0.75f);
            var pzRt = pz.GetComponent<RectTransform>();
            pzRt.anchorMin = new Vector2(0.5f - PERFECT_RADIUS / TARGET_ZONE_RADIUS * 0.5f, 0f);
            pzRt.anchorMax = new Vector2(0.5f + PERFECT_RADIUS / TARGET_ZONE_RADIUS * 0.5f, 1f);
            pzRt.sizeDelta = Vector2.zero;

            // 指示器
            var ind = new GameObject("Indicator", typeof(RectTransform), typeof(Image));
            ind.transform.SetParent(barRailRt, false);
            indicatorRt = ind.GetComponent<RectTransform>();
            ind.GetComponent<Image>().color = Color.white;
            indicatorRt.anchorMin = indicatorRt.anchorMax = new Vector2(0f, 0f);
            indicatorRt.pivot = new Vector2(0.5f, 0.5f);
            indicatorRt.sizeDelta = new Vector2(6f, 0f);

            // 计时
            var tm = new GameObject("Timer", typeof(RectTransform), typeof(TextMeshProUGUI));
            tm.transform.SetParent(pnl.transform, false);
            timerLabel = tm.GetComponent<TextMeshProUGUI>();
            timerLabel.fontSize = 16;
            timerLabel.alignment = TextAlignmentOptions.Center;
            timerLabel.color = new Color(0.9f, 0.9f, 1f);
            var tmRt = tm.GetComponent<RectTransform>();
            tmRt.anchorMin = tmRt.anchorMax = new Vector2(0.5f, 0f);
            tmRt.pivot = new Vector2(0.5f, 0f);
            tmRt.anchoredPosition = new Vector2(0f, 16f);
            tmRt.sizeDelta = new Vector2(200f, 24f);

            // 操作提示
            var instr = new GameObject("Instruction", typeof(RectTransform), typeof(TextMeshProUGUI));
            instr.transform.SetParent(pnl.transform, false);
            instructionLabel = instr.GetComponent<TextMeshProUGUI>();
            instructionLabel.text = "按 [Space] 在目标区域内停止";
            instructionLabel.fontSize = 15;
            instructionLabel.alignment = TextAlignmentOptions.Center;
            instructionLabel.color = new Color(0.7f, 0.75f, 0.85f);
            var iRt = instr.GetComponent<RectTransform>();
            iRt.anchorMin = iRt.anchorMax = new Vector2(0.5f, 1f);
            iRt.pivot = new Vector2(0.5f, 1f);
            iRt.anchoredPosition = new Vector2(0f, -52f);
            iRt.sizeDelta = new Vector2(400f, 22f);

            // 结果
            var res = new GameObject("Result", typeof(RectTransform), typeof(TextMeshProUGUI));
            res.transform.SetParent(pnl.transform, false);
            resultLabel = res.GetComponent<TextMeshProUGUI>();
            resultLabel.fontSize = 42; resultLabel.alignment = TextAlignmentOptions.Center;
            resultLabel.gameObject.SetActive(false);
            var rRt = res.GetComponent<RectTransform>();
            rRt.anchorMin = rRt.anchorMax = new Vector2(0.5f, 0.5f);
            rRt.pivot = new Vector2(0.5f, 0.5f);
            rRt.anchoredPosition = new Vector2(0f, -60f);
            rRt.sizeDelta = new Vector2(400f, 50f);

            // 字体
            try
            {
                var all = Resources.FindObjectsOfTypeAll<TMP_Text>();
                if (all != null && all.Length > 0)
                    foreach (var tmp in new[] { tTmp, timerLabel, instructionLabel, resultLabel })
                    { tmp.font = all[0].font; tmp.fontSharedMaterial = all[0].fontSharedMaterial; }
            }
            catch { }

            canvasReady = true;
        }

        private static void UpdateQTEDisplay()
        {
            if (!canvasReady || qteRoot == null) return;
            if (indicatorRt != null)
                indicatorRt.anchoredPosition = new Vector2((barNorm - 0.5f) * (barRailRt?.rect.width ?? 440f), 0f);
            if (targetZoneRt != null)
                targetZoneRt.anchoredPosition = new Vector2((targetCenter - 0.5f) * (barRailRt?.rect.width ?? 440f), 0f);
            if (timerLabel != null)
            {
                timerLabel.text = string.Format("剩余: {0:F1}s", qteTimeLeft);
                timerLabel.color = qteTimeLeft <= 1f ? new Color(1f, 0.4f, 0.3f) : new Color(0.9f, 0.9f, 1f);
            }
        }

        private static void ShowResult(string rank)
        {
            if (resultLabel == null) return;
            resultLabel.gameObject.SetActive(true);
            if (timerLabel != null) timerLabel.gameObject.SetActive(false);
            switch (rank)
            {
                case "PERFECT": resultLabel.text = "完美！伤害 +50%"; resultLabel.color = new Color(1f, 0.95f, 0.2f); break;
                case "GREAT": resultLabel.text = "优秀！伤害 +30%"; resultLabel.color = new Color(0.2f, 0.95f, 0.5f); break;
                case "GOOD": resultLabel.text = "成功！伤害 +15%"; resultLabel.color = new Color(0.3f, 0.7f, 1f); break;
                default: resultLabel.text = "错过… 无加成"; resultLabel.color = new Color(0.7f, 0.7f, 0.7f); break;
            }
        }
    }

    public class CoroutineRunner : MonoBehaviour { }
}
