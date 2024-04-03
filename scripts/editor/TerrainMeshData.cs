using Godot;
using System;
using System.Collections.Generic;

namespace Pastures;

public class TerrainMeshData
{
    // Mesh data structure.
    public List<Vector3> Vertices { get; private set; }
    public List<Vector2> UVs { get; private set; }
    public Vector3[] Normals { get; private set; }
    public List<int> Indices { get; private set; }

    // An array of index positions of the Indices list, corresponding to chunk index.
    // i.e. int[chunkx * chunksize + chunkz] returns an int list of positions in the indices list.
    private List<int>[] _chunkQuadIndicesLookup;

    private int _resolution;
    private int _vertexResolution;
    private int _chunkCount;
    private int _chunkResolution;

    private Vector2 _scale;
    private float _amplitude;

    private float _simplifyThreshold;

    public TerrainMeshData(Image heightmapImage, int resolution, int chunkCount, Vector2 size, float amplitude, float simplifyThreshold)
    {
        /* SET PRIVATES*/

        _chunkCount = chunkCount;
        _amplitude = amplitude;
        _simplifyThreshold = simplifyThreshold;

        // Initialise the chunk look-up table.
        _chunkQuadIndicesLookup = new List<int>[chunkCount * chunkCount];
        for (int i = 0; i < chunkCount * chunkCount; i++)
            _chunkQuadIndicesLookup[i] = new();

        // Initialise resolutions
        _resolution = resolution;
        _vertexResolution = _resolution + 1;
        _chunkResolution = _resolution / _chunkCount;

        // Set scale based on size and resolution.
        _scale = new
        (
            size.X / _resolution,
            size.Y / _resolution
        );

        // Initialise uniform mesh arrays.
        List<Vector3> uniformVerts = new();
        List<Vector2> uniformUVs = new();
        List<int> uniformIndices = new();

        // Generate uniform mesh.
        GenerateUniformMesh(heightmapImage, uniformVerts, uniformUVs, uniformIndices, out TerrainQuadCell[,] terrainQuadCells);
        Vector3[] uniformNormals = GenerateSmoothNormals(uniformVerts, uniformIndices);

        // Optimise mesh.
        BitwiseOptimiseTerrainQuadCells(_resolution, 0, 0, uniformNormals, terrainQuadCells);
        PopulateVertsFromTerrainQuadCells(terrainQuadCells, uniformVerts, uniformUVs);
        PopulateIndicesFromTerrainQuadCells(terrainQuadCells);
        Normals = GenerateSmoothNormals(Vertices, Indices);
        BitwiseCorrectGapsFromTerrainQuadCells(_resolution, terrainQuadCells);

    }

    /// <summary>
    /// Splits the mesh to the bounds of the chunk at index (X, Z).
    /// </summary>
    public void SplitMeshAtChunk(int chunkX, int chunkZ, Vector3 chunkPosition, out List<Vector3> chunkVertices, out List<Vector2> chunkUVs, out List<Vector3> chunkNormals, out List<int> chunkIndices)
    {
        // Initialise outs.
        // TODO: This could be a struct.
        chunkVertices = new();
        chunkUVs = new();
        chunkNormals = new();
        chunkIndices = new();

        // Don't consider if chunk is out of bounds.
        if (chunkX < 0 || chunkX >= _chunkCount || chunkZ < 0 || chunkZ >= _chunkCount)
        {
            throw new IndexOutOfRangeException($"Chunk at ({chunkX}, {chunkZ}) is out of bounds!");
        }

        Dictionary<int, int> chunkMeshVertIndexByMeshVertIndex = new();

        // Look through every quad in this chunk.
        foreach (int quadIndex in _chunkQuadIndicesLookup[chunkZ * _chunkCount + chunkX])
        {
            // Get four corners of quad from original mesh.
            int[] quadVertIndices = new int[4];
            quadVertIndices[(int)TerrainQuadCell.Corner.TopLeft] = Indices[quadIndex + 0];
            quadVertIndices[(int)TerrainQuadCell.Corner.TopRight] = Indices[quadIndex + 5];
            quadVertIndices[(int)TerrainQuadCell.Corner.BottomLeft] = Indices[quadIndex + 1];
            quadVertIndices[(int)TerrainQuadCell.Corner.BottomRight] = Indices[quadIndex + 2];

            // Array to store reference to new quad corners.
            int[] chunkQuadVertIndices = new int[4];

            // Look through each corner
            for (int i = 0; i < 4; i++)
            {
                // Does the dictionary have a key for the original index?
                if (chunkMeshVertIndexByMeshVertIndex.ContainsKey(quadVertIndices[i]))
                {
                    // Get reference to build indices.
                    chunkQuadVertIndices[i] = chunkMeshVertIndexByMeshVertIndex[quadVertIndices[i]];
                    continue;
                }

                // We haven't discovered this vertex yet. 

                // Add the key and update quad list.
                chunkMeshVertIndexByMeshVertIndex.Add(quadVertIndices[i], chunkVertices.Count);
                chunkQuadVertIndices[i] = chunkVertices.Count;


                // Copy the vertex info to chunk lists.
                chunkVertices.Add(Vertices[quadVertIndices[i]] - chunkPosition); // Correct vertex position too!
                chunkUVs.Add(UVs[quadVertIndices[i]]);
                chunkNormals.Add(Normals[quadVertIndices[i]]);

            }

            // Rebuild quad indices.

            // Triangle 1.
            chunkIndices.Add(chunkQuadVertIndices[(int)TerrainQuadCell.Corner.TopLeft]);
            chunkIndices.Add(chunkQuadVertIndices[(int)TerrainQuadCell.Corner.BottomLeft]);
            chunkIndices.Add(chunkQuadVertIndices[(int)TerrainQuadCell.Corner.BottomRight]);

            chunkIndices.Add(chunkQuadVertIndices[(int)TerrainQuadCell.Corner.TopLeft]);
            chunkIndices.Add(chunkQuadVertIndices[(int)TerrainQuadCell.Corner.BottomRight]);
            chunkIndices.Add(chunkQuadVertIndices[(int)TerrainQuadCell.Corner.TopRight]);
        }
    }

    private void GenerateUniformMesh(Image heightmapImage, List<Vector3> uniformVerts, List<Vector2> uniformUVs, List<int> uniformIndices, out TerrainQuadCell[,] terrainQuads)
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
                uniformVerts.Add(new Vector3(positionX, SampleHeight(uv, heightmapImage) * _amplitude, positionZ));

                // Set uvs.
                uniformUVs.Add(uv);

                // Build indices only if this isn't the first vertex
                if (z > 0 && x > 0)
                {
                    // Build quad.
                    int topLeft = z * _vertexResolution + (x - 1);
                    int topRight = z * _vertexResolution + x;
                    int bottomLeft = (z - 1) * _vertexResolution + (x - 1);
                    int bottomRight = (z - 1) * _vertexResolution + x;

                    // Add triangle 1 to indices.
                    uniformIndices.Add(topLeft);
                    uniformIndices.Add(bottomLeft);
                    uniformIndices.Add(bottomRight);

                    // Add triangle 2 to indices.
                    uniformIndices.Add(topLeft);
                    uniformIndices.Add(bottomRight);
                    uniformIndices.Add(topRight);

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

    private void BitwiseOptimiseTerrainQuadCells(int searchResolution, int originX, int originZ, Vector3[] uniformNormals, TerrainQuadCell[,] terrainQuadCells)
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
                if (halfResolution <= _chunkResolution)
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
                    if (standardDeviation < _simplifyThreshold)
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

                        continue;
                    }
                }

                // Uh oh, it's bumpy!
                // We need to keep searching to see if we can find any more nuggets of optimisation.

                BitwiseOptimiseTerrainQuadCells(halfResolution, originX + quadrantX, originZ + quadrantZ, uniformNormals, terrainQuadCells);
            }
        }
    }

    /// <summary>
    /// Populates verts and uvs list fields from generated terrain quad cells.
    /// </summary>
    private void PopulateVertsFromTerrainQuadCells(TerrainQuadCell[,] terrainQuadCells, List<Vector3> uniformVerts, List<Vector2> uniformUVs)
    {
        Vertices = new();
        UVs = new();

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
                        terrainQuadCells[x, z].cornerIndex[(int)TerrainQuadCell.Corner.BottomLeft] = Vertices.Count;

                        // Mark the vert to be copied.
                        copyVertAtIndex = terrainQuadCells[x, z].uniformCornerIndex[(int)TerrainQuadCell.Corner.BottomLeft];
                    }

                    // Quads with vertex at bottom right
                    if (x > 0 && terrainQuadCells[x - 1, z].IsCornerVertex(TerrainQuadCell.Corner.BottomRight))
                    {
                        // Set corner index to vert we're just about to add.
                        terrainQuadCells[x - 1, z].cornerIndex[(int)TerrainQuadCell.Corner.BottomRight] = Vertices.Count;

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
                        terrainQuadCells[x, z - 1].cornerIndex[(int)TerrainQuadCell.Corner.TopLeft] = Vertices.Count;

                        // Mark the vert to be copied.
                        copyVertAtIndex = terrainQuadCells[x, z - 1].uniformCornerIndex[(int)TerrainQuadCell.Corner.TopLeft];
                    }

                    //Quads with vertex at top right
                    if (x > 0 && terrainQuadCells[x - 1, z - 1].IsCornerVertex(TerrainQuadCell.Corner.TopRight))
                    {
                        // Set corner index to vert we're just about to add.
                        terrainQuadCells[x - 1, z - 1].cornerIndex[(int)TerrainQuadCell.Corner.TopRight] = Vertices.Count;

                        // Mark the vert to be copied.
                        copyVertAtIndex = terrainQuadCells[x - 1, z - 1].uniformCornerIndex[(int)TerrainQuadCell.Corner.TopRight];
                    }
                }

                // If the vert at index has been marked, copy into list.
                if (copyVertAtIndex >= 0)
                {
                    Vertices.Add(uniformVerts[copyVertAtIndex]);
                    UVs.Add(uniformUVs[copyVertAtIndex]);
                }
            }
        }
    }

    private void PopulateIndicesFromTerrainQuadCells(TerrainQuadCell[,] terrainQuadCells)
    {
        // Populate indices from terrain quad cells.
        Indices = new();

        // Find a valid starting quad cell.
        for (int z = 0; z < _resolution; z++)
        {
            for (int x = 0; x < _resolution; x++)
            {
                // We only want to look at cells which have a bottom left hand vertex, indicating the start of a quad.
                // Anything else is a part of a quad.
                if (!terrainQuadCells[x, z].IsCornerVertex(TerrainQuadCell.Corner.BottomLeft))
                    continue;

                // Figure out which chunk this quad lies in.
                int chunkX = (x - x % _chunkResolution) / _chunkResolution;
                int chunkZ = (z - z % _chunkResolution) / _chunkResolution;
                int chunkIndex = chunkZ * _chunkCount + chunkX;

                // Define an array representing the indices of the four vertices making up the quad.
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

                // Store the list-position of quad indices in the chunkIndicesLookup table.
                _chunkQuadIndicesLookup[chunkIndex].Add(Indices.Count);

                // Triangle 1.
                Indices.Add(quadCorners[(int)TerrainQuadCell.Corner.TopLeft]);
                Indices.Add(quadCorners[(int)TerrainQuadCell.Corner.BottomLeft]);
                Indices.Add(quadCorners[(int)TerrainQuadCell.Corner.BottomRight]);

                // Triangle 2.
                Indices.Add(quadCorners[(int)TerrainQuadCell.Corner.TopLeft]);
                Indices.Add(quadCorners[(int)TerrainQuadCell.Corner.BottomRight]);
                Indices.Add(quadCorners[(int)TerrainQuadCell.Corner.TopRight]);

            }
        }
    }


    /// <summary>
    /// Corrects gaps in mesh, introduced by simplifying, by searching for largest quads and moving surrounding verts.
    /// </summary>
    public void BitwiseCorrectGapsFromTerrainQuadCells(int searchResolution, TerrainQuadCell[,] terrainQuadCells)
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
                            float weight = (Vertices[vertIndex].X - Vertices[bottomLeftIndex].X) / (Vertices[bottomRightIndex].X - Vertices[bottomLeftIndex].X);

                            // Set height along quad edge.
                            Vertices[vertIndex] = new(Vertices[vertIndex].X, Mathf.Lerp(Vertices[bottomLeftIndex].Y, Vertices[bottomRightIndex].Y, weight), Vertices[vertIndex].Z);

                            // Set normal along quad edge.
                            Normals[vertIndex] = Normals[bottomLeftIndex].Lerp(Normals[bottomRightIndex], weight);
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
                            float weight = (Vertices[vertIndex].X - Vertices[topLeftIndex].X) / (Vertices[topRightIndex].X - Vertices[topLeftIndex].X);

                            // Set height along quad edge.
                            Vertices[vertIndex] = new(Vertices[vertIndex].X, Mathf.Lerp(Vertices[topLeftIndex].Y, Vertices[topRightIndex].Y, weight), Vertices[vertIndex].Z);

                            // Set normal along quad edge.
                            Normals[vertIndex] = Normals[topLeftIndex].Lerp(Normals[topRightIndex], weight);
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
                            float weight = (Vertices[vertIndex].Z - Vertices[bottomLeftIndex].Z) / (Vertices[topLeftIndex].Z - Vertices[bottomLeftIndex].Z);

                            // Set height along quad edge.
                            Vertices[vertIndex] = new(Vertices[vertIndex].X, Mathf.Lerp(Vertices[bottomLeftIndex].Y, Vertices[topLeftIndex].Y, weight), Vertices[vertIndex].Z);

                            // Set normal along quad edge.
                            Normals[vertIndex] = Normals[bottomLeftIndex].Lerp(Normals[topLeftIndex], weight);
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
                            float weight = (Vertices[vertIndex].Z - Vertices[bottomRightIndex].Z) / (Vertices[topRightIndex].Z - Vertices[bottomRightIndex].Z);

                            // Set height along quad edge.
                            Vertices[vertIndex] = new(Vertices[vertIndex].X, Mathf.Lerp(Vertices[bottomRightIndex].Y, Vertices[topRightIndex].Y, weight), Vertices[vertIndex].Z);

                            // Set normal along quad edge.
                            Normals[vertIndex] = Normals[bottomRightIndex].Lerp(Normals[topRightIndex], weight);
                        }
                    }
                }
            }
        }

        BitwiseCorrectGapsFromTerrainQuadCells(halfResolution, terrainQuadCells);
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
}