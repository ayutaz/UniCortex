using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Unity.Collections;

namespace UniCortex.Samples
{
    /// <summary>
    /// HNSW Dense ベクトル検索のデモ。
    /// 20件の RPG アイテムに対してプリセットクエリで検索を行う。
    /// </summary>
    public class DenseSearchDemo : MonoBehaviour
    {
        UniCortexDatabase db;
        SampleItemInfo[] items;

        Dropdown presetDropdown;
        Dropdown distanceDropdown;
        Slider kSlider;
        Slider efSearchSlider;
        Text resultText;
        Text statusText;

        // Preset query vectors: [Fire, Ice, Earth/Thunder, Defense, Agility, Healing, Dark, Holy]
        static readonly string[] PresetNames = new string[]
        {
            "Fire Weapons",
            "Ice Magic",
            "Defensive",
            "Healing",
            "Dark + Agility"
        };

        static readonly float[][] PresetVectors = new float[][]
        {
            new float[] { 0.9f, 0f, 0f, 0.1f, 0.2f, 0f, 0f, 0f },
            new float[] { 0f, 0.9f, 0f, 0f, 0.1f, 0f, 0f, 0f },
            new float[] { 0f, 0f, 0f, 0.9f, 0f, 0f, 0f, 0f },
            new float[] { 0f, 0f, 0f, 0f, 0f, 0.9f, 0f, 0f },
            new float[] { 0f, 0f, 0f, 0f, 0.7f, 0f, 0.9f, 0f },
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
            UIHelper.CreateLabel(panel, "Dense Vector Search (HNSW)", 26);
            UIHelper.CreateSeparator(panel);

            // Preset Dropdown
            UIHelper.CreateLabel(panel, "Query Preset:", 16);
            presetDropdown = UIHelper.CreateDropdown(panel, PresetNames);

            // Distance Type Dropdown
            UIHelper.CreateLabel(panel, "Distance Type:", 16);
            distanceDropdown = UIHelper.CreateDropdown(panel, new string[] { "EuclideanSq", "Cosine", "DotProduct" });

            // K slider
            var kPair = UIHelper.CreateSliderWithLabel(panel, "K (results):", 1, 20, 5, true, "F0");
            kSlider = kPair.slider;

            // EfSearch slider
            var efPair = UIHelper.CreateSliderWithLabel(panel, "EfSearch:", 10, 200, 50, true, "F0");
            efSearchSlider = efPair.slider;

            // Search button
            var searchBtn = UIHelper.CreateButton(panel, "Search");
            searchBtn.onClick.AddListener(ExecuteSearch);

            UIHelper.CreateSeparator(panel);

            // Result panel
            resultText = UIHelper.CreateScrollableResult(panel, 300);
            resultText.text = "Select a preset and press Search.";

            // Status bar
            statusText = UIHelper.CreateStatusBar(panel);
        }

        void ExecuteSearch()
        {
            int presetIndex = presetDropdown.value;
            int k = (int)kSlider.value;
            int efSearch = (int)efSearchSlider.value;

            DistanceType distType;
            switch (distanceDropdown.value)
            {
                case 1:  distType = DistanceType.Cosine; break;
                case 2:  distType = DistanceType.DotProduct; break;
                default: distType = DistanceType.EuclideanSq; break;
            }

            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            var query = SampleData.MakeQueryVector(Allocator.TempJob, PresetVectors[presetIndex]);
            var param = new SearchParams
            {
                K = k,
                EfSearch = efSearch,
                DistanceType = distType
            };

            var results = db.SearchDense(query, param);
            sw.Stop();

            query.Dispose();

            // Format results
            var sb = new StringBuilder();
            sb.AppendLine($"Query: {PresetNames[presetIndex]}");
            sb.AppendLine($"Vector: [{string.Join(", ", PresetVectors[presetIndex])}]");
            sb.AppendLine($"Distance: {distType}, K={k}, EfSearch={efSearch}");
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
