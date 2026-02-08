using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Unity.Collections;

namespace UniCortex.Samples
{
    /// <summary>
    /// BM25 全文検索のデモ。
    /// 20件の RPG アイテムの Description テキストに対して BM25 検索を行う。
    /// </summary>
    public class BM25SearchDemo : MonoBehaviour
    {
        UniCortexDatabase db;
        SampleItemInfo[] items;

        InputField searchInput;
        Slider kSlider;
        Text resultText;
        Text statusText;

        static readonly string[] PresetQueries = new string[]
        {
            "fire",
            "dragon magic",
            "potion restore",
            "ice frost",
            "holy undead"
        };

        void Start()
        {
            db = SampleData.CreateAndPopulateDatabase();
            items = SampleData.GetItemInfos();
            BuildUI();
        }

        void OnDestroy()
        {
            if (db != null)
            {
                db.Dispose();
                db = null;
            }
        }

        void BuildUI()
        {
            var canvas = UIHelper.CreateCanvas();

            var panel = UIHelper.CreateVerticalPanel(canvas.transform, 12, 8);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.05f, 0.02f);
            panelRect.anchorMax = new Vector2(0.95f, 0.98f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            // Title
            UIHelper.CreateLabel(panel, "BM25 Full-Text Search", 26);
            UIHelper.CreateSeparator(panel);

            // Text input
            UIHelper.CreateLabel(panel, "Search Text:", 16);
            searchInput = UIHelper.CreateInputField(panel, "e.g. fire sword, dragon magic...");

            // Preset buttons row
            UIHelper.CreateLabel(panel, "Presets:", 16);
            var presetRow = UIHelper.CreateHorizontalPanel(panel, 6, 36);
            for (int i = 0; i < PresetQueries.Length; i++)
            {
                string presetText = PresetQueries[i];
                var btn = UIHelper.CreateButton(presetRow, presetText, 36);
                var btnLe = btn.GetComponent<LayoutElement>();
                btnLe.flexibleWidth = 1;
                btn.onClick.AddListener(() =>
                {
                    searchInput.text = presetText;
                    ExecuteSearch();
                });
            }

            // K slider
            var kPair = UIHelper.CreateSliderWithLabel(panel, "K (results):", 1, 20, 5, true, "F0");
            kSlider = kPair.slider;

            // Search button
            var searchBtn = UIHelper.CreateButton(panel, "Search");
            searchBtn.onClick.AddListener(ExecuteSearch);

            UIHelper.CreateSeparator(panel);

            // Result panel
            resultText = UIHelper.CreateScrollableResult(panel, 300);
            resultText.text = "Enter search text or select a preset.";

            // Status bar
            statusText = UIHelper.CreateStatusBar(panel);
        }

        void ExecuteSearch()
        {
            string searchString = searchInput.text;
            if (string.IsNullOrEmpty(searchString))
            {
                resultText.text = "Please enter search text.";
                statusText.text = "No query provided.";
                return;
            }

            int k = (int)kSlider.value;

            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            var queryText = SampleData.MakeTextQuery(searchString, Allocator.TempJob);
            var results = db.SearchBM25(queryText, k);
            sw.Stop();

            queryText.Dispose();

            // Format results
            var sb = new StringBuilder();
            sb.AppendLine($"Query: \"{searchString}\"");
            sb.AppendLine($"K={k}");
            sb.AppendLine();

            if (results.Length == 0)
            {
                sb.AppendLine("No results found.");
            }
            else
            {
                for (int i = 0; i < results.Length; i++)
                {
                    sb.AppendLine($"[{i + 1}] {SampleData.FormatResultDetailed(results[i], db, items)}");
                    sb.AppendLine();
                }
            }

            resultText.text = sb.ToString();
            statusText.text = $"{results.Length} results in {sw.Elapsed.TotalMilliseconds:F2}ms";

            results.Dispose();
        }
    }
}
