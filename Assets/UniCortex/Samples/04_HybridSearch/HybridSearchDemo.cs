using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UniCortex.Hybrid;
using UniCortex.Sparse;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace UniCortex.Samples
{
    /// <summary>
    /// ハイブリッド検索 (RRF) のデモ。
    /// Dense + Sparse + BM25 を任意に組み合わせて RRF で統合検索する。
    /// </summary>
    public class HybridSearchDemo : MonoBehaviour
    {
        UniCortexDatabase db;
        SampleItemInfo[] items;

        // Dense section
        Toggle denseToggle;
        Dropdown densePresetDropdown;

        // Sparse section
        Toggle sparseToggle;
        Dropdown[] sparseDropdowns = new Dropdown[2];
        Slider[] sparseWeightSliders = new Slider[2];
        Text[] sparseWeightLabels = new Text[2];

        // BM25 section
        Toggle bm25Toggle;
        InputField bm25Input;

        // RRF weight sliders
        Slider denseWeightSlider;
        Slider sparseWeightSlider;
        Slider bm25WeightSlider;

        // K sliders
        Slider kSlider;
        Slider subKSlider;

        // Output
        Text resultText;
        Text statusText;

        // Dense presets (same 5 as DenseSearch)
        static readonly string[] DensePresetNames = new string[]
        {
            "Fire Magic",
            "Ice Power",
            "Holy Healing",
            "Dark Agility",
            "Balanced Defense"
        };

        static readonly float[][] DensePresetVectors = new float[][]
        {
            new float[] { 0.9f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f }, // Fire Magic
            new float[] { 0.0f, 0.9f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f }, // Ice Power
            new float[] { 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.8f, 0.0f, 0.9f }, // Holy Healing
            new float[] { 0.0f, 0.0f, 0.0f, 0.0f, 0.8f, 0.0f, 0.9f, 0.0f }, // Dark Agility
            new float[] { 0.2f, 0.2f, 0.2f, 0.8f, 0.2f, 0.2f, 0.0f, 0.2f }, // Balanced Defense
        };

        void Start()
        {
            db = SampleData.CreateAndPopulateDatabase();
            items = SampleData.GetItemInfos();
            BuildUI();
            statusText.text = $"Database ready: {SampleData.ItemCount} items loaded. Configure search modes and press Search.";
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
            var root = UIHelper.CreateVerticalPanel(canvas.transform, 12, 5);
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0.03f, 0.02f);
            rootRect.anchorMax = new Vector2(0.97f, 0.98f);
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            UIHelper.CreateTitle(root, "Hybrid Search (RRF)");
            UIHelper.CreateSeparator(root);

            // --- Dense Section ---
            UIHelper.CreateLabel(root, "Dense Vector Search", 18);
            var denseRow = UIHelper.CreateHorizontalPanel(root, 6, 36);
            denseToggle = UIHelper.CreateToggle(denseRow, "Enable Dense", true, 36);
            var denseToggleLe = denseToggle.GetComponent<LayoutElement>();
            denseToggleLe.preferredWidth = 160;
            denseToggleLe.flexibleWidth = 0;

            var presetLbl = UIHelper.CreateLabel(denseRow, "Preset:", 16);
            var presetLblLe = presetLbl.GetComponent<LayoutElement>();
            presetLblLe.preferredWidth = 55;
            presetLblLe.flexibleWidth = 0;

            densePresetDropdown = UIHelper.CreateDropdown(denseRow, DensePresetNames, 36);
            var ddLe = densePresetDropdown.GetComponent<LayoutElement>();
            ddLe.flexibleWidth = 1;

            UIHelper.CreateSeparator(root);

            // --- Sparse Section ---
            UIHelper.CreateLabel(root, "Sparse Vector Search", 18);
            var sparseToggleRow = UIHelper.CreateHorizontalPanel(root, 6, 36);
            sparseToggle = UIHelper.CreateToggle(sparseToggleRow, "Enable Sparse", true, 36);
            var sparseToggleLe = sparseToggle.GetComponent<LayoutElement>();
            sparseToggleLe.flexibleWidth = 1;

            for (int i = 0; i < 2; i++)
            {
                var row = UIHelper.CreateHorizontalPanel(root, 6, 36);

                var lbl = UIHelper.CreateLabel(row, $"Keyword {i + 1}:", 16);
                var lblLe = lbl.GetComponent<LayoutElement>();
                lblLe.preferredWidth = 90;
                lblLe.flexibleWidth = 0;

                sparseDropdowns[i] = UIHelper.CreateDropdown(row, SampleData.SparseLabels, 36);
                var sDdLe = sparseDropdowns[i].GetComponent<LayoutElement>();
                sDdLe.preferredWidth = 140;
                sDdLe.flexibleWidth = 0;

                var wLbl = UIHelper.CreateLabel(row, "Weight:", 16);
                var wLblLe = wLbl.GetComponent<LayoutElement>();
                wLblLe.preferredWidth = 55;
                wLblLe.flexibleWidth = 0;

                sparseWeightSliders[i] = UIHelper.CreateSlider(row, 0f, 1f, 0.5f);
                var sLe = sparseWeightSliders[i].GetComponent<LayoutElement>();
                sLe.flexibleWidth = 1;

                int idx = i;
                sparseWeightLabels[i] = UIHelper.CreateLabel(row, "0.50", 16);
                sparseWeightLabels[i].alignment = TextAnchor.MiddleRight;
                var valLe = sparseWeightLabels[i].GetComponent<LayoutElement>();
                valLe.preferredWidth = 45;
                valLe.flexibleWidth = 0;

                sparseWeightSliders[i].onValueChanged.AddListener(v => { sparseWeightLabels[idx].text = v.ToString("F2"); });
            }

            // Default sparse dropdowns
            sparseDropdowns[0].value = SampleData.SparseFire;
            sparseDropdowns[1].value = SampleData.SparseSlash;

            UIHelper.CreateSeparator(root);

            // --- BM25 Section ---
            UIHelper.CreateLabel(root, "BM25 Full-Text Search", 18);
            var bm25Row = UIHelper.CreateHorizontalPanel(root, 6, 36);
            bm25Toggle = UIHelper.CreateToggle(bm25Row, "Enable BM25", true, 36);
            var bm25ToggleLe = bm25Toggle.GetComponent<LayoutElement>();
            bm25ToggleLe.preferredWidth = 160;
            bm25ToggleLe.flexibleWidth = 0;

            bm25Input = UIHelper.CreateInputField(bm25Row, "Enter search text (e.g., fire sword)", 36);
            var inputLe = bm25Input.GetComponent<LayoutElement>();
            inputLe.flexibleWidth = 1;
            bm25Input.text = "fire sword";

            UIHelper.CreateSeparator(root);

            // --- RRF Weights ---
            UIHelper.CreateLabel(root, "RRF Fusion Weights:", 18);
            var dwPair = UIHelper.CreateSliderWithLabel(root, "Dense Weight:", 0f, 3f, 1f, false, "F1");
            denseWeightSlider = dwPair.slider;

            var swPair = UIHelper.CreateSliderWithLabel(root, "Sparse Weight:", 0f, 3f, 1f, false, "F1");
            sparseWeightSlider = swPair.slider;

            var bwPair = UIHelper.CreateSliderWithLabel(root, "BM25 Weight:", 0f, 3f, 1f, false, "F1");
            bm25WeightSlider = bwPair.slider;

            UIHelper.CreateSeparator(root);

            // --- K sliders ---
            var kRow = UIHelper.CreateHorizontalPanel(root, 20, 30);
            var kLbl = UIHelper.CreateLabel(kRow, "K:", 16);
            var kLblLe = kLbl.GetComponent<LayoutElement>();
            kLblLe.preferredWidth = 20;
            kLblLe.flexibleWidth = 0;

            kSlider = UIHelper.CreateSlider(kRow, 1, 10, 5, true);
            var kSLe = kSlider.GetComponent<LayoutElement>();
            kSLe.flexibleWidth = 1;

            var kValLbl = UIHelper.CreateLabel(kRow, "5", 16);
            kValLbl.alignment = TextAnchor.MiddleRight;
            var kValLe = kValLbl.GetComponent<LayoutElement>();
            kValLe.preferredWidth = 30;
            kValLe.flexibleWidth = 0;
            kSlider.onValueChanged.AddListener(v => { kValLbl.text = ((int)v).ToString(); });

            var subKRow = UIHelper.CreateHorizontalPanel(root, 20, 30);
            var subKLbl = UIHelper.CreateLabel(subKRow, "SubSearchK:", 16);
            var subKLblLe = subKLbl.GetComponent<LayoutElement>();
            subKLblLe.preferredWidth = 100;
            subKLblLe.flexibleWidth = 0;

            subKSlider = UIHelper.CreateSlider(subKRow, 5, 20, 10, true);
            var subKSLe = subKSlider.GetComponent<LayoutElement>();
            subKSLe.flexibleWidth = 1;

            var subKValLbl = UIHelper.CreateLabel(subKRow, "10", 16);
            subKValLbl.alignment = TextAnchor.MiddleRight;
            var subKValLe = subKValLbl.GetComponent<LayoutElement>();
            subKValLe.preferredWidth = 30;
            subKValLe.flexibleWidth = 0;
            subKSlider.onValueChanged.AddListener(v => { subKValLbl.text = ((int)v).ToString(); });

            // Search button
            var searchBtn = UIHelper.CreateButton(root, "Search", 45);
            searchBtn.onClick.AddListener(ExecuteSearch);

            UIHelper.CreateSeparator(root);

            // Results
            UIHelper.CreateLabel(root, "Results:", 16);
            resultText = UIHelper.CreateScrollableResult(root, 200);
            resultText.text = "Configure search modes and press Search to see hybrid results.";

            // Status bar
            statusText = UIHelper.CreateStatusBar(root);
        }

        void ExecuteSearch()
        {
            bool denseEnabled = denseToggle.isOn;
            bool sparseEnabled = sparseToggle.isOn;
            bool bm25Enabled = bm25Toggle.isOn;

            if (!denseEnabled && !sparseEnabled && !bm25Enabled)
            {
                statusText.text = "Error: At least one search mode must be enabled.";
                resultText.text = "Please enable at least one of Dense, Sparse, or BM25.";
                return;
            }

            int k = (int)kSlider.value;
            int subK = (int)subKSlider.value;
            float denseWeight = denseWeightSlider.value;
            float sparseWeight = sparseWeightSlider.value;
            float bm25Weight = bm25WeightSlider.value;

            // Build query description
            var queryDesc = new StringBuilder();
            queryDesc.Append("Query: ");
            var enabledModes = new List<string>();

            var hybridParams = new HybridSearchParams
            {
                K = k,
                SubSearchK = subK,
                RrfConfig = new RrfConfig
                {
                    RankConstant = 60f,
                    DenseWeight = denseWeight,
                    SparseWeight = sparseWeight,
                    Bm25Weight = bm25Weight,
                },
                DenseParams = new SearchParams { K = subK, EfSearch = 50, DistanceType = DistanceType.EuclideanSq },
            };

            NativeArray<float> denseQuery = default;
            NativeArray<SparseElement> sparseQuery = default;
            NativeArray<byte> textQuery = default;

            // Dense query
            if (denseEnabled)
            {
                int presetIdx = densePresetDropdown.value;
                var vec = DensePresetVectors[presetIdx];
                denseQuery = SampleData.MakeQueryVector(Allocator.TempJob, vec);
                hybridParams.DenseQuery = denseQuery;
                enabledModes.Add($"Dense({DensePresetNames[presetIdx]})");
            }

            // Sparse query
            if (sparseEnabled)
            {
                var elements = new List<(int index, float value)>();
                for (int i = 0; i < 2; i++)
                {
                    float w = sparseWeightSliders[i].value;
                    if (w > 0.001f)
                    {
                        elements.Add((sparseDropdowns[i].value, w));
                    }
                }

                if (elements.Count > 0)
                {
                    sparseQuery = SampleData.MakeSparseQuery(Allocator.TempJob, elements.ToArray());
                    hybridParams.SparseQuery = sparseQuery;

                    var sparseDesc = new StringBuilder("Sparse(");
                    for (int i = 0; i < elements.Count; i++)
                    {
                        if (i > 0) sparseDesc.Append("+");
                        sparseDesc.Append($"{SampleData.SparseLabels[elements[i].index]}={elements[i].value:F1}");
                    }
                    sparseDesc.Append(")");
                    enabledModes.Add(sparseDesc.ToString());
                }
                else
                {
                    enabledModes.Add("Sparse(no keywords)");
                }
            }

            // BM25 query
            if (bm25Enabled)
            {
                string searchText = bm25Input.text;
                if (!string.IsNullOrEmpty(searchText))
                {
                    textQuery = SampleData.MakeTextQuery(searchText, Allocator.TempJob);
                    hybridParams.TextQuery = textQuery;
                    enabledModes.Add($"BM25(\"{searchText}\")");
                }
                else
                {
                    enabledModes.Add("BM25(empty)");
                }
            }

            queryDesc.Append(string.Join(" + ", enabledModes));

            var sw = Stopwatch.StartNew();
            var resultWrapped = db.SearchHybrid(hybridParams);
            sw.Stop();

            var sb = new StringBuilder();
            sb.AppendLine(queryDesc.ToString());
            sb.AppendLine($"RRF Weights: Dense={denseWeight:F1}, Sparse={sparseWeight:F1}, BM25={bm25Weight:F1}");
            sb.AppendLine($"K={k}, SubSearchK={subK}");
            sb.AppendLine();

            int resultCount = 0;
            if (resultWrapped.IsSuccess)
            {
                var results = resultWrapped.Value;
                resultCount = results.Length;
                sb.AppendLine($"Found {results.Length} results:");
                sb.AppendLine();

                for (int i = 0; i < results.Length; i++)
                {
                    sb.AppendLine($"[{i + 1}] {SampleData.FormatResultDetailed(results[i], db, items)}");
                    sb.AppendLine();
                }

                results.Dispose();
            }
            else
            {
                sb.AppendLine($"Search failed: {resultWrapped.Error}");
            }

            resultText.text = sb.ToString();

            // Dispose queries
            if (denseQuery.IsCreated) denseQuery.Dispose();
            if (sparseQuery.IsCreated) sparseQuery.Dispose();
            if (textQuery.IsCreated) textQuery.Dispose();

            statusText.text = $"Hybrid search completed in {sw.Elapsed.TotalMilliseconds:F2}ms | Modes: {string.Join("+", enabledModes)} | K={k}, {resultCount} results";
        }
    }
}
