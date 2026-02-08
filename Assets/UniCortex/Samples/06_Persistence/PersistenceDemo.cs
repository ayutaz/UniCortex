using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Unity.Collections;
using UniCortex.Persistence;

namespace UniCortex.Samples
{
    /// <summary>
    /// Save/Load ワークフローのステップバイステップ デモ。
    /// IndexSerializer を使ってデータベースの永続化と復元を行う。
    /// </summary>
    public class PersistenceDemo : MonoBehaviour
    {
        UniCortexDatabase db;
        SampleItemInfo[] items;

        // State flags
        bool hasDb;
        bool isBuilt;
        bool hasSavedFile;

        // UI
        Text statusPanel;
        Text logText;
        Button btnCreate;
        Button btnBuild;
        Button btnSearch;
        Button btnSave;
        Button btnDispose;
        Button btnLoad;
        Button btnSearchAgain;
        Button btnDelete;

        string filePath;
        StringBuilder logBuilder = new StringBuilder();

        void Start()
        {
            items = SampleData.GetItemInfos();
            filePath = System.IO.Path.Combine(Application.persistentDataPath, "unicortex_sample.ucx");
            BuildUI();
            UpdateButtonStates();
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
            UIHelper.CreateLabel(panel, "Persistence Demo (Save/Load)", 26);
            UIHelper.CreateSeparator(panel);

            // Status panel
            statusPanel = UIHelper.CreateLabel(panel, "", 16);

            UIHelper.CreateSeparator(panel);

            // Step buttons
            btnCreate = UIHelper.CreateButton(panel, "1. Create & Populate");
            btnCreate.onClick.AddListener(StepCreate);

            btnBuild = UIHelper.CreateButton(panel, "2. Build Index");
            btnBuild.onClick.AddListener(StepBuild);

            btnSearch = UIHelper.CreateButton(panel, "3. Search (Verify)");
            btnSearch.onClick.AddListener(StepSearch);

            btnSave = UIHelper.CreateButton(panel, "4. Save to File");
            btnSave.onClick.AddListener(StepSave);

            btnDispose = UIHelper.CreateButton(panel, "5. Dispose DB");
            btnDispose.onClick.AddListener(StepDispose);

            btnLoad = UIHelper.CreateButton(panel, "6. Load from File");
            btnLoad.onClick.AddListener(StepLoad);

            btnSearchAgain = UIHelper.CreateButton(panel, "7. Search Again (Verify)");
            btnSearchAgain.onClick.AddListener(StepSearchAgain);

            btnDelete = UIHelper.CreateButton(panel, "8. Delete File");
            btnDelete.onClick.AddListener(StepDeleteFile);

            UIHelper.CreateSeparator(panel);

            // Log panel
            logText = UIHelper.CreateScrollableResult(panel, 200);
            logText.text = "";
            AppendLog("Ready. Follow steps 1-8 in order.");
        }

        void UpdateButtonStates()
        {
            btnCreate.interactable = !hasDb;
            btnBuild.interactable = hasDb && !isBuilt;
            btnSearch.interactable = hasDb && isBuilt;
            btnSave.interactable = hasDb && isBuilt;
            btnDispose.interactable = hasDb;
            btnLoad.interactable = !hasDb && hasSavedFile;
            btnSearchAgain.interactable = hasDb && isBuilt;
            btnDelete.interactable = hasSavedFile;

            UpdateStatusPanel();
        }

        void UpdateStatusPanel()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Documents: {(hasDb ? db.DocumentCount.ToString() : "N/A")}");
            sb.AppendLine($"Built: {(hasDb ? isBuilt.ToString() : "N/A")}");
            sb.Append($"File: {filePath}");
            if (hasSavedFile) sb.Append(" [EXISTS]");
            statusPanel.text = sb.ToString();
        }

        void AppendLog(string message)
        {
            string timestamp = System.DateTime.Now.ToString("HH:mm:ss.fff");
            logBuilder.AppendLine($"[{timestamp}] {message}");
            logText.text = logBuilder.ToString();
        }

        void StepCreate()
        {
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            var config = new DatabaseConfig
            {
                Capacity = 100,
                Dimension = SampleData.VectorDimension,
                HnswConfig = HnswConfig.Default,
                DistanceType = DistanceType.EuclideanSq,
                BM25K1 = 1.2f,
                BM25B = 0.75f,
            };

            db = new UniCortexDatabase(config);

            for (int i = 0; i < SampleData.ItemCount; i++)
            {
                var item = items[i];

                var dense = SampleData.MakeQueryVector(Allocator.Temp,
                    GetDenseVector(i));

                var sparseData = GetSparseData(i);
                var sparse = SampleData.MakeSparseQuery(Allocator.Temp, sparseData);

                var text = SampleData.MakeTextQuery(item.Description, Allocator.Temp);

                db.Add(item.Id, dense, sparse, text);

                dense.Dispose();
                sparse.Dispose();
                text.Dispose();

                db.SetMetadataInt(item.Id, SampleData.FieldPrice, item.Price);
                db.SetMetadataInt(item.Id, SampleData.FieldRarity, item.Rarity);
                db.SetMetadataFloat(item.Id, SampleData.FieldWeight, item.Weight);
                db.SetMetadataBool(item.Id, SampleData.FieldIsEquipable, item.IsEquipable);
            }

            sw.Stop();
            hasDb = true;
            isBuilt = false;
            AppendLog($"Created database with {db.DocumentCount} documents ({sw.Elapsed.TotalMilliseconds:F2}ms)");
            UpdateButtonStates();
        }

        void StepBuild()
        {
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            db.Build();
            sw.Stop();
            isBuilt = true;
            AppendLog($"Index built successfully ({sw.Elapsed.TotalMilliseconds:F2}ms)");
            UpdateButtonStates();
        }

        void StepSearch()
        {
            DoSearch("Pre-save search");
        }

        void StepSave()
        {
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            var result = IndexSerializer.Save(filePath, db);
            sw.Stop();

            if (result.IsSuccess)
            {
                hasSavedFile = true;
                var fileInfo = new System.IO.FileInfo(filePath);
                AppendLog($"Saved to file ({fileInfo.Length} bytes, {sw.Elapsed.TotalMilliseconds:F2}ms)");
            }
            else
            {
                AppendLog($"Save FAILED: {result.Error}");
            }
            UpdateButtonStates();
        }

        void StepDispose()
        {
            db.Dispose();
            db = null;
            hasDb = false;
            isBuilt = false;
            AppendLog("Database disposed. All in-memory data released.");
            UpdateButtonStates();
        }

        void StepLoad()
        {
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            var result = IndexSerializer.Load(filePath);
            sw.Stop();

            if (result.IsSuccess)
            {
                db = result.Value;
                hasDb = true;
                isBuilt = true;
                AppendLog($"Loaded from file. Documents={db.DocumentCount} ({sw.Elapsed.TotalMilliseconds:F2}ms)");
            }
            else
            {
                AppendLog($"Load FAILED: {result.Error}");
            }
            UpdateButtonStates();
        }

        void StepSearchAgain()
        {
            DoSearch("Post-load search");
        }

        void StepDeleteFile()
        {
            try
            {
                System.IO.File.Delete(filePath);
                hasSavedFile = false;
                AppendLog("File deleted.");
            }
            catch (System.Exception ex)
            {
                AppendLog($"Delete failed: {ex.Message}");
            }
            UpdateButtonStates();
        }

        void DoSearch(string label)
        {
            // Search for "fire" items using the Fire Weapons preset
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            var query = SampleData.MakeQueryVector(Allocator.TempJob,
                0.9f, 0f, 0f, 0.1f, 0.2f, 0f, 0f, 0f);
            var param = new SearchParams
            {
                K = 5,
                EfSearch = 50,
                DistanceType = DistanceType.EuclideanSq
            };

            var results = db.SearchDense(query, param);
            sw.Stop();
            query.Dispose();

            var sb = new StringBuilder();
            sb.AppendLine($"{label} - \"Fire Weapons\" (K=5):");
            for (int i = 0; i < results.Length; i++)
            {
                sb.AppendLine($"  [{i + 1}] {SampleData.FormatResult(results[i], db, items)}");
            }
            AppendLog(sb.ToString().TrimEnd());

            results.Dispose();
            UpdateButtonStates();
        }

        // Dense vectors matching SampleData order
        static readonly float[][] DenseVectors = new float[][]
        {
            new float[] { 0.9f, 0.0f, 0.0f, 0.1f, 0.2f, 0.0f, 0.0f, 0.0f },
            new float[] { 0.0f, 0.9f, 0.0f, 0.0f, 0.1f, 0.0f, 0.0f, 0.0f },
            new float[] { 0.0f, 0.0f, 0.9f, 0.1f, 0.0f, 0.0f, 0.0f, 0.0f },
            new float[] { 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.9f, 0.0f, 0.0f },
            new float[] { 0.5f, 0.0f, 0.0f, 0.8f, 0.0f, 0.0f, 0.0f, 0.0f },
            new float[] { 0.0f, 0.0f, 0.0f, 0.0f, 0.7f, 0.0f, 0.9f, 0.0f },
            new float[] { 0.0f, 0.0f, 0.0f, 0.8f, 0.0f, 0.1f, 0.0f, 0.9f },
            new float[] { 0.1f, 0.1f, 0.1f, 0.0f, 0.0f, 0.1f, 0.1f, 0.1f },
            new float[] { 0.0f, 0.0f, 0.0f, 0.0f, 0.8f, 0.0f, 0.4f, 0.0f },
            new float[] { 0.0f, 0.8f, 0.0f, 0.1f, 0.0f, 0.0f, 0.0f, 0.0f },
            new float[] { 0.0f, 0.0f, 0.7f, 0.8f, 0.0f, 0.0f, 0.0f, 0.0f },
            new float[] { 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.8f, 0.0f, 0.1f },
            new float[] { 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.9f, 0.0f },
            new float[] { 0.0f, 0.0f, 0.0f, 0.0f, 0.9f, 0.0f, 0.0f, 0.0f },
            new float[] { 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.5f, 0.0f, 0.7f },
            new float[] { 0.8f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f },
            new float[] { 0.0f, 0.8f, 0.0f, 0.0f, 0.2f, 0.0f, 0.0f, 0.0f },
            new float[] { 0.6f, 0.0f, 0.0f, 0.4f, 0.0f, 0.0f, 0.0f, 0.0f },
            new float[] { 0.0f, 0.0f, 0.0f, 0.1f, 0.2f, 0.0f, 0.0f, 0.9f },
            new float[] { 0.0f, 0.0f, 0.1f, 0.0f, 0.0f, 0.7f, 0.0f, 0.2f },
        };

        static float[] GetDenseVector(int index)
        {
            return DenseVectors[index];
        }

        // Sparse vectors matching SampleData order
        static readonly (int, float)[][] SparseVectors = new (int, float)[][]
        {
            new (int, float)[] { (SampleData.SparseFire, 0.9f), (SampleData.SparseSlash, 0.7f) },
            new (int, float)[] { (SampleData.SparseIce, 0.9f), (SampleData.SparseMagic, 0.6f) },
            new (int, float)[] { (SampleData.SparseThunder, 0.9f), (SampleData.SparseCrush, 0.8f) },
            new (int, float)[] { (SampleData.SparseHealing, 0.9f) },
            new (int, float)[] { (SampleData.SparseFire, 0.4f), (SampleData.SparseDefense, 0.8f), (SampleData.SparseDragon, 0.7f) },
            new (int, float)[] { (SampleData.SparseDark, 0.8f), (SampleData.SparseSlash, 0.5f), (SampleData.SparseAgility, 0.6f) },
            new (int, float)[] { (SampleData.SparseHoly, 0.9f), (SampleData.SparseDefense, 0.8f) },
            new (int, float)[] { (SampleData.SparseMagic, 0.9f) },
            new (int, float)[] { (SampleData.SparsePoison, 0.8f), (SampleData.SparsePiercing, 0.7f), (SampleData.SparseAgility, 0.5f) },
            new (int, float)[] { (SampleData.SparseIce, 0.7f), (SampleData.SparseMagic, 0.5f) },
            new (int, float)[] { (SampleData.SparseThunder, 0.5f), (SampleData.SparseDefense, 0.9f) },
            new (int, float)[] { (SampleData.SparseHealing, 0.7f), (SampleData.SparseNature, 0.6f) },
            new (int, float)[] { (SampleData.SparseDark, 0.9f), (SampleData.SparseMagic, 0.8f) },
            new (int, float)[] { (SampleData.SparseAgility, 0.9f) },
            new (int, float)[] { (SampleData.SparseHoly, 0.7f), (SampleData.SparseHealing, 0.4f), (SampleData.SparseUndead, 0.6f) },
            new (int, float)[] { (SampleData.SparseFire, 0.8f), (SampleData.SparseMagic, 0.4f) },
            new (int, float)[] { (SampleData.SparseIce, 0.8f), (SampleData.SparsePiercing, 0.7f) },
            new (int, float)[] { (SampleData.SparseFire, 0.5f), (SampleData.SparseDragon, 0.8f), (SampleData.SparseDefense, 0.4f) },
            new (int, float)[] { (SampleData.SparseHoly, 0.8f), (SampleData.SparseSlash, 0.6f), (SampleData.SparseUndead, 0.7f) },
            new (int, float)[] { (SampleData.SparseNature, 0.8f), (SampleData.SparseHealing, 0.6f), (SampleData.SparseMagic, 0.5f) },
        };

        static (int index, float value)[] GetSparseData(int itemIndex)
        {
            var raw = SparseVectors[itemIndex];
            var result = new (int index, float value)[raw.Length];
            for (int i = 0; i < raw.Length; i++)
                result[i] = (raw[i].Item1, raw[i].Item2);
            return result;
        }
    }
}
