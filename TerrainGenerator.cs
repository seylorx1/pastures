using Godot;
using System.Collections.Generic;

namespace Pastures;

[Tool]
public partial class TerrainGenerator : MeshInstance3D
{
    private Vector2 _scale = new(0.25f, 0.25f);
    private Vector2 _size = new(32.0f, 32.0f);
    private int _resolution = 128;
    private int _vertexResolution = 129;

    [Export] Texture2D Heightmap { get; set; }

    [Export]
    public Vector2 Size
    {
        get => _size;

        set
        {
            _size = new
            (
                x: Mathf.Max(value.X, 1),
                y: Mathf.Max(value.Y, 1)
            );

            UpdateMeshScale();
        }
    }

    [Export]
    public int Resolution
    {
        get => _resolution;

        // Make sure resolution is never negative, and is always divisible by 2.
        set
        {
            _resolution = 0;
            for (int i = 9; i > 0; i--)
            {
                int res = 1 << (4 + i);
                if (value >= res)
                {
                    _resolution = res;
                    break;
                }
            }
            if (_resolution == 0)
            {
                _resolution = 128;
            }

            _vertexResolution = _resolution + 1;

            UpdateMeshScale();
        }
    }

    [Export] public float Amplitude { get; set; } = 16.0f;
    [Export] public float SimplifyThreshold { get; set; } = 0.005f;

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

        GenerateUniformMesh(heightmapImage, uniformVerts, uniformUVs, uniformIndices, out TerrainQuadCell[,] terrainQuadCells);

        Vector3[] uniformNormals = GenerateSmoothNormals(uniformVerts, uniformIndices);

        BitwiseOptimiseTerrainQuadCells(_resolution, 0, 0, uniformNormals, terrainQuadCells);

        // for (int z = _resolution - 1; z >= 0; z--)
        // {
        //     string row = $"Row {z:D2}: ";
        //     for (int x = 0; x < _resolution; x++)
        //     {
        //         if (terrainQuadCells[x, z].IsCellSingleQuad())
        //             row += "  ";
        //         else if (terrainQuadCells[x, z].edges == 0b1111)
        //             row += "• ";
        //         else if (terrainQuadCells[x, z].IsCornerVertex(TerrainQuadCell.Corner.BottomRight))
        //             row += "R ";
        //         else
        //             row += "# ";
        //     }
        //     GD.Print(row);
        // }

        PopulateVertsFromTerrainQuadCells(terrainQuadCells, uniformVerts, uniformUVs, out List<Vector3> verts, out List<Vector2> uvs);

        List<int> indices = BuildIndicesFromTerrainQuadCells(terrainQuadCells);

        Vector3[] normals = GenerateSmoothNormals(verts, indices);

        BitwiseCorrectGapsFromTerrainQuadCells(_resolution, verts, normals, terrainQuadCells);


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

    private List<int> BuildIndicesFromTerrainQuadCells(TerrainQuadCell[,] terrainQuadCells)
    {
        // Populate indices from terrain quad cells.
        List<int> indices = new();

        // Find a valid starting quad cell.
        for (int z = 0; z < _resolution; z++)
        {
            for (int x = 0; x < _resolution; x++)
            {
                // We only want to look at cells which have a bottom left hand vertex, indicating the start of a quad.
                // Anything else is a part of a quad.
                if (!terrainQuadCells[x, z].IsCornerVertex(TerrainQuadCell.Corner.BottomLeft))
                    continue;

                int[] quadCorners = new int[4];

                // It's important to note here that these quads always have a 1:1 ratio.

                // Search in one direction to find a corner.
                int length = 0;

                // Move from current x position to the end of the map.
                for (int searchX = x; searchX < _resolution; searchX++)
                {
                    // Once a bottom right hand corner has been detected, set the length and stop looking.
                    // Note that the length can be 0 for single quads.
                    if (terrainQuadCells[searchX, z].IsCornerVertex(TerrainQuadCell.Corner.BottomRight))
                    {
                        length = searchX - x;
                        break;
                    }
                }

                // The rest of the corners can be extrapolated from the length.
                quadCorners[(int)TerrainQuadCell.Corner.BottomLeft] = terrainQuadCells[x, z].cornerIndex[(int)TerrainQuadCell.Corner.BottomLeft];
                quadCorners[(int)TerrainQuadCell.Corner.BottomRight] = terrainQuadCells[x + length, z].cornerIndex[(int)TerrainQuadCell.Corner.BottomRight];
                quadCorners[(int)TerrainQuadCell.Corner.TopLeft] = terrainQuadCells[x, z + length].cornerIndex[(int)TerrainQuadCell.Corner.TopLeft];
                quadCorners[(int)TerrainQuadCell.Corner.TopRight] = terrainQuadCells[x + length, z + length].cornerIndex[(int)TerrainQuadCell.Corner.TopRight];

                // With these indices at our disposal, build triangles.

                // Triangle 1.
                indices.Add(quadCorners[(int)TerrainQuadCell.Corner.TopLeft]);
                indices.Add(quadCorners[(int)TerrainQuadCell.Corner.BottomLeft]);
                indices.Add(quadCorners[(int)TerrainQuadCell.Corner.BottomRight]);

                // Triangle 2.
                indices.Add(quadCorners[(int)TerrainQuadCell.Corner.TopLeft]);
                indices.Add(quadCorners[(int)TerrainQuadCell.Corner.BottomRight]);
                indices.Add(quadCorners[(int)TerrainQuadCell.Corner.TopRight]);

            }
        }

        return indices;
    }

    private void PopulateVertsFromTerrainQuadCells(TerrainQuadCell[,] terrainQuadCells, List<Vector3> uniformVerts, List<Vector2> uniformUVs, out List<Vector3> verts, out List<Vector2> uvs)
    {
        verts = new();
        uvs = new();

        // Iterate over uniform vertices.
        for (int z = 0; z < _vertexResolution; z++)
        {
            for (int x = 0; x < _vertexResolution; x++)
            {
                int copyVertAtIndex = -1;

                // Get surrounding quad cells.

                // Quads with vertex at bottom
                if (z < _resolution)
                {
                    // Quads with vertex at bottom left
                    if (x < _resolution && terrainQuadCells[x, z].IsCornerVertex(TerrainQuadCell.Corner.BottomLeft))
                    {
                        // Set corner index to vert we're just about to add.
                        terrainQuadCells[x, z].cornerIndex[(int)TerrainQuadCell.Corner.BottomLeft] = verts.Count;

                        // Mark the vert to be copied.
                        copyVertAtIndex = terrainQuadCells[x, z].uniformCornerIndex[(int)TerrainQuadCell.Corner.BottomLeft];
                    }

                    // Quads with vertex at bottom right
                    if (x > 0 && terrainQuadCells[x - 1, z].IsCornerVertex(TerrainQuadCell.Corner.BottomRight))
                    {
                        // Set corner index to vert we're just about to add.
                        terrainQuadCells[x - 1, z].cornerIndex[(int)TerrainQuadCell.Corner.BottomRight] = verts.Count;

                        // Mark the vert to be copied.
                        copyVertAtIndex = terrainQuadCells[x - 1, z].uniformCornerIndex[(int)TerrainQuadCell.Corner.BottomRight];
                    }
                }

                // Quads with vertex at top
                if (z > 0)
                {
                    // Quads with vertex at top left
                    if (x < _resolution && terrainQuadCells[x, z - 1].IsCornerVertex(TerrainQuadCell.Corner.TopLeft))
                    {
                        // Set corner index to vert we're just about to add.
                        terrainQuadCells[x, z - 1].cornerIndex[(int)TerrainQuadCell.Corner.TopLeft] = verts.Count;

                        // Mark the vert to be copied.
                        copyVertAtIndex = terrainQuadCells[x, z - 1].uniformCornerIndex[(int)TerrainQuadCell.Corner.TopLeft];
                    }

                    //Quads with vertex at top right
                    if (x > 0 && terrainQuadCells[x - 1, z - 1].IsCornerVertex(TerrainQuadCell.Corner.TopRight))
                    {
                        // Set corner index to vert we're just about to add.
                        terrainQuadCells[x - 1, z - 1].cornerIndex[(int)TerrainQuadCell.Corner.TopRight] = verts.Count;

                        // Mark the vert to be copied.
                        copyVertAtIndex = terrainQuadCells[x - 1, z - 1].uniformCornerIndex[(int)TerrainQuadCell.Corner.TopRight];
                    }
                }

                // If the vert at index has been marked, copy into list.
                if (copyVertAtIndex >= 0)
                {
                    verts.Add(uniformVerts[copyVertAtIndex]);
                    uvs.Add(uniformUVs[copyVertAtIndex]);
                }
            }
        }
    }

    /// <summary>
    /// Corrects gaps in mesh, introduced by simplifying, by searching for largest quads and moving surrounding verts.
    /// </summary>
    public void BitwiseCorrectGapsFromTerrainQuadCells(int searchResolution, List<Vector3> verts, Vector3[] normals, TerrainQuadCell[,] terrainQuadCells)
    {
        if (searchResolution < 4)
            return;

        int halfResolution = searchResolution >> 1;

        for (int sectionZ = 0; sectionZ < _resolution; sectionZ += halfResolution)
        {
            for (int sectionX = 0; sectionX < _resolution; sectionX += halfResolution)
            {
                // Check to see whether this section is a single quad.

                // These quads start from the bottom left, so ensure this is the case for this section.
                if (!terrainQuadCells[sectionX, sectionZ].IsCornerVertex(TerrainQuadCell.Corner.BottomLeft))
                    continue;

                // As quads have a ratio of 1:1, we only need to look in one direction.
                // Given the minimum quad size is 2x2 cells, we can miss the first cell. (searchX = 1)
                bool validQuadrant = true;
                for (int searchX = 0; searchX < halfResolution; searchX++)
                {
                    bool isBottomRight = terrainQuadCells[sectionX + searchX, sectionZ].IsCornerVertex(TerrainQuadCell.Corner.BottomRight);

                    if (searchX < halfResolution - 1)
                    {
                        if (isBottomRight)
                        {
                            validQuadrant = false;
                            break;
                        }
                    }
                    else
                    {
                        if (!isBottomRight)
                        {
                            validQuadrant = false;
                            break;
                        }
                    }

                }

                if (!validQuadrant)
                    continue;

                // Get the positions of the four corners of this quadrant.

                int bottomLeftIndex = terrainQuadCells[sectionX, sectionZ].cornerIndex[(int)TerrainQuadCell.Corner.BottomLeft];
                int bottomRightIndex = terrainQuadCells[sectionX + halfResolution - 1, sectionZ].cornerIndex[(int)TerrainQuadCell.Corner.BottomRight];
                int topLeftIndex = terrainQuadCells[sectionX, sectionZ + halfResolution - 1].cornerIndex[(int)TerrainQuadCell.Corner.TopLeft];
                int topRightIndex = terrainQuadCells[sectionX + halfResolution - 1, sectionZ + halfResolution - 1].cornerIndex[(int)TerrainQuadCell.Corner.TopRight];

                // Now we need to search for vertices that aren't connected, but are aligned with edges.
                int cellX;
                int cellZ;

                // Search along bottom edge, if still in bounds.
                cellZ = sectionZ - 1;
                if (cellZ >= 0)
                {
                    // Search along quad cells, missing the last cell before the end.
                    // (The last cell has vertex which is corner of the current quad.)
                    for (cellX = sectionX; cellX < sectionX + halfResolution - 1; cellX++)
                    {
                        // Check if vertex exists.
                        if (terrainQuadCells[cellX, cellZ].IsCornerVertex(TerrainQuadCell.Corner.TopRight))
                        {
                            // Get vert index.
                            int vertIndex = terrainQuadCells[cellX, cellZ].cornerIndex[(int)TerrainQuadCell.Corner.TopRight];

                            // Get vert 'progress' along quad edge as weight.
                            float weight = (verts[vertIndex].X - verts[bottomLeftIndex].X) / (verts[bottomRightIndex].X - verts[bottomLeftIndex].X);

                            // Set height along quad edge.
                            verts[vertIndex] = new(verts[vertIndex].X, Mathf.Lerp(verts[bottomLeftIndex].Y, verts[bottomRightIndex].Y, weight), verts[vertIndex].Z);

                            // Set normal along quad edge.
                            normals[vertIndex] = normals[bottomLeftIndex].Lerp(normals[bottomRightIndex], weight);
                        }
                    }
                }

                // Search along top edge, if still in bounds.
                cellZ = sectionZ + halfResolution;
                if (cellZ < _resolution)
                {
                    // Search along quad cells, missing the last cell before the end.
                    // (The last cell has vertex which is corner of the current quad.)
                    for (cellX = sectionX; cellX < sectionX + halfResolution - 1; cellX++)
                    {
                        // Check if vertex exists.
                        if (terrainQuadCells[cellX, cellZ].IsCornerVertex(TerrainQuadCell.Corner.BottomRight))
                        {
                            // Get vert index.
                            int vertIndex = terrainQuadCells[cellX, cellZ].cornerIndex[(int)TerrainQuadCell.Corner.BottomRight];

                            // Get vert 'progress' along quad edge as weight.
                            float weight = (verts[vertIndex].X - verts[topLeftIndex].X) / (verts[topRightIndex].X - verts[topLeftIndex].X);

                            // Set height along quad edge.
                            verts[vertIndex] = new(verts[vertIndex].X, Mathf.Lerp(verts[topLeftIndex].Y, verts[topRightIndex].Y, weight), verts[vertIndex].Z);

                            // Set normal along quad edge.
                            normals[vertIndex] = normals[topLeftIndex].Lerp(normals[topRightIndex], weight);
                        }
                    }
                }

                // Search along left edge, if still in bounds.
                cellX = sectionX - 1;
                if (cellX >= 0)
                {
                    // Search along quad cells, missing the last cell before the end.
                    // (The last cell has vertex which is corner of the current quad.)
                    for (cellZ = sectionZ; cellZ < sectionZ + halfResolution - 1; cellZ++)
                    {
                        // Check if vertex exists.
                        if (terrainQuadCells[cellX, cellZ].IsCornerVertex(TerrainQuadCell.Corner.TopRight))
                        {
                            // Get vert index.
                            int vertIndex = terrainQuadCells[cellX, cellZ].cornerIndex[(int)TerrainQuadCell.Corner.TopRight];

                            // Get vert 'progress' along quad edge as weight.
                            float weight = (verts[vertIndex].Z - verts[bottomLeftIndex].Z) / (verts[topLeftIndex].Z - verts[bottomLeftIndex].Z);

                            // Set height along quad edge.
                            verts[vertIndex] = new(verts[vertIndex].X, Mathf.Lerp(verts[bottomLeftIndex].Y, verts[topLeftIndex].Y, weight), verts[vertIndex].Z);

                            // Set normal along quad edge.
                            normals[vertIndex] = normals[bottomLeftIndex].Lerp(normals[topLeftIndex], weight);
                        }
                    }
                }

                // Search along right edge, if still in bounds.
                cellX = sectionX + halfResolution;
                if (cellX < _resolution)
                {
                    // Search along quad cells, missing the last cell before the end.
                    // (The last cell has vertex which is corner of the current quad.)
                    for (cellZ = sectionZ; cellZ < sectionZ + halfResolution - 1; cellZ++)
                    {
                        // Check if vertex exists.
                        if (terrainQuadCells[cellX, cellZ].IsCornerVertex(TerrainQuadCell.Corner.TopLeft))
                        {
                            // Get vert index.
                            int vertIndex = terrainQuadCells[cellX, cellZ].cornerIndex[(int)TerrainQuadCell.Corner.TopLeft];

                            // Get vert 'progress' along quad edge as weight.
                            float weight = (verts[vertIndex].Z - verts[bottomRightIndex].Z) / (verts[topRightIndex].Z - verts[bottomRightIndex].Z);

                            // Set height along quad edge.
                            verts[vertIndex] = new(verts[vertIndex].X, Mathf.Lerp(verts[bottomRightIndex].Y, verts[topRightIndex].Y, weight), verts[vertIndex].Z);

                            // Set normal along quad edge.
                            normals[vertIndex] = normals[bottomRightIndex].Lerp(normals[topRightIndex], weight);
                        }
                    }
                }
            }
        }

        BitwiseCorrectGapsFromTerrainQuadCells(halfResolution, verts, normals, terrainQuadCells);
    }

    public void BitwiseOptimiseTerrainQuadCells(int searchResolution, int originX, int originZ, Vector3[] uniformNormals, TerrainQuadCell[,] terrainQuadCells)
    {
        if (searchResolution < 4)
            return;

        // In resolution is assumed to be full, so we need to step it down.
        // For example, let's assume it's 64 x 64.

        int halfResolution = searchResolution >> 1;

        // Now the resolution is halved to 32 x 32.

        for (int quadrantZ = 0; quadrantZ < searchResolution; quadrantZ += halfResolution)
        {
            for (int quadrantX = 0; quadrantX < searchResolution; quadrantX += halfResolution)
            {
                // This offsets the search quadrant as a 2x2 of the search resolution.
                // This can be thought of as the working area.

                // _resolution is referencing the quad count per axis, but we need vertices.
                // Half resolution is increased by one to allow for searching through quads.

                // The property of this array changes, but it's purpose is to calculate the standard deviation of this quadrant.
                Vector3[] workingNormals = new Vector3[(halfResolution + 1) * (halfResolution + 1)];

                // Begin calculating standard deviation.
                Vector3 normalMean = Vector3.Zero;

                // Search through vertices.

                for (int vertexSearchZ = 0; vertexSearchZ < halfResolution + 1; vertexSearchZ++)
                {
                    for (int vertexSearchX = 0; vertexSearchX < halfResolution + 1; vertexSearchX++)
                    {
                        // Get the location of this loop in index space.
                        int vertexX = originX + quadrantX + vertexSearchX;
                        int vertexZ = originZ + quadrantZ + vertexSearchZ;

                        // Get normal
                        Vector3 normal = uniformNormals[vertexZ * _vertexResolution + vertexX];

                        // Populate search normals array.
                        workingNormals[vertexSearchZ * (halfResolution + 1) + vertexSearchX] = normal;

                        // Add to normal mean.
                        normalMean += normal;

                    }
                }

                // Calculate mean.
                normalMean /= workingNormals.Length;

                // Begin calculating the variance.
                Vector3 variance = Vector3.Zero;

                // Update search normals to subtract the mean and square the result.
                for (int i = 0; i < workingNormals.Length; i++)
                    workingNormals[i] = (workingNormals[i] - normalMean) * (workingNormals[i] - normalMean);

                // Get the mean of this new value to set the variance.
                foreach (Vector3 normal in workingNormals)
                    variance += normal;
                variance /= workingNormals.Length;

                // While you can't get the standard deviation of a vector (as this metric has loose definitions when working with space)
                // We can approximate something akin to it by getting the length of this new vector. (it's close because it involves a square root...)
                float standardDeviation = variance.Length();

                // This area is pretty flat!
                // We don't need to search for finer details, just blanket this area as a quad.
                if (standardDeviation < SimplifyThreshold)
                {
                    for (int quadSearchZ = 0; quadSearchZ < halfResolution; quadSearchZ++)
                    {
                        for (int quadSearchX = 0; quadSearchX < halfResolution; quadSearchX++)
                        {
                            int quadX = originX + quadrantX + quadSearchX;
                            int quadZ = originZ + quadrantZ + quadSearchZ;

                            // Assign sides to quad.

                            bool internalQuad = true;

                            if (quadSearchX == 0)
                            {
                                terrainQuadCells[quadX, quadZ].SetExternalEdge(TerrainQuadCell.Edge.Left);
                                internalQuad = false;
                            }
                            if (quadSearchX == halfResolution - 1)
                            {
                                terrainQuadCells[quadX, quadZ].SetExternalEdge(TerrainQuadCell.Edge.Right);
                                internalQuad = false;
                            }
                            if (quadSearchZ == 0)
                            {
                                terrainQuadCells[quadX, quadZ].SetExternalEdge(TerrainQuadCell.Edge.Down);
                                internalQuad = false;
                            }
                            if (quadSearchZ == halfResolution - 1)
                            {
                                terrainQuadCells[quadX, quadZ].SetExternalEdge(TerrainQuadCell.Edge.Up);
                                internalQuad = false;
                            }

                            if (internalQuad)
                                terrainQuadCells[quadX, quadZ].edges = 0b1111;
                        }
                    }
                }
                // Uh oh, it's bumpy!
                // We need to keep searching to see if we can find any more nuggets of optimisation.
                else
                {
                    BitwiseOptimiseTerrainQuadCells(halfResolution, originX + quadrantX, originZ + quadrantZ, uniformNormals, terrainQuadCells);
                }
            }
        }
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

    private void GenerateUniformMesh(Image heightmapImage, List<Vector3> verts, List<Vector2> uvs, List<int> indices, out TerrainQuadCell[,] terrainQuads)
    {
        terrainQuads = new TerrainQuadCell[_resolution, _resolution];

        // Loop across rows.
        for (int z = 0; z < _vertexResolution; z++)
        {
            // Vertex position (Z)
            float positionZ = (z - _vertexResolution / 2) * _scale.Y;

            // Calculate UV's Y.
            Vector2 uv = new(0f, (float)z / _vertexResolution);

            // Loop across columns in row.
            for (int x = 0; x < _vertexResolution; x++)
            {
                // Vertex position (X)
                float positionX = (x - _vertexResolution / 2) * _scale.X;

                // Calculate UV's X.
                uv.X = (float)x / _vertexResolution;

                // Set vertex grid.
                verts.Add(new Vector3(positionX, SampleHeight(uv, heightmapImage) * Amplitude, positionZ));

                // Set uvs.
                uvs.Add(uv);

                // Build indices only if this isn't the first vertex
                if (z > 0 && x > 0)
                {
                    // Build quad.
                    int topLeft = z * _vertexResolution + (x - 1);
                    int topRight = z * _vertexResolution + x;
                    int bottomLeft = (z - 1) * _vertexResolution + (x - 1);
                    int bottomRight = (z - 1) * _vertexResolution + x;

                    // Add triangle 1 to indices.
                    indices.Add(topLeft);
                    indices.Add(bottomLeft);
                    indices.Add(bottomRight);

                    // Add triangle 2 to indices.
                    indices.Add(topLeft);
                    indices.Add(bottomRight);
                    indices.Add(topRight);

                    // Init quad.
                    terrainQuads[x - 1, z - 1] = new
                    (
                        topLeft,
                        topRight,
                        bottomLeft,
                        bottomRight
                    );
                }

            }
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

    private void UpdateMeshScale()
    {
        _scale = new
        (
            _size.X / _resolution,
            _size.Y / _resolution
        );

        GD.Print
        (
            "Mesh scale updated to " +
            $"[{_scale.X}, {_scale.Y}] " +
            "(with a size of " +
            $"[{_size.X}m, {_size.Y}m] " +
            "at a resolution of " +
            $"[{_resolution}, {_resolution}])"
        );
    }

}