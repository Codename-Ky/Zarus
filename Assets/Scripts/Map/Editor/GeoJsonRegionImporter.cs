#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Zarus.Map;

namespace Zarus.Map.Editor
{
    public static class GeoJsonRegionImporter
    {
        private const string DefaultGeoJsonPath = "Assets/Sprites/za.json";
        private const string DefaultDatabasePath = "Assets/Resources/Map/RegionDatabase.asset";
        private const string DefaultMeshFolder = "Assets/Map/Meshes";

        static GeoJsonRegionImporter()
        {
            EditorApplication.delayCall += EnsureGeneratedAssetsExist;
        }

        [MenuItem("Zarus/Map/Rebuild Region Assets")]
        public static void ForceRebuild()
        {
            Import(true);
        }

        public static void Import(bool showDialog)
        {
            var geoJson = AssetDatabase.LoadAssetAtPath<TextAsset>(DefaultGeoJsonPath);
            if (geoJson == null)
            {
                Debug.LogWarning($"[GeoJsonRegionImporter] Missing GeoJSON file at {DefaultGeoJsonPath}");
                return;
            }

            Directory.CreateDirectory(DefaultMeshFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(DefaultDatabasePath) ?? "Assets/Resources/Map");

            var database = AssetDatabase.LoadAssetAtPath<RegionDatabase>(DefaultDatabasePath);
            if (database == null)
            {
                database = ScriptableObject.CreateInstance<RegionDatabase>();
                AssetDatabase.CreateAsset(database, DefaultDatabasePath);
            }

            // Keep artist-facing data before deleting old assets
            var existingMap = database.Regions?.ToDictionary(r => r.RegionId, r => r, StringComparer.OrdinalIgnoreCase)
                               ?? new Dictionary<string, RegionEntry>(StringComparer.OrdinalIgnoreCase);

            // Delete all existing mesh assets to ensure clean regeneration
            DeleteExistingMeshAssets();

            var geometries = RegionGeometryFactory.ParseGeoJson(geoJson.text, out var normalization);
            var entries = BuildEntries(geometries, normalization, existingMap);

            database.ReplaceEntries(entries);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (showDialog)
            {
                EditorUtility.DisplayDialog("Region Assets Updated", "South Africa region geometry has been rebuilt.", "Close");
            }
        }

        private static void DeleteExistingMeshAssets()
        {
            if (!Directory.Exists(DefaultMeshFolder))
            {
                return;
            }

            var meshFiles = Directory.GetFiles(DefaultMeshFolder, "*.asset");
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var meshFile in meshFiles)
                {
                    AssetDatabase.DeleteAsset(meshFile);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        private static void EnsureGeneratedAssetsExist()
        {
            EditorApplication.delayCall -= EnsureGeneratedAssetsExist;
            if (Application.isBatchMode)
            {
                return;
            }

            if (!File.Exists(DefaultDatabasePath))
            {
                Import(false);
            }
        }

        private static List<RegionEntry> BuildEntries(IReadOnlyList<RegionGeometry> geometries, RegionGeometryFactory.Normalization normalization, IReadOnlyDictionary<string, RegionEntry> artistData)
        {
            var results = new List<RegionEntry>(geometries.Count);
            
            // Use two-pass centered mesh creation with proper geographic centroids
            var (centeredMeshes, centroids) = RegionGeometryFactory.CreateCenteredMeshes(geometries, normalization);
            
            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < geometries.Count; i++)
                {
                    var geometry = geometries[i];
                    var mesh = centeredMeshes[i];
                    var centroid = centroids[i];
                    var entry = new RegionEntry();
                    var meshPath = Path.Combine(DefaultMeshFolder, $"{geometry.Id}_Mesh.asset").Replace('\\', '/');
                    
                    // Set mesh name before creating asset
                    mesh.name = $"{geometry.Id}_Mesh";
                    AssetDatabase.CreateAsset(mesh, meshPath);

                    // Use the true geographic centroid (properly offset) instead of mesh bounds center
                    entry.SetRuntimeData(geometry.Id, geometry.Name, mesh, centroid, mesh.bounds);
                    if (!string.IsNullOrEmpty(geometry.Id) && artistData != null && artistData.TryGetValue(geometry.Id, out var artistEntry))
                    {
                        entry.CopyArtistFacingData(artistEntry);
                    }

                    results.Add(entry);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            return results;
        }
    }
}
#endif
