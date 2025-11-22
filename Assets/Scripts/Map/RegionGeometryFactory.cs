using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using LibTessDotNet;
using UnityEngine;

namespace Zarus.Map
{
    public static class RegionGeometryFactory
    {
        private const float MinNormalizationRange = 0.0001f;
        private const float MinPolygonArea = 0.0001f;

        public struct Normalization
        {
            public Vector2 Center;
            public float Range;
        }

        public static IReadOnlyList<RegionGeometry> ParseGeoJson(string json, out Normalization normalization)
        {
            var regions = new List<RegionGeometry>();
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;

            var doc = JObject.Parse(json);
            var features = doc["features"];
            if (features == null)
            {
                throw new InvalidOperationException("GeoJSON payload does not define any features.");
            }

            foreach (var feature in features)
            {
                var properties = feature["properties"];
                var geometry = feature["geometry"];
                var region = new RegionGeometry
                {
                    Id = properties?["id"]?.ToString() ?? string.Empty,
                    Name = properties?["name"]?.ToString() ?? string.Empty
                };

                if (string.IsNullOrEmpty(region.Id))
                {
                    region.Id = region.Name.Replace(" ", string.Empty).ToUpperInvariant();
                }

                var type = geometry?["type"]?.ToString();
                if (string.Equals(type, "Polygon", StringComparison.OrdinalIgnoreCase))
                {
                    region.Shapes.Add(ParsePolygon(geometry["coordinates"], ref minX, ref minY, ref maxX, ref maxY));
                }
                else if (string.Equals(type, "MultiPolygon", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var polygon in geometry["coordinates"])
                    {
                        region.Shapes.Add(ParsePolygon(polygon, ref minX, ref minY, ref maxX, ref maxY));
                    }
                }

                if (region.Shapes.Count > 0)
                {
                    regions.Add(region);
                }
            }

            if (double.IsInfinity(minX))
            {
                minX = minY = 0;
                maxX = maxY = 1;
            }

            var offset = new Vector2((float)((minX + maxX) * 0.5), (float)((minY + maxY) * 0.5));
            var rangeX = (float)(maxX - minX);
            var rangeY = (float)(maxY - minY);
            var largestRange = Mathf.Max(rangeX, rangeY);
            if (largestRange <= MinNormalizationRange)
            {
                largestRange = 1f;
            }

            normalization = new Normalization
            {
                Center = offset,
                Range = largestRange
            };

            return regions;
        }

        public static UnityEngine.Mesh CreateMesh(RegionGeometry region, Normalization normalization, string meshName = null, UnityEngine.Mesh reuseMesh = null)
        {
            return CreateMesh(region, normalization, Vector3.zero, meshName, reuseMesh);
        }

        public static UnityEngine.Mesh CreateMesh(RegionGeometry region, Normalization normalization, Vector3 visualOffset, string meshName = null, UnityEngine.Mesh reuseMesh = null)
        {
            if (region == null)
            {
                throw new ArgumentNullException(nameof(region));
            }

            var tess = new Tess();
            foreach (var shape in region.Shapes)
            {
                AddContour(tess, shape.Outer, false, normalization);
                foreach (var hole in shape.Holes)
                {
                    AddContour(tess, hole, true, normalization);
                }
            }

            tess.Tessellate(WindingRule.EvenOdd, ElementType.Polygons, 3);
            return BuildMeshFromTess(region, tess, visualOffset, meshName, reuseMesh);
        }

        /// <summary>
        /// Creates centered meshes using a two-pass approach:
        /// 1. Creates all meshes to calculate combined bounds
        /// 2. Recreates all meshes offset by the visual center
        /// Returns both the centered meshes and their true geographic centroids
        /// </summary>
        public static (List<UnityEngine.Mesh> meshes, List<Vector3> centroids) CreateCenteredMeshes(IReadOnlyList<RegionGeometry> geometries, Normalization normalization)
        {
            if (geometries == null || geometries.Count == 0)
            {
                return (new List<UnityEngine.Mesh>(), new List<Vector3>());
            }

            // First pass: create meshes without offset to calculate bounds and true centroids
            var tempMeshes = new List<UnityEngine.Mesh>();
            var originalCentroids = new List<Vector3>();
            
            foreach (var geometry in geometries)
            {
                var mesh = CreateMesh(geometry, normalization, Vector3.zero, geometry.Id);
                tempMeshes.Add(mesh);
                
                // Calculate true geographic centroid from the original geometry
                var centroid = CalculateGeometricCentroid(geometry, normalization);
                originalCentroids.Add(centroid);
            }

            // Calculate combined bounds
            var combinedBounds = tempMeshes[0].bounds;
            for (int i = 1; i < tempMeshes.Count; i++)
            {
                combinedBounds.Encapsulate(tempMeshes[i].bounds);
            }

            // Calculate visual center offset
            var visualCenter = combinedBounds.center;

            // Second pass: recreate meshes with visual center offset
            var centeredMeshes = new List<UnityEngine.Mesh>();
            var centeredCentroids = new List<Vector3>();
            
            for (int i = 0; i < geometries.Count; i++)
            {
                var geometry = geometries[i];
                var mesh = CreateMesh(geometry, normalization, visualCenter, geometry.Id, tempMeshes[i]);
                centeredMeshes.Add(mesh);
                
                // Apply the same visual center offset to the centroids
                centeredCentroids.Add(originalCentroids[i] - visualCenter);
            }

            return (centeredMeshes, centeredCentroids);
        }

        /// <summary>
        /// Calculates the geometric centroid of a region based on its polygon shapes.
        /// Uses area-weighted centroid calculation for accurate positioning.
        /// </summary>
        private static Vector3 CalculateGeometricCentroid(RegionGeometry geometry, Normalization normalization)
        {
            if (geometry == null || geometry.Shapes.Count == 0)
            {
                return Vector3.zero;
            }

            double totalArea = 0;
            double centroidX = 0;
            double centroidY = 0;

            foreach (var shape in geometry.Shapes)
            {
                if (shape.Outer == null || shape.Outer.Count < 3)
                {
                    continue;
                }

                // Normalize the polygon points
                var normalizedPoints = new List<Vector2>();
                foreach (var point in shape.Outer)
                {
                    var normalized = (point - normalization.Center) / normalization.Range;
                    normalizedPoints.Add(normalized);
                }

                // Calculate polygon area and centroid using the shoelace formula
                double area = 0;
                double cx = 0;
                double cy = 0;

                for (int i = 0; i < normalizedPoints.Count; i++)
                {
                    var current = normalizedPoints[i];
                    var next = normalizedPoints[(i + 1) % normalizedPoints.Count];
                    
                    double crossProduct = current.x * next.y - next.x * current.y;
                    area += crossProduct;
                    cx += (current.x + next.x) * crossProduct;
                    cy += (current.y + next.y) * crossProduct;
                }

                area *= 0.5;
                
                if (Math.Abs(area) > MinPolygonArea)
                {
                    cx /= (6.0 * area);
                    cy /= (6.0 * area);
                    
                    // Weight by absolute area (in case of holes)
                    double absArea = Math.Abs(area);
                    totalArea += absArea;
                    centroidX += cx * absArea;
                    centroidY += cy * absArea;
                }
            }

            if (totalArea > MinPolygonArea)
            {
                centroidX /= totalArea;
                centroidY /= totalArea;
            }

            return new Vector3((float)centroidX, (float)centroidY, 0f);
        }

        private static RegionPolygon ParsePolygon(JToken polygonElement, ref double minX, ref double minY, ref double maxX, ref double maxY)
        {
            var loops = new RegionPolygon();
            var isOuter = true;
            foreach (var ringElement in polygonElement)
            {
                var ring = new List<Vector2>();
                foreach (var coordinate in ringElement)
                {
                    if (coordinate.Count() < 2)
                    {
                        continue;
                    }

                    var lon = coordinate[0].Value<double>();
                    var lat = coordinate[1].Value<double>();
                    minX = Math.Min(minX, lon);
                    maxX = Math.Max(maxX, lon);
                    minY = Math.Min(minY, lat);
                    maxY = Math.Max(maxY, lat);
                    ring.Add(new Vector2((float)lon, (float)lat));
                }

                if (ring.Count < 3)
                {
                    continue;
                }

                if (isOuter)
                {
                    loops.Outer = ring;
                    isOuter = false;
                }
                else
                {
                    loops.Holes.Add(ring);
                }
            }

            return loops;
        }

        private static void AddContour(Tess tess, List<Vector2> points, bool isHole, Normalization normalization)
        {
            if (points == null || points.Count < 3)
            {
                return;
            }

            var contour = new ContourVertex[points.Count];
            var orientationIsClockwise = SignedArea(points) < 0f;
            var shouldReverse = isHole ? !orientationIsClockwise : orientationIsClockwise;
            for (var i = 0; i < points.Count; i++)
            {
                var index = shouldReverse ? points.Count - 1 - i : i;
                var normalized = (points[index] - normalization.Center) / normalization.Range;
                contour[i].Position = new Vec3(normalized.x, normalized.y, 0f);
            }

            tess.AddContour(contour, ContourOrientation.Original);
        }

        private static UnityEngine.Mesh BuildMeshFromTess(RegionGeometry region, Tess tess, Vector3 visualOffset, string meshName, UnityEngine.Mesh mesh)
        {
            var vertexCount = tess.Vertices.Length;
            if (mesh == null)
            {
                mesh = new UnityEngine.Mesh();
            }

            var vertices = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                var v = tess.Vertices[i].Position;
                vertices[i] = new Vector3(v.X, v.Y, 0f) - visualOffset;
            }

            var triangleCount = tess.ElementCount * 3;
            var indices = new int[triangleCount];
            for (int i = 0; i < triangleCount; i++)
            {
                indices[i] = tess.Elements[i];
            }

            mesh.Clear();
            mesh.name = string.IsNullOrEmpty(meshName) ? region.Id : meshName;
            mesh.indexFormat = vertexCount > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(indices, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static float SignedArea(List<Vector2> points)
        {
            double sum = 0;
            for (int i = 0; i < points.Count; i++)
            {
                var current = points[i];
                var next = points[(i + 1) % points.Count];
                sum += (next.x - current.x) * (next.y + current.y);
            }

            return (float)sum;
        }
    }

    public sealed class RegionGeometry
    {
        public string Id;
        public string Name;
        public List<RegionPolygon> Shapes = new();
    }

    public sealed class RegionPolygon
    {
        public List<Vector2> Outer = new();
        public List<List<Vector2>> Holes = new();
    }
}
