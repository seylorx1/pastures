using Godot;
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;


namespace Pastures
{
    [Tool]
    public partial class TerrainGenerator : MeshInstance3D
    {
        private Vector2 _scale = new(0.25f, 0.25f);
        private Vector2 _size = new(32.0f, 32.0f);
        private Vector2I _resolution = new(128, 128);

        [Export] Texture2D Heightmap { get; set; }

        [Export]
        public Vector2 Size
        {
            get => _size;

            set
            {
                _size = new
                (
                    x: Mathf.Max(value.X, 0),
                    y: Mathf.Max(value.Y, 0)
                );

                UpdateMeshScale();
            }
        }

        [Export]
        public Vector2I Resolution
        {
            get => _resolution;

            // Make sure resolution is never negative, and is always divisible by 2.
            set
            {
                _resolution = new
                (
                    x: Mathf.Max(value.X - (value.X % 2), 0),
                    y: Mathf.Max(value.Y - (value.Y % 2), 0)
                );

                UpdateMeshScale();
            }
        }

        [Export] public float Amplitude { get; set; } = 16.0f;

        // Buttons
        [Export]
        public bool GenerateMesh
        {
            get => false;
            set
            {
                if (value)
                    OnGenerateMesh();
            }
        }

        public void OnGenerateMesh()
        {
            Image heightmapImage = null;

            // Sample heightmap
            if (Heightmap != null)
                heightmapImage = Heightmap.GetImage();

            // Init arrays.
            Godot.Collections.Array surfaceArray = new();
            surfaceArray.Resize((int)Mesh.ArrayType.Max);

            List<Vector3> uniformVerts = new();
            List<Vector2> uniformUVs = new();
            List<int> uniformIndices = new();

            GenerateUniformMesh(heightmapImage, uniformVerts, uniformUVs, uniformIndices);

            Vector3[] uniformNormals = GenerateSmoothNormals(uniformVerts, uniformIndices);

            List<int> regions = CalculateRegionsFromUniformNormals(uniformNormals);

            // We don't need these normals anymore. Unreference them and hope the GC is blessing us.
            uniformNormals = null;

            int[] regionMap = GenerateRegionMap(regions, uniformVerts.Count);

            // Regenerate mesh, using regions.
            List<Vector3> verts = new();
            List<Vector2> uvs = new();
            int[] indexMap;

            GenerateVertsUVsFromRegionMap(regionMap, uniformVerts, uniformUVs, verts, uvs, out indexMap);

            // Save some memory and clear verts and UV caches.
            uniformVerts.Clear();
            uniformUVs.Clear();

            // Calculate indices
            List<int> indices = CalculateIndicesFromMaps(regionMap, indexMap);

            Vector3[] normals = GenerateSmoothNormals(verts, indices);

            // Apply lists to arrays.
            surfaceArray[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
            surfaceArray[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
            surfaceArray[(int)Mesh.ArrayType.Normal] = normals;
            surfaceArray[(int)Mesh.ArrayType.Index] = indices.ToArray();

            // Set the mesh.
            ArrayMesh arrMesh = new();
            arrMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surfaceArray);
            Mesh = arrMesh;
        }

        private float SampleHeight(Vector2 uv, Image heightmapImage = null)
        {
            // No heightmap? >:(
            if (heightmapImage == null)
                return Mathf.Sin(uv.X * Mathf.Tau) * Mathf.Cos(uv.Y * Mathf.Tau);

            int imageWidth = heightmapImage.GetWidth() - 1;
            int imageHeight = heightmapImage.GetHeight() - 1;

            Vector2I minPixel = new(Mathf.FloorToInt(uv.X * imageWidth), Mathf.FloorToInt(uv.Y * imageHeight));
            Vector2I maxPixel = new(Mathf.Clamp(minPixel.X + 1, 0, imageWidth), Mathf.Clamp(minPixel.Y + 1, 0, imageHeight));

            Vector2 percentPixel = new Vector2(uv.X * imageWidth, uv.Y * imageHeight) - (Vector2)minPixel;

            Color topLeftColour = heightmapImage.GetPixel(minPixel.X, maxPixel.Y);
            Color topRightColour = heightmapImage.GetPixel(maxPixel.X, maxPixel.Y);
            Color bottomLeftColour = heightmapImage.GetPixel(minPixel.X, minPixel.Y);
            Color bottomRightColour = heightmapImage.GetPixel(maxPixel.X, minPixel.Y);

            Color topColor = topLeftColour.Lerp(topRightColour, percentPixel.X);
            Color bottomColor = bottomLeftColour.Lerp(bottomRightColour, percentPixel.X);

            return bottomColor.Lerp(topColor, percentPixel.Y).R;
        }

        private void GenerateUniformMesh(Image heightmapImage, List<Vector3> verts, List<Vector2> uvs, List<int> indices)
        {
            // The starting vertex index of the previous row.
            // Set to -1 as the first row of vertices shouldn't build triangles.
            int previousRowIndex = -1;

            // Loop across rows.
            for (int z = 0; z < _resolution.Y + 1; z++)
            {
                // The starting vertex index of this current row.
                int rowIndex = GetRowIndex(z);

                // Vertex position (Z)
                float positionZ = (z - _resolution.Y / 2) * _scale.Y;

                // Calculate UV's Y.
                Vector2 uv = new(0f, (float)z / (_resolution.Y + 1));

                // Loop across columns in row.
                for (int x = 0; x < _resolution.X + 1; x++)
                {
                    // Vertex position (X)
                    float positionX = (x - _resolution.X / 2) * _scale.X;

                    // Calculate UV's X.
                    uv.X = (float)x / (_resolution.X + 1);

                    // Set vertex grid.
                    verts.Add(new Vector3(positionX, SampleHeight(uv, heightmapImage) * Amplitude, positionZ));

                    // Set uvs.
                    uvs.Add(uv);

                    // Build indices only if this isn't the first row
                    if (previousRowIndex >= 0)
                    {
                        // Build first triangle of quad on even indices, assuming we're not at the end.
                        if (x < _resolution.X)
                        {
                            indices.Add(rowIndex + x);
                            indices.Add(previousRowIndex + x);
                            indices.Add(previousRowIndex + x + 1);
                        }

                        // Build second triangle of quad, assuming we're not at the start.
                        if (x > 0)
                        {
                            indices.Add(rowIndex + x);
                            indices.Add(rowIndex + x - 1);
                            indices.Add(previousRowIndex + x);
                        }
                    }
                }

                // Cache this row index as previous, ready for next loop.
                previousRowIndex = rowIndex;
            }
        }

        private static Vector3[] GenerateSmoothNormals(List<Vector3> verts, List<int> indices)
        {
            // Allocate normals array.
            // Using an array here as the smooth normals algorthim doesn't scan incrementally.
            Vector3[] normals = new Vector3[verts.Count];

            // Calculate sum of normals across triangle ABC.
            for (int triangle = 0; triangle < indices.Count; triangle += 3)
            {
                int a = indices[triangle];
                int b = indices[triangle + 1];
                int c = indices[triangle + 2];

                Vector3 localisedNormal =
                (
                    verts[b] - verts[a]
                )
                .Cross
                (
                    verts[c] - verts[a]
                );

                normals[a] += localisedNormal;
                normals[b] += localisedNormal;
                normals[c] += localisedNormal;
            }

            // Normalise normals in array.
            for (int i = 0; i < normals.Length; i++)
            {
                normals[i] = -normals[i].Normalized();
            }

            return normals;
        }

        /// <summary>
        /// A list of vertex indices that can be represented by a quad larger than 1x1. <br/>
        ///  Index 0 - Min X<br/>
        ///  Index 1 - Min Z<br/>
        ///  Index 2 - Max X<br/>
        ///  Index 3 - Max Z<br/>
        /// and so forth...
        /// </summary>
        private List<int> CalculateRegionsFromUniformNormals(Vector3[] normals)
        {
            List<int> regions = new();

            // Iterate over normals.
            for (int z = 0; z < _resolution.Y + 1; z++)
            {
                int rowIndex = GetRowIndex(z);

                for (int x = 0; x < _resolution.X + 1; x++)
                {
                    // Check to see if we should create a new region.
                    bool searchNewRegion = true;
                    for (int r = 0; r < regions.Count; r += 4)
                    {
                        // Check to see if current index is in region bounds.
                        searchNewRegion =
                            x < regions[r + 0] ||
                            z < regions[r + 1] ||
                            x >= regions[r + 2] ||
                            z >= regions[r + 3];

                        if (!searchNewRegion)
                        {
                            // Stop checking regions.
                            break;
                        }
                    }

                    // Don't look for a new region. This current vertex isn't applicable!
                    if (!searchNewRegion)
                        continue;

                    // Start searching for largest possible quad with the same normals.

                    // Get the normal of the initial vertex.
                    Vector3 searchNormal = normals[rowIndex + x];

                    int width = 0;
                    int height = 0;

                    //float bestRatio = 0f;
                    //int bestArea = 0;

                    // Factors in ratio and area.
                    float bestScore = 0f;

                    int bestWidth = 0;
                    int bestHeight = 0;

                    // This is used to narrow the search down each iteration.
                    int widthMaximum = int.MaxValue;

                    int searchRowIndex = rowIndex;


                    // Search across to find largest surface area.
                    do
                    {
                        //Perform checks to see if next point is suitable.

                        // If width is within maximum
                        if (width < widthMaximum)
                        {
                            // Increment width.
                            width++;

                            // Test to see if this new area is intersecting.
                            bool areaIntersects = false;
                            for (int r = 0; r < regions.Count; r += 4)
                            {
                                areaIntersects =
                                (
                                    x < regions[r + 2] &&           // A min x < B max x
                                    x + width > regions[r + 0] &&   // A max x > B min x
                                    z < regions[r + 3] &&           // A min z < B max z
                                    z + height > regions[r + 1]     // A max z > B min z
                                );

                                // This current area intersects another region.
                                if (areaIntersects)
                                {
                                    // GD.Print($"Region {regions.Count / 4 + 1} intersects with region {(r + 4) / 4}");
                                    break;
                                }

                            }

                            // If normals aren't too dissimilar and this area isn't intersecting another region
                            if (Mathf.Abs(searchNormal.Dot(normals[searchRowIndex + width])) > 0.9999f && !areaIntersects)
                            {
                                // If we're still going to be in bounds on the next increment, continue.
                                if (x + width < _resolution.X)
                                {
                                    continue;
                                }
                            }
                            // Normals are too dissimilar or another area is being intersected.
                            // Go back one width to when it was likely alright.
                            else
                            {
                                width--;
                            }

                            // We've hit the end of the line.
                        }

                        // Narrow width maximum, if applicable, for next iteration.
                        widthMaximum = Mathf.Min(widthMaximum, width);

                        // Never got to check horizontally before hitting a vertex with dissimilar normals.
                        // This is the end of the loop.
                        if (width <= 0)
                            break;

                        // We can't do much with a single strip of vertices.
                        if (height > 0)
                        {
                            // Calculate the area.
                            int area = width * height;

                            // Calculate the ratio of current area, with the largest value always in the denominator.
                            float ratio = width < height ? ((float)width) / height : ((float)height) / width;

                            // Calculate the score.
                            float score = area + area * ratio;

                            if (bestScore < score)
                            {
                                bestScore = score;
                                bestWidth = width;
                                bestHeight = height;
                            }
                        }

                        // Reset width.
                        width = -1;

                        // Update height and search row index.
                        height++;
                        searchRowIndex = GetRowIndex(z + height);

                        // Move on to next row.

                    }
                    while
                    (
                        // Height doesn't exceed bounds
                        z + height <= _resolution.Y
                    );

                    // Check to see if we have at least a one-wide or one-deep area that is greater than 1x1.
                    if ((bestWidth * bestHeight) > 1)
                    {
                        regions.Add(x);
                        regions.Add(z);
                        regions.Add(x + bestWidth);
                        regions.Add(z + bestHeight);

                        //GD.Print($"{x}, {z} -> {bestWidth}, {bestHeight}");
                    }
                }
            }

            return regions;
        }

        private int[] GenerateRegionMap(List<int> regions, int uniformVertexCount, bool verbose = false)
        {
            // A map-like array that caches if any given vertex is in a region or not.
            int[] vertsRegionMap = new int[uniformVertexCount];

            // Step through regions.
            for (int r = 0; r < regions.Count; r += 4)
            {
                for (int z = regions[r + 1]; z <= regions[r + 3]; z++)
                {
                    int rowIndex = GetRowIndex(z);
                    for (int x = regions[r + 0]; x <= regions[r + 2]; x++)
                    {
                        int index = rowIndex + x;

                        // Bottom left corner
                        if (x == regions[r + 0] && z == regions[r + 1])
                        {
                            vertsRegionMap[index] &= ~0b00010000; // Unset any 'ignore' bits.
                            vertsRegionMap[index] |= 0b00000001;
                        }
                        // Bottom right corner
                        else if (x == regions[r + 2] && z == regions[r + 1])
                        {
                            vertsRegionMap[index] &= ~0b00010000; // Unset any 'ignore' bits.
                            vertsRegionMap[index] |= 0b00000010;
                        }
                        // Top left corner
                        else if (x == regions[r + 0] && z == regions[r + 3])
                        {
                            vertsRegionMap[index] &= ~0b00010000; // Unset any 'ignore' bits.
                            vertsRegionMap[index] |= 0b00000100;
                        }
                        // Top right corner
                        else if (x == regions[r + 2] && z == regions[r + 3])
                        {
                            vertsRegionMap[index] &= ~0b00010000; // Unset any 'ignore' bits.
                            vertsRegionMap[index] |= 0b00001000;
                        }
                        // Elsewhere in bounds, assuming it's not already been marked a corner, mark to ignore.
                        else if (vertsRegionMap[index] == 0)
                        {
                            vertsRegionMap[index] |= 0b00010000;
                        }

                    }
                }

                if (verbose)
                {
                    GD.Print($"\nGenerated Region Map (Step {(r + 4) / 4} / {regions.Count / 4})");
                    GD.Print($"Region {(r + 4) / 4} Data: Min({regions[r + 0] + 1}, {regions[r + 1] + 1}) Max({regions[r + 2] + 1}, {regions[r + 3] + 1})");
                    for (int z = _resolution.Y; z >= 0; z--)
                    {
                        int rowIndex = GetRowIndex(z);
                        string rowDebug = $"Row {z:D2}: ";
                        for (int x = 0; x <= _resolution.X; x++)
                        {
                            int flag = vertsRegionMap[rowIndex + x];
                            rowDebug += flag == 0 ? "[] " : flag == 16 ? "-- " : $"{flag:D2} ";
                        }
                        GD.Print(rowDebug);
                    }
                }
            }

            return vertsRegionMap;
        }

        // private int[] GenerateIndexMapFromRegionMap(int[] vertsRegionMap, bool verbose = true)
        // {
        //     int[] vertsIndexMap = new int[vertsRegionMap.Length];

        //     // Build vertsIndexMap.
        //     int indexIncr = 0;
        //     for (int z = 0; z < _resolution.Y + 1; z++)
        //     {
        //         int rowIndex = GetRowIndex(z);

        //         for (int x = 0; x < _resolution.X + 1; x++)
        //         {
        //             // Vertex is within bounds of region. Ignore.
        //             if ((vertsRegionMap[rowIndex + x] & 0b00010000) == 0b00010000)
        //             {
        //                 vertsIndexMap[rowIndex + x] = -1;
        //             }
        //             // Convert vertsRegionMap to an index map.
        //             else
        //             {
        //                 vertsIndexMap[rowIndex + x] = indexIncr;
        //                 indexIncr++;
        //             }
        //         }
        //     }



        //     return vertsIndexMap;
        // }

        private void GenerateVertsUVsFromRegionMap(int[] regionMap, List<Vector3> uniformVerts, List<Vector2> uniformUVs, List<Vector3> verts, List<Vector2> uvs, out int[] indexMap, bool verbose = false)
        {
            // Map indexes to region.
            indexMap = new int[regionMap.Length];
            int indexIncr = 0;

            // Copy over verts and UVs.
            for (int z = 0; z < _resolution.Y + 1; z++)
            {
                int rowIndex = GetRowIndex(z);
                for (int x = 0; x < _resolution.X + 1; x++)
                {
                    bool addVert = false;

                    // This is a corner of a region so we need to add this vertex.
                    int regionBitmask = regionMap[rowIndex + x];
                    if (regionBitmask > 0 && regionBitmask < 0b00010000)
                    {
                        addVert = true;
                    }

                    // We need to sample around the current vertex to see if there are any adjacent quads.
                    // If ANY of the neighbours are not marked as a region, add this vertex.
                    else
                    {
                        for (int sampleZ = z - 1; sampleZ <= z + 1; sampleZ++)
                        {
                            // Out of bounds.
                            if (sampleZ < 0 || sampleZ > _resolution.Y)
                                continue;

                            for (int sampleX = x - 1; sampleX <= x + 1; sampleX++)
                            {
                                // Out of bounds.
                                if (sampleX < 0 || sampleX > _resolution.X)
                                    continue;

                                // Don't need to sample centre.
                                if (sampleX == x && sampleZ == z)
                                    continue;

                                // If any sample is not in a region, we need to add a vertex.
                                if (regionMap[GetRowIndex(sampleZ) + sampleX] == 0)
                                {
                                    addVert = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (addVert)
                    {
                        verts.Add(uniformVerts[rowIndex + x]);
                        uvs.Add(uniformUVs[rowIndex + x]);

                        indexMap[rowIndex + x] = indexIncr;
                        indexIncr++;
                    }
                    else
                    {
                        indexMap[rowIndex + x] = -1;
                    }

                }
            }

            if (verbose)
            {
                GD.Print($"\nGenerated Index Map ");

                string colDebug = "Column: ";
                for (int x = 0; x <= _resolution.X; x++)
                {
                    colDebug += $"{x:D3} ";
                }
                GD.Print(colDebug);

                for (int z = _resolution.Y; z >= 0; z--)
                {
                    int rowIndex = GetRowIndex(z);
                    string rowDebug = $"Row {z:D2}: ";
                    for (int x = 0; x <= _resolution.X; x++)
                    {
                        rowDebug += indexMap[rowIndex + x] == -1 ? "--- " : $"{indexMap[rowIndex + x]:D3} ";
                    }
                    GD.Print(rowDebug);
                }
            }
        }

        private List<int> CalculateIndicesFromMaps(int[] vertsRegionMap, int[] vertsIndexMap, bool verbose = false)
        {
            List<int> indices = new();

            for (int z = 0; z < _resolution.Y; z++) // Ignore last + 1.
            {
                int rowIndex = GetRowIndex(z);
                int nextRowIndex = GetRowIndex(z + 1);

                for (int x = 0; x < _resolution.X; x++) // Ignore last + 1.
                {
                    // Check if the bottom left region mask has been set.
                    if ((vertsRegionMap[rowIndex + x] & 0b00000001) == 0b00000001)
                    {
                        // Iterate up until the top left corner is found.

                        bool foundZ = false;

                        int searchRowIndex = -1;
                        for (int searchZ = z + 1; searchZ <= _resolution.Y + 1; searchZ++)
                        {
                            searchRowIndex = GetRowIndex(searchZ);

                            if ((vertsRegionMap[searchRowIndex + x] & 0b00000100) == 0b00000100)
                            {
                                foundZ = true;
                                break;
                            }
                        }

                        if (!foundZ)
                        {
                            GD.PrintErr("Top of region not sealed!");

                        }

                        // Iterate right until the bottom right corner is found.

                        bool foundX = false;

                        int searchX;
                        for (searchX = x + 1; searchX <= _resolution.X + 1; searchX++)
                        {
                            if ((vertsRegionMap[rowIndex + searchX] & 0b00000010) == 0b00000010)
                            {
                                foundX = true;
                                break;
                            }
                        }

                        if (!foundX)
                        {
                            GD.PrintErr("Right of region not sealed!");
                        }

                        // We don't need to look for the top right hand corner, it can be assumed that it's there.

                        // Triangle 1.
                        indices.Add(vertsIndexMap[searchRowIndex + x]);
                        indices.Add(vertsIndexMap[rowIndex + x]);
                        indices.Add(vertsIndexMap[rowIndex + searchX]);

                        // Triangle 2.
                        indices.Add(vertsIndexMap[searchRowIndex + searchX]);
                        indices.Add(vertsIndexMap[searchRowIndex + x]);
                        indices.Add(vertsIndexMap[rowIndex + searchX]);
                    }

                    // Check if this vertex has not been ignored.
                    else if ((vertsRegionMap[rowIndex + x] & 0b00010000) != 0b00010000)
                    {
                        // Check if quad points are valid.
                        if
                        (
                            vertsIndexMap[rowIndex + x] < 0 ||
                            vertsIndexMap[rowIndex + x + 1] < 0 ||
                            vertsIndexMap[nextRowIndex + x] < 0 ||
                            vertsIndexMap[nextRowIndex + x + 1] < 0
                        )
                            continue;

                        // Triangle 1.
                        indices.Add(vertsIndexMap[nextRowIndex + x]);
                        indices.Add(vertsIndexMap[rowIndex + x]);
                        indices.Add(vertsIndexMap[rowIndex + x + 1]);

                        // Triangle 2.
                        indices.Add(vertsIndexMap[nextRowIndex + x + 1]);
                        indices.Add(vertsIndexMap[nextRowIndex + x]);
                        indices.Add(vertsIndexMap[rowIndex + x + 1]);
                    }
                }
            }

            if (verbose)
            {
                for (int idx = 0; idx < indices.Count; idx += 3)
                {
                    int triangle = (idx + 3) / 3;
                    GD.Print($"\nTriangle {2 + (triangle % 2) * -1}: Index {triangle}");
                    GD.Print(indices[idx + 0]);
                    GD.Print(indices[idx + 1]);
                    GD.Print(indices[idx + 2]);
                }
            }

            return indices;
        }

        private void UpdateMeshScale()
        {
            _scale = new
            (
                _size.X / _resolution.X,
                _size.Y / _resolution.Y
            );

            GD.Print
            (
                "Mesh scale updated to " +
                $"[{_scale.X}, {_scale.Y}] " +
                "(with a size of " +
                $"[{_size.X}m, {_size.Y}m] " +
                "at a resolution of " +
                $"[{_resolution.X}, {_resolution.Y}])"
            );
        }

        private int GetRowIndex(int z) =>
            // Offset
            z *
            // Width
            (_resolution.X + 1);

    }
}