using Godot;
using System;
using System.Collections.Generic;

namespace Pastures;

[Tool]
public partial class TerrainGenerator : Node3D
{
    [ExportCategory("Terrain Generator Settings")]
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
        }
    }

    [Export] public float Amplitude { get; set; } = 16.0f;

    [Export] public ShaderMaterial TerrainMaterial { get; set; }

    [ExportGroup("Mesh Settings")]
    [Export]
    public int Resolution
    {
        get => _resolution;

        // Make sure resolution is never negative, and is always divisible by 2.
        set
        {
            // Always enforce a minimum size of 32. (Do while will bitshift this 16 to 32)
            _resolution = 16;

            // Bitshift clamp between 32 and 8196 (although this upper value will likely be utterly overkill).
            do
            {
                _resolution <<= 1;
            }
            while (_resolution < value && _resolution < 8192);
        }
    }

    [Export]
    public int ChunkCount
    {
        get => _chunkCount;

        set
        {
            // Minimum chunk count is two. (Do while will bitshift this 1 to a 2.)
            _chunkCount = 1;

            // Find the next lowest power of two from the value by bitshifting up.
            do
            {
                _chunkCount <<= 1;
            }
            while (_chunkCount < value && _chunkCount < _resolution >> 1);

            // Value is either half of resolution or the next lowest power of two to value.
        }
    }

    [Export] public float SimplifyThreshold { get; set; } = 0.005f;

    [Export] public TerrainGeneratorLOD[] LevelOfDetailSettings;


    [ExportGroup("Terrain Generation Actions")]
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

    [Export]
    public bool DeleteChunks
    {
        get => false;
        set
        {
            if (value)
                ClearData();
        }
    }

    private Vector2 _size = new(32.0f, 32.0f);
    private int _resolution = 128;
    private int _chunkCount = 4;


    public void OnGenerateMesh()
    {
        if (Heightmap == null)
            throw new NullReferenceException("Heightmap is not set!");

        Image heightmapImage = Heightmap.GetImage();



        // Delete child nodes.
        ClearData();

        // Iterate through LOD levels.
        for (int i = 0; i < LevelOfDetailSettings.Length; i++)
        {
            if (i == 0 && LevelOfDetailSettings[i].LOD_Level != 0)
            {
                throw new Exception("First element of Level of Detail Settings must have an LOD Level of 0.");
            }

            int lodResolution = _resolution >> LevelOfDetailSettings[i].LOD_Level;

            if (_resolution < 32)
            {
                GD.PrintErr
                (
                    $"LOD {LevelOfDetailSettings[i].LOD_Level} at index {i} causes resolution to be below 32. (Resolution is now {lodResolution}...)\n" +
                    "Try increasing base resolution or adjusting Level Of Detail Settings."
                );

                continue;
            }

            // Create new material for this LOD level, or use the base material for LOD 0.
            LevelOfDetailSettings[i].LOD_Material = i > 0 ? (ShaderMaterial)TerrainMaterial.Duplicate() : TerrainMaterial;
            LevelOfDetailSettings[i].UpdateMaterialProperties();

            // Generate full mesh data.
            TerrainMeshData data = new
            (
                heightmapImage,
                lodResolution, // Bitshift down based on LOD Level.
                _chunkCount,
                _size,
                Amplitude,
                SimplifyThreshold
            );

            // Create chunk nodes.
            int chunkCount = ChunkCount; // Cache result.
            Vector2 chunkSize = _size / chunkCount;
            for (int chunkZ = 0; chunkZ < chunkCount; chunkZ++)
            {
                for (int chunkX = 0; chunkX < chunkCount; chunkX++)
                {
                    // Calculate chunk centre.
                    Vector3 chunkPosition =
                        new
                        (
                            x: (chunkX - chunkCount / 2) * chunkSize.X + chunkSize.X / 2,
                            y: 0,
                            z: (chunkZ - chunkCount / 2) * chunkSize.Y + chunkSize.Y / 2
                        );

                    // Create node at position
                    MeshInstance3D meshInstance = new()
                    {
                        Name = $"Chunk ({chunkX}, {chunkZ}) : LOD {LevelOfDetailSettings[i].LOD_Level}",

                        // Additional LOD's are children of the LOD 0 node, so don't set a position for anything above LOD 0.
                        Position = LevelOfDetailSettings[i].LOD_Level == 0 ? chunkPosition : Vector3.Zero,

                        // Set to the terrain layer.
                        Layers = 0b10,

                        VisibilityRangeBegin = LevelOfDetailSettings[i].Begin,
                        VisibilityRangeBeginMargin = LevelOfDetailSettings[i].BeginMargin,
                        VisibilityRangeEnd = LevelOfDetailSettings[i].End,
                        VisibilityRangeEndMargin = LevelOfDetailSettings[i].EndMargin,
                        VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled
                    };

                    // Add to scene tree.
                    if (LevelOfDetailSettings[i].LOD_Level == 0)
                    {
                        AddChild(meshInstance);
                    }
                    else
                    {
                        GetChild(chunkZ * chunkCount + chunkX, true).AddChild(meshInstance);
                    }
                    // Allow for saving.
                    meshInstance.Owner = GetTree().EditedSceneRoot;

                    // Create thread to split mesh to new array mesh.
                    // Init arrays.
                    Godot.Collections.Array surfaceArray = new();
                    surfaceArray.Resize((int)Mesh.ArrayType.Max);

                    // Split mesh.
                    data.SplitMeshAtChunk(chunkX, chunkZ, chunkPosition, out List<Vector3> chunkVertices, out List<Vector2> chunkUVs, out List<Vector3> chunkNormals, out List<int> chunkIndices);

                    // Apply lists to arrays.
                    Vector3[] chunkVertexArray = chunkVertices.ToArray();
                    surfaceArray[(int)Mesh.ArrayType.Vertex] = chunkVertexArray;
                    surfaceArray[(int)Mesh.ArrayType.TexUV] = chunkUVs.ToArray();
                    surfaceArray[(int)Mesh.ArrayType.Normal] = chunkNormals.ToArray();
                    surfaceArray[(int)Mesh.ArrayType.Index] = chunkIndices.ToArray();

                    // Set the mesh.
                    ArrayMesh arrMesh = new();
                    arrMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surfaceArray);
                    arrMesh.SurfaceSetMaterial(0, LevelOfDetailSettings[i].LOD_Material);
                    meshInstance.Mesh = arrMesh;

                    // Create physics mesh on LOD 0.
                    if (LevelOfDetailSettings[i].LOD_Level == 0)
                        meshInstance.CreateTrimeshCollision();
                }
            }
        }
    }

    public void ClearData()
    {
        // Remove any pre-existing child nodes.
        while (GetChildCount(true) > 0)
        {
            GetChild(0, true).Free();
        }
    }
}