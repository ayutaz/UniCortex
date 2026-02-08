using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UniCortex;
using UniCortex.Sparse;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace UniCortex.Samples
{
    /// <summary>
    /// Sparse ベクトル検索のデモ。
    /// キーワード + 重みペアからクエリを構築して検索する。
    /// </summary>
    public class SparseSearchDemo : MonoBehaviour
    {
        UniCortexDatabase db;
        SampleItemInfo[] items;

        // UI References
        Dropdown[] keywordDropdowns = new Dropdown[3];
        Slider[] weightSliders = new Slider[3];
        Text[] weightLabels = new Text[3];
        Slider kSlider;
        Text kLabel;
        Text resultText;
        Text statusText;

        void Start()
        {
            db = SampleData.CreateAndPopulateDatabase();
            items = SampleData.GetItemInfos();
            BuildUI();
            statusText.text = $"Database ready: {SampleData.ItemCount} items loaded. Select keywords and weights, then press Search.";
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
            var root = UIHelper.CreateVerticalPanel(canvas.transform, 15, 8);
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0.05f, 0.02f);
            rootRect.anchorMax = new Vector2(0.95f, 0.98f);
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            UIHelper.CreateTitle(root, "Sparse Vector Search");
            UIHelper.CreateSeparator(root);

            // Keyword + Weight pairs
            UIHelper.CreateLabel(root, "Query Keywords (select keyword + weight):", 16);

            for (int i = 0; i < 3; i++)
            {
                var row = UIHelper.CreateHorizontalPanel(root, 6, 36);

                var lbl = UIHelper.CreateLabel(row, $"Keyword {i + 1}:", 16);
                var lblLe = lbl.GetComponent<LayoutElement>();
                lblLe.preferredWidth = 90;
                lblLe.flexibleWidth = 0;

                keywordDropdowns[i] = UIHelper.CreateDropdown(row, SampleData.SparseLabels, 36);
                var ddLe = keywordDropdowns[i].GetComponent<LayoutElement>();
                ddLe.preferredWidth = 150;
                ddLe.flexibleWidth = 0;

                var wLbl = UIHelper.CreateLabel(row, "Weight:", 16);
                var wLblLe = wLbl.GetComponent<LayoutElement>();
                wLblLe.preferredWidth = 60;
                wLblLe.flexibleWidth = 0;

                weightSliders[i] = UIHelper.CreateSlider(row, 0f, 1f, 0.5f);
                var sLe = weightSliders[i].GetComponent<LayoutElement>();
                sLe.flexibleWidth = 1;

                int idx = i;
                weightLabels[i] = UIHelper.CreateLabel(row, "0.50", 16);
                weightLabels[i].alignment = TextAnchor.MiddleRight;
                var valLe = weightLabels[i].GetComponent<LayoutElement>();
                valLe.preferredWidth = 45;
                valLe.flexibleWidth = 0;

                weightSliders[i].onValueChanged.AddListener(v => { weightLabels[idx].text = v.ToString("F2"); });
            }

            // Set initial dropdown defaults to different keywords
            keywordDropdowns[0].value = SampleData.SparseFire;
            keywordDropdowns[1].value = SparseSlashIndex();
            keywordDropdowns[2].value = SampleData.SparseMagic;

            UIHelper.CreateSeparator(root);

            // Preset buttons
            UIHelper.CreateLabel(root, "Presets:", 16);
            var presetRow = UIHelper.CreateHorizontalPanel(root, 8, 40);

            var fireWeaponsBtn = UIHelper.CreateButton(presetRow, "Fire Weapons", 40);
            var fireWeaponsLe = fireWeaponsBtn.GetComponent<LayoutElement>();
            fireWeaponsLe.flexibleWidth = 1;
            fireWeaponsBtn.onClick.AddListener(PresetFireWeapons);

            var magicalBtn = UIHelper.CreateButton(presetRow, "Magical Items", 40);
            var magicalLe = magicalBtn.GetComponent<LayoutElement>();
            magicalLe.flexibleWidth = 1;
            magicalBtn.onClick.AddListener(PresetMagicalItems);

            var defenseBtn = UIHelper.CreateButton(presetRow, "Defensive Gear", 40);
            var defenseLe = defenseBtn.GetComponent<LayoutElement>();
            defenseLe.flexibleWidth = 1;
            defenseBtn.onClick.AddListener(PresetDefensiveGear);

            UIHelper.CreateSeparator(root);

            // K slider
            var kPair = UIHelper.CreateSliderWithLabel(root, "K (results):", 1, 20, 5, true, "F0");
            kSlider = kPair.slider;
            kLabel = kPair.valueLabel;

            // Search button
            var searchBtn = UIHelper.CreateButton(root, "Search", 45);
            searchBtn.onClick.AddListener(ExecuteSearch);

            UIHelper.CreateSeparator(root);

            // Results
            UIHelper.CreateLabel(root, "Results:", 16);
            resultText = UIHelper.CreateScrollableResult(root, 250);
            resultText.text = "Press Search or select a preset to see results.";

            // Status bar
            statusText = UIHelper.CreateStatusBar(root);
        }

        int SparseSlashIndex()
        {
            return SampleData.SparseSlash;
        }

        void PresetFireWeapons()
        {
            keywordDropdowns[0].value = SampleData.SparseFire;
            weightSliders[0].value = 0.9f;
            keywordDropdowns[1].value = SampleData.SparseSlash;
            weightSliders[1].value = 0.7f;
            // Zero out third slot
            weightSliders[2].value = 0f;
            ExecuteSearch();
        }

        void PresetMagicalItems()
        {
            keywordDropdowns[0].value = SampleData.SparseMagic;
            weightSliders[0].value = 0.9f;
            keywordDropdowns[1].value = SampleData.SparseIce;
            weightSliders[1].value = 0.5f;
            weightSliders[2].value = 0f;
            ExecuteSearch();
        }

        void PresetDefensiveGear()
        {
            keywordDropdowns[0].value = SampleData.SparseDefense;
            weightSliders[0].value = 0.9f;
            keywordDropdowns[1].value = SampleData.SparseHoly;
            weightSliders[1].value = 0.5f;
            weightSliders[2].value = 0f;
            ExecuteSearch();
        }

        void ExecuteSearch()
        {
            int k = (int)kSlider.value;

            // Build sparse query elements from UI
            var elements = new List<(int index, float value)>();
            for (int i = 0; i < 3; i++)
            {
                float w = weightSliders[i].value;
                if (w > 0.001f)
                {
                    int dimIndex = keywordDropdowns[i].value;
                    elements.Add((dimIndex, w));
                }
            }

            if (elements.Count == 0)
            {
                statusText.text = "Error: No keywords with non-zero weight selected.";
                resultText.text = "Please select at least one keyword with weight > 0.";
                return;
            }

            // Build query description
            var queryDesc = new StringBuilder();
            queryDesc.Append("Query: ");
            for (int i = 0; i < elements.Count; i++)
            {
                if (i > 0) queryDesc.Append(" + ");
                queryDesc.Append($"{SampleData.SparseLabels[elements[i].index]}={elements[i].value:F2}");
            }

            var sw = Stopwatch.StartNew();
            var query = SampleData.MakeSparseQuery(Allocator.TempJob, elements.ToArray());
            var results = db.SearchSparse(query, k);
            sw.Stop();
            query.Dispose();

            var sb = new StringBuilder();
            sb.AppendLine(queryDesc.ToString());
            sb.AppendLine($"Found {results.Length} results (K={k})");
            sb.AppendLine();

            for (int i = 0; i < results.Length; i++)
            {
                sb.AppendLine($"[{i + 1}] {SampleData.FormatResultDetailed(results[i], db, items)}");
                sb.AppendLine();
            }

            resultText.text = sb.ToString();
            int resultCount = results.Length;
            results.Dispose();

            statusText.text = $"Sparse search completed in {sw.Elapsed.TotalMilliseconds:F2}ms | {elements.Count} keywords, K={k}, {resultCount} results";
        }
    }
}
