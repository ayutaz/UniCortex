using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Unity.Collections;
using UniCortex;

namespace UniCortex.Samples
{
    /// <summary>
    /// メタデータフィルタ付き Dense 検索のデモ。
    /// Dense 検索 (K=20) で候補を取得し、メタデータで後フィルタリングする。
    /// </summary>
    public class FilterDemo : MonoBehaviour
    {
        UniCortexDatabase db;
        SampleItemInfo[] items;

        Dropdown presetDropdown;
        Text resultText;
        Text statusText;

        // Filter controls
        Toggle priceFilterToggle;
        Slider priceMinSlider;
        Slider priceMaxSlider;
        Text priceMinLabel;
        Text priceMaxLabel;

        Toggle rarityFilterToggle;
        Dropdown rarityDropdown;

        Toggle equipFilterToggle;
        Toggle equipValueToggle;

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

            var panel = UIHelper.CreateVerticalPanel(canvas.transform, 12, 6);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.05f, 0.02f);
            panelRect.anchorMax = new Vector2(0.95f, 0.98f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            // Title
            UIHelper.CreateLabel(panel, "Metadata Filter Demo", 26);
            UIHelper.CreateSeparator(panel);

            // Search section
            UIHelper.CreateLabel(panel, "Query Preset:", 16);
            presetDropdown = UIHelper.CreateDropdown(panel, PresetNames);

            var searchBtn = UIHelper.CreateButton(panel, "Search");
            searchBtn.onClick.AddListener(ExecuteSearch);

            UIHelper.CreateSeparator(panel);

            // Filter section
            UIHelper.CreateLabel(panel, "Filters:", 18);

            // Price filter
            priceFilterToggle = UIHelper.CreateToggle(panel, "Price Filter", false);
            var priceMinPair = UIHelper.CreateSliderWithLabel(panel, "Price Min:", 0, 10000, 0, true, "F0");
            priceMinSlider = priceMinPair.slider;
            priceMinLabel = priceMinPair.valueLabel;
            var priceMaxPair = UIHelper.CreateSliderWithLabel(panel, "Price Max:", 0, 10000, 10000, true, "F0");
            priceMaxSlider = priceMaxPair.slider;
            priceMaxLabel = priceMaxPair.valueLabel;

            // Rarity filter
            rarityFilterToggle = UIHelper.CreateToggle(panel, "Rarity Filter (>=)", false);
            rarityDropdown = UIHelper.CreateDropdown(panel, new string[] { "1", "2", "3", "4", "5" });

            // Equipable filter
            equipFilterToggle = UIHelper.CreateToggle(panel, "Equipable Filter", false);
            equipValueToggle = UIHelper.CreateToggle(panel, "Must be equipable", true);

            UIHelper.CreateSeparator(panel);

            // Result panel
            resultText = UIHelper.CreateScrollableResult(panel, 200);
            resultText.text = "Select a preset and press Search.";

            // Status bar
            statusText = UIHelper.CreateStatusBar(panel);
        }

        void ExecuteSearch()
        {
            int presetIndex = presetDropdown.value;
            bool priceEnabled = priceFilterToggle.isOn;
            int priceMin = (int)priceMinSlider.value;
            int priceMax = (int)priceMaxSlider.value;
            bool rarityEnabled = rarityFilterToggle.isOn;
            int minRarity = rarityDropdown.value + 1; // dropdown index 0="1", 1="2", etc.
            bool equipEnabled = equipFilterToggle.isOn;
            bool equipRequired = equipValueToggle.isOn;

            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            var query = SampleData.MakeQueryVector(Allocator.TempJob, PresetVectors[presetIndex]);
            var param = new SearchParams
            {
                K = 20,
                EfSearch = 100,
                DistanceType = DistanceType.EuclideanSq
            };

            var results = db.SearchDense(query, param);
            query.Dispose();

            // Post-filter using metadata
            var filtered = new List<SearchResult>();
            for (int i = 0; i < results.Length; i++)
            {
                var r = results[i];
                var extResult = db.GetExternalId(r.InternalId);
                if (!extResult.IsSuccess) continue;
                ulong docId = extResult.Value;

                bool pass = true;
                if (priceEnabled)
                {
                    var priceResult = db.GetMetadataInt(docId, SampleData.FieldPrice);
                    if (priceResult.IsSuccess)
                        pass &= priceResult.Value >= priceMin && priceResult.Value <= priceMax;
                    else
                        pass = false;
                }
                if (rarityEnabled && pass)
                {
                    var rarityResult = db.GetMetadataInt(docId, SampleData.FieldRarity);
                    if (rarityResult.IsSuccess)
                        pass &= rarityResult.Value >= minRarity;
                    else
                        pass = false;
                }
                if (equipEnabled && pass)
                {
                    var equipResult = db.GetMetadataBool(docId, SampleData.FieldIsEquipable);
                    if (equipResult.IsSuccess)
                        pass &= equipResult.Value == equipRequired;
                    else
                        pass = false;
                }
                if (pass) filtered.Add(r);
            }

            sw.Stop();

            // Format results
            var sb = new StringBuilder();
            sb.AppendLine($"Query: {PresetNames[presetIndex]}");
            sb.AppendLine($"Vector: [{string.Join(", ", PresetVectors[presetIndex])}]");
            sb.AppendLine();

            // Show active filters
            if (priceEnabled || rarityEnabled || equipEnabled)
            {
                sb.Append("Active Filters: ");
                if (priceEnabled) sb.Append($"Price[{priceMin}-{priceMax}] ");
                if (rarityEnabled) sb.Append($"Rarity>={minRarity} ");
                if (equipEnabled) sb.Append($"Equipable={equipRequired} ");
                sb.AppendLine();
            }
            sb.AppendLine();

            if (filtered.Count == 0)
            {
                sb.AppendLine("No results passed the filter.");
            }
            else
            {
                for (int i = 0; i < filtered.Count; i++)
                {
                    sb.AppendLine($"[{i + 1}] {SampleData.FormatResultDetailed(filtered[i], db, items)}");
                    sb.AppendLine();
                }
            }

            resultText.text = sb.ToString();
            statusText.text = $"{results.Length} results found, {filtered.Count} passed filter ({sw.Elapsed.TotalMilliseconds:F2}ms)";

            results.Dispose();
        }
    }
}
